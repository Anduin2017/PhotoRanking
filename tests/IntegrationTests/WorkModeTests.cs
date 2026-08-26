using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using static Aiursoft.WebTools.Extends;

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class WorkModeTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public WorkModeTests()
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
    public async Task WorkModePrioritizesVisualNoveltyAndAlbumDiversity()
    {
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Albums.AddRange(
                new Album { AlbumId = "work-a", Name = "A" },
                new Album { AlbumId = "work-b", Name = "B" },
                new Album { AlbumId = "work-c", Name = "C" });
            context.Photos.AddRange(
                new Photo { FilePath = "a-most-novel.jpg", AlbumId = "work-a", EstimatedScore = 4, PredictionUncertainty = 0.1, PredictionNovelty = 0.9 },
                new Photo { FilePath = "a-high-disagreement.jpg", AlbumId = "work-a", EstimatedScore = 4, PredictionUncertainty = 10, PredictionNovelty = 0.8 },
                new Photo { FilePath = "b-anchor.jpg", AlbumId = "work-b", EstimatedScore = 3, PredictionUncertainty = 0.7, PredictionNovelty = 0.7 },
                new Photo { FilePath = "c-anchor.jpg", AlbumId = "work-c", EstimatedScore = 3, PredictionUncertainty = 0.6, PredictionNovelty = 0.6 },
                new Photo { FilePath = "already-rated.jpg", AlbumId = "work-c", IndependentScore = 5, PredictionUncertainty = 10, PredictionNovelty = 10 });
            await context.SaveChangesAsync();
        }

        var photos = await _http.GetFromJsonAsync<List<Photo>>(
            "/api/photos/discover?mode=work&page=1&pageSize=3&shuffleSeed=42");

        Assert.IsNotNull(photos);
        Assert.HasCount(3, photos);
        Assert.AreEqual(3, photos.Select(p => p.AlbumId).Distinct().Count(),
            "The first active-learning pass should establish anchors across albums.");
        Assert.IsTrue(photos.Any(p => p.FilePath == "a-most-novel.jpg"));
        Assert.IsFalse(photos.Any(p => p.FilePath == "a-high-disagreement.jpg"));
        Assert.IsFalse(photos.Any(p => p.FilePath == "already-rated.jpg"));
    }
}
