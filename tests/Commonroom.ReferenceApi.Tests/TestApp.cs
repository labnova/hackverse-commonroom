using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Commonroom.ReferenceApi.Engine;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Commonroom.ReferenceApi.Tests;

/// <summary>
/// Reference API with Chat:Engine=Fake, a temp SQLite file and a local test JwtBearer
/// (symmetric key that exists only in this test project).
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    public const string Region = "test-1";
    public const string UserPoolId = "test-1_TestPool";
    public const string ClientId = "test-client";
    public const string ApiKey = "test-key";
    public const string AllowedStore = "vs_alpha";
    public const string OtherAllowedStore = "vs_gamma";

    public static string Issuer => $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}";

    private static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "test-key-1" };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"commonroom-test-{Guid.NewGuid():N}.db");

    public RecordingChatEngine Engine { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Cognito:Region", Region);
        builder.UseSetting("Cognito:UserPoolId", UserPoolId);
        builder.UseSetting("Cognito:ClientId", ClientId);
        builder.UseSetting("ApiKeys:Allowed:0", ApiKey);
        builder.UseSetting("VectorStores:Allowed:0", AllowedStore);
        builder.UseSetting("VectorStores:Allowed:1", OtherAllowedStore);
        builder.UseSetting("Chat:Engine", "Fake");
        builder.UseSetting("ConnectionStrings:Chat", $"Data Source={_dbPath}");

        builder.ConfigureTestServices(services =>
        {
            // Validate locally signed tokens instead of fetching Cognito JWKS.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.ConfigurationManager = null;
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
                o.TokenValidationParameters.ValidIssuer = Issuer;
            });
            services.AddSingleton<IChatEngine>(Engine);
        });
    }

    public static string Token(string sub, string clientId = ClientId, string tokenUse = "access", string[]? groups = null)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            IssuedAt = now,
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = sub,
                ["client_id"] = clientId,
                ["token_use"] = tokenUse,
                ["username"] = sub,
                ["cognito:groups"] = groups ?? ["members"],
            },
        });
    }

    public HttpClient ClientFor(string sub, string clientId = ClientId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(sub, clientId));
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}

/// <summary>Fake engine that records the vector store each call received.</summary>
public sealed class RecordingChatEngine : IChatEngine
{
    private readonly FakeChatEngine _inner = new();

    public ConcurrentDictionary<string, string?> LastVectorStoreByText { get; } = new();

    public Task<ChatAnswer> Answer(IReadOnlyList<ChatTurn> history, string userText, string? vectorStoreId, CancellationToken ct)
    {
        LastVectorStoreByText[userText] = vectorStoreId;
        return _inner.Answer(history, userText, vectorStoreId, ct);
    }
}
