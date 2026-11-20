using System.Net;
using System.Security.Claims;
using Anduin.PhotoRanking.Services;
using Microsoft.AspNetCore.Authentication;
using Moq;

namespace Anduin.PhotoRanking.Tests.Services;

[TestClass]
public class RankingSessionTests
{
    [TestMethod]
    public async Task ValidLocalTicketDoesNotContactAuthentik()
    {
        var (context, authentication) = CreateContext();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "ranking-user")], RankingSession.Scheme));
        authentication.Setup(service => service.AuthenticateAsync(context, RankingSession.Scheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, RankingSession.Scheme)));
        using var client = new HttpClient(new OutpostStub(_ => throw new InvalidOperationException("Unexpected SSO request.")));

        Assert.IsTrue(await CreateSession(client).AuthenticateAsync(context));
        Assert.AreSame(principal, context.User);
    }

    [TestMethod]
    public async Task VerifiedProxySessionIssuesLocalCookieUsingOnlyOutpostIdentity()
    {
        var (context, authentication) = CreateContext();
        context.Request.Headers["X-Authentik-Uid"] = "untrusted-header";
        context.Request.Headers.Cookie = "__Host-PhotoRanking=invalid; authentik_proxy_test=signed%3Did";
        using var client = new HttpClient(new OutpostStub(request =>
        {
            Assert.AreEqual("/outpost.goauthentik.io/auth/caddy", request.RequestUri?.AbsolutePath);
            Assert.AreEqual("authentik_proxy_test=signed%3Did", request.Headers.GetValues("Cookie").Single());
            Assert.IsNull(request.Headers.Authorization);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("X-Authentik-Meta-App", "ranking");
            response.Headers.Add("X-Authentik-Uid", "verified-user");
            return response;
        }));
        authentication.Setup(service => service.SignInAsync(context, RankingSession.Scheme,
                It.Is<ClaimsPrincipal>(principal => principal.FindFirstValue(ClaimTypes.NameIdentifier) == "verified-user"),
                It.Is<AuthenticationProperties>(properties => properties.IsPersistent)))
            .Returns(Task.CompletedTask).Verifiable();

        Assert.IsTrue(await CreateSession(client).AuthenticateAsync(context));
        authentication.Verify();
    }

    [TestMethod]
    public async Task HeaderCredentialsCanAuthorizeAnApiRequestButDoNotIssueLocalCookie()
    {
        var (context, _) = CreateContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers.Authorization = "Bearer temporary";
        using var client = new HttpClient(new OutpostStub(request =>
        {
            Assert.AreEqual("Bearer temporary", request.Headers.Authorization?.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("X-Authentik-Meta-App", "ranking");
            response.Headers.Add("X-Authentik-Uid", "verified-user");
            return response;
        }));

        Assert.IsTrue(await CreateSession(client).AuthenticateAsync(context));
        Assert.AreEqual("verified-user", context.User.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [TestMethod]
    public async Task SuccessfulOutpostStatusWithoutRankingIdentityDoesNotAuthenticate()
    {
        var (context, _) = CreateContext();
        context.Request.Headers.Cookie = "authentik_proxy_test=unverified";
        using var client = new HttpClient(new OutpostStub(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        Assert.IsFalse(await CreateSession(client).AuthenticateAsync(context));
        Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task AnonymousLoginReturnsToTheConfiguredOrigin()
    {
        var (context, _) = CreateContext();
        context.Request.Host = new HostString("untrusted.example");
        context.Request.Path = "/feed";
        context.Request.QueryString = new QueryString("?view=grid");
        using var client = new HttpClient(new OutpostStub(_ => throw new InvalidOperationException("Unexpected SSO request.")));

        Assert.IsFalse(await CreateSession(client).AuthenticateAsync(context));
        Assert.AreEqual(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.AreEqual("/outpost.goauthentik.io/start?rd=https%3A%2F%2Franking.example%2Ffeed%3Fview%3Dgrid",
            context.Response.Headers.Location.ToString());
    }

    private static RankingSession CreateSession(HttpClient client) =>
        new(client, new RankingSession.Settings(new Uri("http://outpost"), new Uri("https://ranking.example"), "ranking"));

    private static (DefaultHttpContext Context, Mock<IAuthenticationService> Authentication) CreateContext()
    {
        var authentication = new Mock<IAuthenticationService>(MockBehavior.Strict);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.RequestServices = Mock.Of<IServiceProvider>(provider =>
            provider.GetService(typeof(IAuthenticationService)) == authentication.Object);
        authentication.Setup(service => service.AuthenticateAsync(context, RankingSession.Scheme))
            .ReturnsAsync(AuthenticateResult.NoResult());
        return (context, authentication);
    }

    private sealed class OutpostStub(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
