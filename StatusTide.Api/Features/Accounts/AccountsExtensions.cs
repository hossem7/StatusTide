using System.Text;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using StatusTide.Api.Features.Dashboard;
using StatusTide.Api.Features.Guests;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Accounts;

public class JwtOptions
{
    /// <summary>At least 32 bytes. Comes from the environment outside development.</summary>
    public string SigningKey { get; set; } = "";
    public string Issuer { get; set; } = "statustide";
    public string Audience { get; set; } = "statustide";
}

public static class AccountsExtensions
{
    public const string RateLimitPolicy = "accounts";

    /// <summary>Password and email attempts one address may make per minute.</summary>
    public const int AttemptsPerMinute = 10;

    /// <summary>Picks JWT or the guest cookie for each request. See <see cref="AddAccounts"/>.</summary>
    public const string SessionScheme = "Session";

    private const string QueryToken = "access_token";

    /// <summary>
    /// Registers accounts and makes sign-in choose per request: a request carrying a JWT is judged
    /// by that JWT alone, and anything else falls back to the guest cookie. A bad JWT therefore
    /// gets a 401 the client can refresh from, rather than quietly becoming a guest.
    /// Call this after <see cref="GuestExtensions.AddGuests"/>.
    /// </summary>
    public static IServiceCollection AddAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .Validate(jwt => Encoding.UTF8.GetByteCount(jwt.SigningKey) >= 32, "Jwt:SigningKey must be at least 32 bytes.")
            .ValidateOnStart();

        services.AddAuthentication(SessionScheme)
            .AddJwtBearer()
            .AddPolicyScheme(SessionScheme, SessionScheme, scheme =>
                scheme.ForwardDefaultSelector = http => HasJwt(http.Request)
                    ? JwtBearerDefaults.AuthenticationScheme
                    : GuestAuthenticationHandler.SchemeName);

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                bearer.Events = new JwtBearerEvents
                {
                    // Browsers can't set headers on a WebSocket, so SignalR sends the token in the
                    // query string. Only the hub accepts that. Request logs leave out the query.
                    OnMessageReceived = context =>
                    {
                        if (IsHub(context.Request) && context.Request.Query[QueryToken].ToString() is { Length: > 0 } token)
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<AccessTokenIssuer>();
        services.AddScoped<RefreshSessions>();
        services.AddScoped<RegisterCommand>();
        services.AddScoped<SignInCommand>();
        services.AddScoped<ConfirmEmailCommand>();

        services.AddRateLimiter(limiter => limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = AttemptsPerMinute, Window = TimeSpan.FromMinutes(1) })));

        return services;
    }

    private static bool IsHub(HttpRequest request) => request.Path.StartsWithSegments(StatusHub.Path);

    private static bool HasJwt(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        || (IsHub(request) && request.Query.ContainsKey(QueryToken));
}
