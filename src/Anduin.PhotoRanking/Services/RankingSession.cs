using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;

namespace Anduin.PhotoRanking.Services;

public sealed class RankingSession(HttpClient client, RankingSession.Settings settings)
{
    public const string Scheme = "PhotoRanking";
    public const string CookieName = "__Host-PhotoRanking";
    public const string Port = "5001";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);

    public sealed record Settings(Uri Outpost, Uri PublicOrigin, string Application)
    {
        public static Settings? Read(IConfiguration configuration)
        {
            var section = configuration.GetSection("RankingSession");
            if (!section.Exists()) return null;

            var outpost = ReadOrigin(section, "Outpost", allowHttp: true);
            var publicOrigin = ReadOrigin(section, "PublicOrigin", allowHttp: false);
            var application = section["Application"];
            if (string.IsNullOrWhiteSpace(application))
                throw new InvalidOperationException("RankingSession:Application is required.");
            return new Settings(outpost, publicOrigin, application);
        }

        private static Uri ReadOrigin(IConfiguration section, string name, bool allowHttp)
        {
            if (!Uri.TryCreate(section[name], UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp)) ||
                uri.PathAndQuery != "/" || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
                throw new InvalidOperationException($"RankingSession:{name} must be an HTTP(S) origin.");
            return uri;
        }
    }

    public async Task<bool> AuthenticateAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(Scheme);
        if (result.Succeeded && result.Principal is { } principal)
        {
            context.User = principal;
            return true;
        }

        var request = context.Request;
        context.Response.Headers.CacheControl = "no-store";
        var hasAuthorizationHeader = request.Headers.Authorization.Count > 0;
        var canRedirect = !hasAuthorizationHeader && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method));

        var proxyCookies = request.Headers.Cookie
            .SelectMany(value => value?.Split(';', StringSplitOptions.TrimEntries) ?? [])
            .Where(value => value.StartsWith("authentik_proxy_", StringComparison.Ordinal));
        var cookie = string.Join("; ", proxyCookies);
        if (cookie.Length == 0 && !hasAuthorizationHeader)
        {
            Challenge(context, canRedirect);
            return false;
        }

        using var check = new HttpRequestMessage(HttpMethod.Get, new Uri(settings.Outpost, "/outpost.goauthentik.io/auth/caddy"));
        if (cookie.Length > 0) check.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (hasAuthorizationHeader) check.Headers.TryAddWithoutValidation("Authorization", request.Headers.Authorization.AsEnumerable());
        check.Headers.Add("X-Forwarded-Proto", settings.PublicOrigin.Scheme);
        check.Headers.Add("X-Forwarded-Host", settings.PublicOrigin.Authority);
        check.Headers.Add("X-Forwarded-Uri", "/");
        try
        {
            using var response = await client.SendAsync(check, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized)
            {
                Challenge(context, canRedirect);
                return false;
            }

            if (response.StatusCode == HttpStatusCode.OK &&
                ReadHeader(response, "X-Authentik-Meta-App") == settings.Application &&
                ReadHeader(response, "X-Authentik-Uid") is { Length: > 0 } subject)
            {
                var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], Scheme);
                context.User = new ClaimsPrincipal(identity);
                // Keep API header credentials limited to the current request.
                if (!hasAuthorizationHeader)
                    await context.SignInAsync(Scheme, context.User, new AuthenticationProperties { IsPersistent = true });
                return true;
            }

            context.Response.StatusCode = response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Forbidden
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status503ServiceUnavailable;
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }

        return false;
    }

    private void Challenge(HttpContext context, bool canRedirect)
    {
        if (!canRedirect)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var returnUrl = settings.PublicOrigin.GetLeftPart(UriPartial.Authority) + context.Request.GetEncodedPathAndQuery();
        context.Response.Redirect("/outpost.goauthentik.io/start" + QueryString.Create("rd", returnUrl));
    }

    private static string? ReadHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        var headers = values.ToArray();
        return headers.Length == 1 ? headers[0] : null;
    }
}
