using System.Text.Json;
using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using static Aiursoft.WebTools.Extends;

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class EnjoyModeTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public EnjoyModeTests()
    {
        _port = TestPortAllocator.GetAvailablePort();
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };
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
    public async Task TestEnjoyModeFiltersLowScorePhotos()
    {
        // 1. Seed data
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album { AlbumId = "test-album", Name = "Test Album" };
            context.Albums.Add(album);
            
            // Photo with high score (>= 3.0)
            context.Photos.Add(new Photo 
            { 
                FilePath = "photo1.jpg", 
                AlbumId = "test-album", 
                IndependentScore = 3.5,
                OverallScore = 3.5, 
                RatingCount = 1 
            });
            
            // Photo with low score (< 3.0)
            context.Photos.Add(new Photo 
            { 
                FilePath = "photo2.jpg", 
                AlbumId = "test-album", 
                IndependentScore = 1.5,
                OverallScore = 1.5, 
                RatingCount = 1 
            });
            
            await context.SaveChangesAsync();
        }

        // 2. Call API multiple times to verify only high score photos are selected
        for (int i = 0; i < 20; i++)
        {
            var response = await _http.GetAsync("/api/photos/discover?mode=enjoy&pageSize=10");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var photos = JsonSerializer.Deserialize<List<Photo>>(content, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            Assert.IsFalse(photos!.Any(x => x.FilePath == "photo2.jpg"), "Low score photo should NOT be included in enjoy mode");
            Assert.IsTrue(photos!.All(x => x.IndependentScore >= 3.0), "All photos in enjoy mode should have final manual score >= 3.0");
        }
    }

    [TestMethod]
    public async Task TestEnjoyModeWithCustomMinScore()
    {
        // 1. Seed data
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var album = new Album { AlbumId = "test-album-2", Name = "Test Album 2" };
            context.Albums.Add(album);

            // Photo with score 4.5
            context.Photos.Add(new Photo
            {
                FilePath = "photo-high.jpg",
                AlbumId = "test-album-2",
                IndependentScore = 4.5,
                OverallScore = 4.5,
                RatingCount = 1
            });

            // Photo with score 3.5
            context.Photos.Add(new Photo
            {
                FilePath = "photo-mid.jpg",
                AlbumId = "test-album-2",
                IndependentScore = 3.5,
                OverallScore = 3.5,
                RatingCount = 1
            });

            // Photo with score 2.5
            context.Photos.Add(new Photo
            {
                FilePath = "photo-low.jpg",
                AlbumId = "test-album-2",
                IndependentScore = 2.5,
                OverallScore = 2.5,
                RatingCount = 1
            });

            await context.SaveChangesAsync();
        }

        // 2. Call API with minScore = 4.0
        var response = await _http.GetAsync("/api/photos/discover?mode=enjoy&minScore=4.0&pageSize=10");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var photos = JsonSerializer.Deserialize<List<Photo>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.IsTrue(photos!.All(x => x.IndependentScore >= 4.0), "All photos should have final manual score >= 4.0");
        Assert.IsTrue(photos!.Any(x => x.FilePath == "photo-high.jpg"));
        Assert.IsFalse(photos!.Any(x => x.FilePath == "photo-mid.jpg"));
        Assert.IsFalse(photos!.Any(x => x.FilePath == "photo-low.jpg"));
    }

    [TestMethod]
    public async Task EnjoyModeUsesAStableRandomOrderInsideTheRequestedRange()
    {
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Albums.Add(new Album { AlbumId = "enjoy-range", Name = "Enjoy Range" });
            context.Photos.AddRange(
                new Photo { FilePath = "below.jpg", AlbumId = "enjoy-range", IndependentScore = 3.9 },
                new Photo { FilePath = "inside-a.jpg", AlbumId = "enjoy-range", IndependentScore = 4.0 },
                new Photo { FilePath = "inside-b.jpg", AlbumId = "enjoy-range", IndependentScore = 4.5 },
                new Photo { FilePath = "above.jpg", AlbumId = "enjoy-range", IndependentScore = 5.0 });
            await context.SaveChangesAsync();
        }

        const string query = "/api/photos/discover?mode=enjoy&minScore=4&maxScore=4.5&pageSize=1&shuffleSeed=123";
        var firstPage = await _http.GetFromJsonAsync<List<Photo>>(query + "&page=1");
        var firstPageAgain = await _http.GetFromJsonAsync<List<Photo>>(query + "&page=1");
        var secondPage = await _http.GetFromJsonAsync<List<Photo>>(query + "&page=2");

        Assert.IsNotNull(firstPage);
        Assert.IsNotNull(firstPageAgain);
        Assert.IsNotNull(secondPage);
        Assert.HasCount(1, firstPage);
        Assert.HasCount(1, secondPage);
        Assert.AreEqual(firstPage[0].Id, firstPageAgain[0].Id, "One slideshow session must have stable ordering.");
        Assert.AreNotEqual(firstPage[0].Id, secondPage[0].Id, "Adjacent pages must not repeat a photo.");
        Assert.IsTrue(firstPage.Concat(secondPage).All(p => p.IndependentScore is >= 4 and <= 4.5));
    }
}
