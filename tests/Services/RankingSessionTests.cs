using Anduin.PhotoRanking.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Anduin.PhotoRanking.Tests.Services;

[TestClass]
public class RankingSessionTests
{
    [TestMethod]
    public void UsesCodeAndPkceAndKeepsAFixedIndependentCookie()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RankingOidc:Authority"] = "https://auth.example/application/o/photoranking/",
            ["RankingOidc:PublicOrigin"] = "https://ranking.example",
            ["RankingOidc:ClientId"] = "photoranking",
            ["RankingOidc:ClientSecret"] = "test-secret"
        });
        RankingSession.Add(builder.Services, builder.Configuration);
        using var services = builder.Services.BuildServiceProvider();
        var oidc = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var cookie = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(RankingSession.Scheme);

        Assert.AreEqual("code", oidc.ResponseType);
        Assert.IsTrue(oidc.UsePkce);
        Assert.IsTrue(oidc.RequireHttpsMetadata);
        Assert.IsFalse(oidc.UseTokenLifetime);
        Assert.IsFalse(oidc.SaveTokens);
        Assert.AreEqual(new PathString("/signin-oidc"), oidc.CallbackPath);
        Assert.AreEqual(RankingSession.Scheme, oidc.SignInScheme);
        Assert.AreEqual(TimeSpan.FromDays(180), cookie.ExpireTimeSpan);
        Assert.AreEqual(TimeSpan.FromDays(180), cookie.Cookie.MaxAge);
        Assert.IsFalse(cookie.SlidingExpiration);
        Assert.IsTrue(cookie.Cookie.HttpOnly);
        Assert.AreEqual(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.IsNull(cookie.Cookie.Domain);
    }

    [TestMethod]
    public void PartialSettingsFailClosedAtStartup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RankingOidc:Authority"] = "https://auth.example/application/o/photoranking/",
                ["RankingOidc:PublicOrigin"] = "https://ranking.example",
                ["RankingOidc:ClientId"] = "photoranking"
            })
            .Build();

        Assert.ThrowsExactly<InvalidOperationException>(() => RankingSession.Add(new ServiceCollection(), config));
    }

    [TestMethod]
    public void OmittingSettingsLeavesTheAuthenticationSchemesUnregistered()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        RankingSession.Add(services, config);

        Assert.IsFalse(services.Any(service => service.ServiceType == typeof(IAuthenticationSchemeProvider)));
    }
}
