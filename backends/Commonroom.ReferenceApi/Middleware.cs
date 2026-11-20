using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Commonroom.ReferenceApi;

public static class Headers
{
    public const string CorrelationId = "X-Correlation-Id";
    public const string SessionId = "X-Session-Id";
    public const string UserIdentity = "X-User-Identity";
    public const string ApiKey = "X-Api-Key";
    public const string Contract = "X-Commonroom-Contract";
    public const string ContractVersion = "commonroom-chat/0.2.0";
}

/// <summary>Echoes X-Correlation-Id (generated when absent or malformed) on every response.</summary>
public static class CorrelationId
{
    private const string ItemKey = "commonroom.correlation_id";
    private const int MaxLength = 128;

    public static string Get(HttpContext ctx) => ctx.Items[ItemKey] as string ?? string.Empty;

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var incoming = ctx.Request.Headers[Headers.CorrelationId].ToString();
        var id = IsSafe(incoming) ? incoming : Guid.NewGuid().ToString("N");
        ctx.Items[ItemKey] = id;
        ctx.TraceIdentifier = id;

        var sessionId = ctx.Request.Headers[Headers.SessionId].ToString();
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Headers.CorrelationId] = id;
            ctx.Response.Headers[Headers.Contract] = Headers.ContractVersion;
            if (IsSafe(sessionId))
            {
                ctx.Response.Headers[Headers.SessionId] = sessionId;
            }
            return Task.CompletedTask;
        });

        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Commonroom.Correlation");
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next();
        }
    });

    private static bool IsSafe(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaxLength &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
}

/// <summary>Requires a valid X-Api-Key on every request except /health and CORS preflight.</summary>
public static class ApiKeyMiddleware
{
    public static IApplicationBuilder UseApiKey(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (HttpMethods.IsOptions(ctx.Request.Method) || ctx.Request.Path.StartsWithSegments("/health"))
        {
            await next();
            return;
        }

        var allowed = ctx.RequestServices.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>().CurrentValue.Allowed;
        var provided = ctx.Request.Headers[Headers.ApiKey].ToString();

        if (!IsAllowed(provided, allowed))
        {
            ctx.Response.Headers.WWWAuthenticate = "ApiKey header=\"X-Api-Key\"";
            await Problems.WriteAsync(ctx, StatusCodes.Status401Unauthorized, ErrorCodes.InvalidApiKey,
                string.IsNullOrEmpty(provided) ? "Missing X-Api-Key header." : "X-Api-Key is not valid.");
            return;
        }

        await next();
    });

    private static bool IsAllowed(string provided, string[] allowed)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var match = false;
        foreach (var key in allowed)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }
            match |= CryptographicOperations.FixedTimeEquals(providedBytes, Encoding.UTF8.GetBytes(key));
        }
        return match;
    }
}

public sealed record UserIdentifier(
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("user_email")] string? UserEmail,
    [property: JsonPropertyName("user_role")] string? UserRole);

/// <summary>
/// Parses X-User-Identity for logging/diagnostics only. It is NEVER used for authorization:
/// the effective identity always comes from the validated access token.
/// </summary>
public static class UserIdentityHeader
{
    private const string ItemKey = "commonroom.header_identity";
    private const int MaxLength = 4096;

    public static UserIdentifier? Get(HttpContext ctx) => ctx.Items[ItemKey] as UserIdentifier;

    public static IApplicationBuilder UseUserIdentityHeader(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var raw = ctx.Request.Headers[Headers.UserIdentity].ToString();
        if (!string.IsNullOrEmpty(raw) && ctx.User.Identity?.IsAuthenticated == true)
        {
            var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Commonroom.UserIdentityHeader");
            var sub = ctx.User.FindFirstValue("sub");
            var parsed = TryParse(raw);

            if (parsed is null)
            {
                logger.LogWarning("Ignoring malformed {Header} header for sub {Sub}", Headers.UserIdentity, sub);
            }
            else
            {
                ctx.Items[ItemKey] = parsed;
                if (!string.Equals(parsed.UserId, sub, StringComparison.Ordinal))
                {
                    logger.LogWarning(
                        "{Header} user_id {HeaderUserId} does not match token sub {Sub}; header ignored for identity",
                        Headers.UserIdentity, parsed.UserId, sub);
                }
                else
                {
                    logger.LogInformation("{Header} received: user_id={HeaderUserId} user_role={HeaderUserRole}",
                        Headers.UserIdentity, parsed.UserId, parsed.UserRole);
                }
            }
        }

        await next();
    });

    private static UserIdentifier? TryParse(string raw)
    {
        if (raw.Length > MaxLength)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UserIdentifier>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
