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

    [TestMethod]
    public async Task SeededFeedRotatesBetweenSessionsAndPagesWithoutDuplicates()
    {
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Albums.Add(new Album { AlbumId = "rotating-feed", Name = "Rotating Feed" });
            context.Photos.AddRange(Enumerable.Range(0, 80).Select(index => new Photo
            {
                FilePath = $"candidate-{index:D2}.jpg",
                AlbumId = "rotating-feed",
                EstimatedScore = 4.5 - index * 0.002
            }));
            context.Photos.Add(new Photo
            {
                FilePath = "rated-candidate.jpg",
                AlbumId = "rotating-feed",
                IndependentScore = 6,
                EstimatedScore = 6
            });
            await context.SaveChangesAsync();
        }

        const int seedA = 101;
        const int seedB = 202;
        var firstA = await _http.GetFromJsonAsync<List<Photo>>($"/api/photos/feed?size=10&seed={seedA}");
        var repeatedA = await _http.GetFromJsonAsync<List<Photo>>($"/api/photos/feed?size=10&seed={seedA}");
        var firstB = await _http.GetFromJsonAsync<List<Photo>>($"/api/photos/feed?size=10&seed={seedB}");

        Assert.IsNotNull(firstA);
        Assert.IsNotNull(repeatedA);
        Assert.IsNotNull(firstB);
        CollectionAssert.AreEqual(firstA.Select(p => p.Id).ToArray(), repeatedA.Select(p => p.Id).ToArray());
        CollectionAssert.AreNotEqual(firstA.Select(p => p.Id).ToArray(), firstB.Select(p => p.Id).ToArray());
        Assert.IsTrue(firstA.All(p => p.FeedRank.HasValue));
        Assert.IsFalse(firstA.Any(p => p.FilePath == "rated-candidate.jpg"));

        var cursor = firstA[^1];
        var secondA = await _http.GetFromJsonAsync<List<Photo>>(
            $"/api/photos/feed?size=10&seed={seedA}&beforeRank={cursor.FeedRank}&beforeId={cursor.Id}");
        Assert.IsNotNull(secondA);
        Assert.IsFalse(firstA.Select(p => p.Id).Intersect(secondA.Select(p => p.Id)).Any());
    }
}
