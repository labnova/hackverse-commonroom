using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Commonroom.ReferenceApi;
using Commonroom.ReferenceApi.Api;
using Commonroom.ReferenceApi.Data;
using Commonroom.ReferenceApi.Engine;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var cognito = builder.Configuration.GetSection("Cognito").Get<CognitoOptions>() ?? new CognitoOptions();
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("ApiKeys"));
builder.Services.Configure<VectorStoreOptions>(builder.Configuration.GetSection("VectorStores"));
builder.Services.AddMessageRateLimit(builder.Configuration);

builder.Services.AddDbContext<ChatDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Chat") ?? "Data Source=commonroom-ref.db"));

var chatEngine = builder.Configuration["Chat:Engine"] is { Length: > 0 } e ? e : "Fake";
if (string.Equals(chatEngine, "Foundry", StringComparison.OrdinalIgnoreCase))
{
    var foundry = builder.Configuration.GetSection("Foundry").Get<FoundryOptions>() ?? new FoundryOptions();
    builder.Services.AddSingleton<IChatEngine>(new FoundryChatEngine(foundry));
}
else if (string.Equals(chatEngine, "Fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IChatEngine, FakeChatEngine>();
}
else
{
    throw new InvalidOperationException($"Unknown Chat:Engine '{chatEngine}'. Use 'Fake' or 'Foundry'.");
}

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

// Minimal API binding failures always throw, so BadRequestExceptionHandler answers 400 problem+json (never 500).
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
    c.ProblemDetails.Extensions.TryAdd("correlation_id", CorrelationId.Get(c.HttpContext)));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = cognito.Authority;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = cognito.Authority,
            // Cognito access tokens carry no "aud": the client is checked via client_id below.
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "username",
            RoleClaimType = "cognito:groups",
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                var principal = ctx.Principal;
                if (principal?.FindFirstValue("token_use") != "access")
                {
                    ctx.Fail("token_use must be 'access'.");
                }
                else if (string.IsNullOrEmpty(cognito.ClientId) ||
                         !string.Equals(principal.FindFirstValue("client_id"), cognito.ClientId, StringComparison.Ordinal))
                {
                    ctx.Fail("Token was not issued for this project's client_id.");
                }
                return Task.CompletedTask;
            },
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.Headers.WWWAuthenticate = "Bearer";
                var detail = ctx.AuthenticateFailure is null
                    ? "A Cognito access token is required (Authorization: Bearer <token>)."
                    : "The bearer token is invalid, expired or not issued for this project.";
                await Problems.WriteAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, detail);
            },
            OnForbidden = ctx => Problems.WriteAsync(ctx.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "The authenticated user is not allowed to perform this operation."),
            OnAuthenticationFailed = ctx =>
            {
                ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Commonroom.Auth")
                    .LogWarning("Bearer authentication failed: {Reason}", ctx.Exception.Message);
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length > 0)
    {
        p.WithOrigins(allowedOrigins);
    }
    p.WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
     .WithHeaders("Authorization", "Content-Type", "Accept",
         Headers.CorrelationId, Headers.SessionId, Headers.UserIdentity, Headers.ApiKey)
     .WithExposedHeaders(Headers.CorrelationId, Headers.SessionId, Headers.Contract)
     .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
}));

var app = builder.Build();

if (!cognito.IsConfigured)
{
    app.Logger.LogWarning("Cognito:Region/UserPoolId/ClientId not configured: every authenticated request will be rejected.");
}
app.Logger.LogInformation("Chat engine: {Engine}", chatEngine);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDb>();
    // SQLite does not create folders (e.g. /home/data on App Service).
    var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
    if (Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
    db.Database.Migrate();
}

app.UseCorrelationId();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseApiKey();
app.UseUserIdentityHeader();
app.UseRateLimiter();

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" })).AllowAnonymous();

app.MapGet("/me", (HttpContext ctx) =>
{
    var user = ctx.User;
    var sub = user.FindFirstValue("sub");
    var header = UserIdentityHeader.Get(ctx);
    return TypedResults.Ok(new MeResponse(
        Sub: sub,
        Email: user.FindFirstValue("email"),
        Roles: user.FindAll("cognito:groups").Select(c => c.Value).ToArray(),
        ClientId: user.FindFirstValue("client_id"),
        HeaderIdentityMatches: header is not null && sub is not null && string.Equals(header.UserId, sub, StringComparison.Ordinal)));
});

app.MapConversations();

app.MapFallback((HttpContext ctx) =>
    Problems.Result(ctx, StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"No resource at {ctx.Request.Path}."));

app.Run();

public sealed record MeResponse(string? Sub, string? Email, string[] Roles, string? ClientId, bool HeaderIdentityMatches);

public partial class Program;
