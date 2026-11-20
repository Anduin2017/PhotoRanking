using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Anduin.PhotoRanking.Services;

public static class RankingSession
{
    public const string Scheme = "PhotoRanking.OidcSession";
    public const string CookieName = "__Host-PhotoRanking";
    public const string CallbackPath = "/signin-oidc";
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);

    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("RankingOidc");
        if (!section.Exists()) return;

        var authority = ReadHttpsUrl(section, "Authority");
        var publicOrigin = ReadHttpsUrl(section, "PublicOrigin");
        if (publicOrigin.AbsolutePath != "/")
            throw new InvalidOperationException("RankingOidc:PublicOrigin must be an HTTPS origin.");
        var clientId = RequiredValue(section, "ClientId");
        var clientSecret = RequiredValue(section, "ClientSecret");
        services.AddSingleton(new Enabled(publicOrigin.GetLeftPart(UriPartial.Authority)));

        services.AddAuthentication(Scheme)
            .AddCookie(Scheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.Path = "/";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.MaxAge = Lifetime;
                options.ExpireTimeSpan = Lifetime;
                options.SlidingExpiration = false;
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = authority.AbsoluteUri;
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.SignInScheme = Scheme;
                options.CallbackPath = CallbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.UseTokenLifetime = false;
                options.SaveTokens = false;
                options.MapInboundClaims = false;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.RedirectUri = new Uri(publicOrigin, CallbackPath).AbsoluteUri;
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.CacheControl = "no-store";
                    await context.Response.WriteAsync("Sign-in could not be completed. Open PhotoRanking to try again.");
                };
            });
    }

    public static void Use(WebApplication app)
    {
        if (app.Services.GetService<Enabled>() is not { } settings) return;

        // Caddy's new upstream is available only when native authentication is configured.
        app.Urls.Add("http://*:5000");
        app.Urls.Add("http://*:5001");
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/sign-out")
            {
                await context.SignOutAsync(Scheme);
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync("Signed out of PhotoRanking.");
                return;
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                await next();
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Path.StartsWithSegments("/api") ||
                (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
            {
                RedirectUri = settings.PublicOrigin + context.Request.GetEncodedPathAndQuery(),
                IsPersistent = true
            });
        });
    }

    private static string RequiredValue(IConfiguration section, string name) =>
        section[name] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"RankingOidc:{name} is required.");

    private static Uri ReadHttpsUrl(IConfiguration section, string name)
    {
        if (!Uri.TryCreate(RequiredValue(section, name), UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
            throw new InvalidOperationException($"RankingOidc:{name} must be an HTTPS URL.");
        return url;
    }

    private sealed record Enabled(string PublicOrigin);
}
