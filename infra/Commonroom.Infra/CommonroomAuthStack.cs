using System.Text;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Constructs;

namespace Commonroom.Infra;

public sealed class CommonroomAuthStack : Stack
{
    private static readonly string[] DefaultProjects = ["demo-host", "project-alpha"];
    private static readonly string[] Groups = ["members", "curators"];

    public CommonroomAuthStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
        var projects = ReadProjects();
        var devAuth = ReadBool("devAuth");
        var callbackUrls = ReadUrlList("callbackUrls", ReadString("callbackUrl") ?? "http://localhost:5173/callback");
        var logoutUrls = ReadUrlList("logoutUrls", ReadString("logoutUrl") ?? "http://localhost:5173/");
        var domainPrefix = ReadString("domainPrefix") ?? $"commonroom-{Aws.ACCOUNT_ID}";

        var pool = new UserPool(this, "UserPool", new UserPoolProps
        {
            UserPoolName = "commonroom-users",
            SelfSignUpEnabled = false,
            SignInAliases = new SignInAliases { Email = true },
            SignInCaseSensitive = false,
            AutoVerify = new AutoVerifiedAttrs { Email = true },
            StandardAttributes = new StandardAttributes
            {
                Email = new StandardAttribute { Required = true, Mutable = true },
            },
            PasswordPolicy = new PasswordPolicy
            {
                MinLength = 12,
                RequireLowercase = true,
                RequireUppercase = true,
                RequireDigits = true,
                RequireSymbols = false,
            },
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            // TOTP (authenticator app) only: no SMS (cost), optional per user.
            Mfa = Mfa.OPTIONAL,
            MfaSecondFactor = new MfaSecondFactor { Otp = true, Sms = false },
            EnableSmsRole = false,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        foreach (var group in Groups)
        {
            _ = new CfnUserPoolGroup(this, $"Group{Pascal(group)}", new CfnUserPoolGroupProps
            {
                UserPoolId = pool.UserPoolId,
                GroupName = group,
                Description = $"Commonroom Chat {group}",
            });
        }

        var domain = pool.AddDomain("HostedUiDomain", new UserPoolDomainOptions
        {
            CognitoDomain = new CognitoDomainOptions { DomainPrefix = domainPrefix },
        });

        _ = new CfnOutput(this, "UserPoolId", new CfnOutputProps { Value = pool.UserPoolId });
        _ = new CfnOutput(this, "Region", new CfnOutputProps { Value = Aws.REGION });
        _ = new CfnOutput(this, "HostedUiDomain", new CfnOutputProps { Value = domain.BaseUrl() });

        foreach (var project in projects)
        {
            // Public client (no secret) + Authorization Code grant: the host must send
            // code_challenge / code_challenge_method=S256 (PKCE) on /oauth2/authorize.
            var client = pool.AddClient($"Client{Pascal(project)}", new UserPoolClientOptions
            {
                UserPoolClientName = project,
                GenerateSecret = false,
                OAuth = new OAuthSettings
                {
                    Flows = new OAuthFlows { AuthorizationCodeGrant = true, ImplicitCodeGrant = false, ClientCredentials = false },
                    Scopes = [OAuthScope.OPENID, OAuthScope.EMAIL, OAuthScope.PROFILE],
                    // Shared list (callbackUrls) + per-project list (callbackUrls:<project>) from context.
                    CallbackUrls = callbackUrls.Concat(ReadUrlList($"callbackUrls:{project}", null)).Distinct().ToArray(),
                    LogoutUrls = logoutUrls.Concat(ReadUrlList($"logoutUrls:{project}", null)).Distinct().ToArray(),
                },
                SupportedIdentityProviders = [UserPoolClientIdentityProvider.COGNITO],
                PreventUserExistenceErrors = true,
                EnableTokenRevocation = true,
                AccessTokenValidity = Duration.Hours(1),
                IdTokenValidity = Duration.Hours(1),
                RefreshTokenValidity = Duration.Days(30),
            });

            // Explicit flows: refresh always; USER_PASSWORD only for dev (CLI token in README).
            var flows = new List<string> { "ALLOW_REFRESH_TOKEN_AUTH" };
            if (devAuth)
            {
                flows.Add("ALLOW_USER_PASSWORD_AUTH");
            }
            ((CfnUserPoolClient)client.Node.DefaultChild!).ExplicitAuthFlows = flows.ToArray();

            _ = new CfnOutput(this, $"ClientId{Pascal(project)}", new CfnOutputProps
            {
                Value = client.UserPoolClientId,
                Description = $"Cognito app client id for project '{project}'",
            });
        }
    }

    /// <summary>Absolute URL list from context (JSON array or comma list); https only, http allowed for localhost.</summary>
    private string[] ReadUrlList(string key, string? fallback)
    {
        var raw = Node.TryGetContext(key);
        IEnumerable<string> values = raw switch
        {
            null => fallback is null ? [] : [fallback],
            string s when s.TrimStart().StartsWith('[') => JsonSerializer.Deserialize<string[]>(s) ?? [],
            string s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            System.Collections.IEnumerable e => e.Cast<object?>().Select(o => o?.ToString() ?? string.Empty),
            _ => fallback is null ? [] : [fallback],
        };

        var result = values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToArray();
        if (result.Length == 0 && fallback is not null) result = [fallback];
        foreach (var url in result)
        {
            var ok = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                     (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
            if (!ok) throw new ArgumentException($"Context '{key}': '{url}' must be https (http only for localhost).");
        }
        return result;
    }

    private string[] ReadProjects()
    {
        var raw = Node.TryGetContext("projects");
        IEnumerable<string> values = raw switch
        {
            null => DefaultProjects,
            string s when s.TrimStart().StartsWith('[') => JsonSerializer.Deserialize<string[]>(s) ?? DefaultProjects,
            string s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            System.Collections.IEnumerable e => e.Cast<object?>().Select(o => o?.ToString() ?? string.Empty),
            _ => DefaultProjects,
        };

        var result = values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToArray();
        if (result.Length == 0)
        {
            throw new ArgumentException("Context 'projects' must contain at least one project name.");
        }

        foreach (var p in result)
        {
            if (!p.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                throw new ArgumentException($"Invalid project name '{p}': use letters, digits, '-' or '_'.");
            }
        }

        return result;
    }

    private bool ReadBool(string key) => Node.TryGetContext(key) switch
    {
        bool b => b,
        string s => bool.TryParse(s, out var b) && b,
        _ => false,
    };

    private string? ReadString(string key) =>
        Node.TryGetContext(key) is string s && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static string Pascal(string value)
    {
        var sb = new StringBuilder();
        foreach (var part in value.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(char.ToUpperInvariant(part[0])).Append(part[1..]);
        }
        return sb.ToString();
    }
}
