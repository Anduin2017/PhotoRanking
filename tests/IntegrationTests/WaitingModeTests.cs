using System.Text.Json;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using static Aiursoft.WebTools.Extends;

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class WaitingModeTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public WaitingModeTests()
    {
        _port = Network.GetAvailablePort();
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
    public async Task TestWaitingModeIgnoresMinScore()
    {
        // 1. Seed data
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album { AlbumId = "test-album-w", Name = "Test Album W" };
            context.Albums.Add(album);
            
            // Photo with very low score
            context.Photos.Add(new Photo 
            { 
                FilePath = "photo-new.jpg", 
                AlbumId = "test-album-w", 
                OverallScore = 0, 
                Knownness = 0,
                RatingCount = 0 
            });
            
            await context.SaveChangesAsync();
        }

        // 2. Call API with high minScore but in waiting mode
        // Even if we pass minScore=4.0, it should be ignored in waiting mode
        var response = await _http.GetAsync("/api/photos/discover?mode=waiting&minScore=4.0&pageSize=10");
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var photos = JsonSerializer.Deserialize<List<Photo>>(content, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.IsTrue(photos!.Any(x => x.FilePath == "photo-new.jpg"), "Low score photo SHOULD be included in waiting mode regardless of minScore");
    }

    [TestMethod]
    public async Task TestWaitingModeRandomness()
    {
         // 1. Seed data (enough to have different sets)
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album { AlbumId = "test-album-r", Name = "Test Album R" };
            context.Albums.Add(album);
            
            for (int i = 0; i < 50; i++)
            {
                context.Photos.Add(new Photo 
                { 
                    FilePath = $"photo-{i}.jpg", 
                    AlbumId = "test-album-r", 
                    OverallScore = 0, 
                    Knownness = 0 
                });
            }
            
            await context.SaveChangesAsync();
        }

        // 2. Call API multiple times
        var response1 = await _http.GetAsync("/api/photos/discover?mode=waiting&pageSize=5");
        var content1 = await response1.Content.ReadAsStringAsync();
        var photos1 = JsonSerializer.Deserialize<List<Photo>>(content1, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var response2 = await _http.GetAsync("/api/photos/discover?mode=waiting&pageSize=5");
        var content2 = await response2.Content.ReadAsStringAsync();
        var photos2 = JsonSerializer.Deserialize<List<Photo>>(content2, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Extremely unlikely to pick exactly same 5 photos in same order out of 50 if random
        Assert.IsNotEmpty(photos1!);
        Assert.IsNotEmpty(photos2!);
    }

    [TestMethod]
    public async Task TestWaitingModeExcludesRatedPhotos()
    {
        // 1. Seed data
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var album = new Album { AlbumId = "test-album-exclude", Name = "Test Album Exclude" };
            context.Albums.Add(album);

            // Rated photo
            context.Photos.Add(new Photo
            {
                FilePath = "photo-rated.jpg",
                AlbumId = "test-album-exclude",
                IndependentScore = 3.0,
                OverallScore = 3.0,
                Knownness = 50,
                RatingCount = 1
            });

            // Unrated photo
            context.Photos.Add(new Photo
            {
                FilePath = "photo-unrated.jpg",
                AlbumId = "test-album-exclude",
                IndependentScore = null,
                OverallScore = 0,
                Knownness = 0,
                RatingCount = 0
            });

            await context.SaveChangesAsync();
        }

        // 2. Call API in waiting mode
        var response = await _http.GetAsync("/api/photos/discover?mode=waiting&pageSize=10");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var photos = JsonSerializer.Deserialize<List<Photo>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.IsNotNull(photos);
        Assert.IsFalse(photos.Any(x => x.FilePath == "photo-rated.jpg"), "Rated photo SHOULD NOT be included in waiting mode");
        Assert.IsTrue(photos.Any(x => x.FilePath == "photo-unrated.jpg"), "Unrated photo SHOULD be included in waiting mode");
    }
}
