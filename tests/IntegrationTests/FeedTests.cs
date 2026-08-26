using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using static Aiursoft.WebTools.Extends;

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class FeedTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public FeedTests()
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
    public async Task FeedIsStablePredictedScoreOrderAndPagesThroughNulls()
    {
        int tieFirstId;
        int firstNullId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Albums.Add(new Album { AlbumId = "feed", Name = "Feed" });
            var top = new Photo { FilePath = "top.jpg", AlbumId = "feed", EstimatedScore = 5 };
            var tieFirst = new Photo { FilePath = "tie-first.jpg", AlbumId = "feed", EstimatedScore = 4 };
            var tieSecond = new Photo { FilePath = "tie-second.jpg", AlbumId = "feed", EstimatedScore = 4 };
            var firstNull = new Photo { FilePath = "null-first.jpg", AlbumId = "feed", EstimatedScore = null };
            var secondNull = new Photo { FilePath = "null-second.jpg", AlbumId = "feed", EstimatedScore = null };
            var rated = new Photo { FilePath = "rated.jpg", AlbumId = "feed", IndependentScore = 6, EstimatedScore = 6 };
            context.Photos.AddRange(top, tieFirst, tieSecond, firstNull, secondNull, rated);
            await context.SaveChangesAsync();
            tieFirstId = tieFirst.Id;
            firstNullId = firstNull.Id;
        }

        var firstPage = await _http.GetFromJsonAsync<List<Photo>>("/api/photos/feed?size=2");
        Assert.IsNotNull(firstPage);
        CollectionAssert.AreEqual(new[] { "top.jpg", "tie-first.jpg" }, firstPage.Select(p => p.FilePath).ToArray());

        var secondPage = await _http.GetFromJsonAsync<List<Photo>>($"/api/photos/feed?size=2&beforeScore=4&beforeId={tieFirstId}");
        Assert.IsNotNull(secondPage);
        CollectionAssert.AreEqual(new[] { "tie-second.jpg", "null-first.jpg" }, secondPage.Select(p => p.FilePath).ToArray());

        var thirdPage = await _http.GetFromJsonAsync<List<Photo>>($"/api/photos/feed?size=2&beforeId={firstNullId}");
        Assert.IsNotNull(thirdPage);
        CollectionAssert.AreEqual(new[] { "null-second.jpg" }, thirdPage.Select(p => p.FilePath).ToArray());
        Assert.IsFalse(firstPage.Concat(secondPage).Concat(thirdPage).Any(p => p.FilePath == "rated.jpg"));
    }
}
