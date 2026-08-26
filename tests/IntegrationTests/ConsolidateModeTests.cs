using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using static Aiursoft.WebTools.Extends;

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class ConsolidateModeTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public ConsolidateModeTests()
    {
        _port = TestPortAllocator.GetAvailablePort();
        _http = new HttpClient { BaseAddress = new Uri($"http://localhost:{_port}") };
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        _server = await AppAsync<TestStartup>([], port: _port);
        await _server.UpdateDbAsync<AppDbContext>();
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
    }

    [TestMethod]
    public async Task LegacyConsolidateAliasHasNoCoverageOrRatingHistorySemantics()
    {
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Albums.AddRange(
                new Album { AlbumId = "covered", Name = "Covered", KnownRate = 1 },
                new Album { AlbumId = "uncovered", Name = "Uncovered", KnownRate = 0 });
            context.Photos.AddRange(
                new Photo { FilePath = "covered-unrated.jpg", AlbumId = "covered", IndependentScore = null },
                new Photo { FilePath = "uncovered-unrated.jpg", AlbumId = "uncovered", IndependentScore = null },
                new Photo { FilePath = "rated.jpg", AlbumId = "covered", IndependentScore = 4 });
            await context.SaveChangesAsync();
        }

        var response = await _http.GetAsync("/api/photos/discover?mode=consolidate&pageSize=10");
        response.EnsureSuccessStatusCode();
        var photos = await response.Content.ReadFromJsonAsync<List<Photo>>();

        Assert.IsNotNull(photos);
        Assert.HasCount(2, photos);
        Assert.IsTrue(photos.Any(p => p.FilePath == "covered-unrated.jpg"));
        Assert.IsTrue(photos.Any(p => p.FilePath == "uncovered-unrated.jpg"));
        Assert.IsFalse(photos.Any(p => p.FilePath == "rated.jpg"));
    }
}
