using System.Text.Json;
using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using static Aiursoft.WebTools.Extends;

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class FeaturedModeTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public FeaturedModeTests()
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
    public async Task TestFeaturedModeFiltersByIndependentScore()
    {
        // 1. Seed data
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album { AlbumId = "test-album", Name = "Test Album" };
            context.Albums.Add(album);
            
            context.Photos.AddRange(
                new Photo { FilePath = "photo1.jpg", AlbumId = "test-album", IndependentScore = 5.0, OverallScore = 5.0 },
                new Photo { FilePath = "photo2.jpg", AlbumId = "test-album", IndependentScore = 4.0, OverallScore = 4.0 },
                new Photo { FilePath = "photo3.jpg", AlbumId = "test-album", IndependentScore = 5.0, OverallScore = 5.0 },
                new Photo { FilePath = "photo4.jpg", AlbumId = "test-album", IndependentScore = null, OverallScore = 0.0 }
            );
            
            await context.SaveChangesAsync();
        }

        // 2. Test filtering by score 5
        var response = await _http.GetAsync("/api/photos/discover?mode=featured&minScore=5.0&pageSize=10");
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var photos = JsonSerializer.Deserialize<List<Photo>>(content, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.IsNotNull(photos);
        Assert.HasCount(2, photos);
        Assert.IsTrue(photos.All(x => Math.Abs(x.IndependentScore!.Value - 5.0) < 0.0001));
        Assert.IsTrue(photos.Any(x => x.FilePath == "photo1.jpg"));
        Assert.IsTrue(photos.Any(x => x.FilePath == "photo3.jpg"));

        // 3. Test filtering by score 4
        response = await _http.GetAsync("/api/photos/discover?mode=featured&minScore=4.0&pageSize=10");
        response.EnsureSuccessStatusCode();
        
        content = await response.Content.ReadAsStringAsync();
        photos = JsonSerializer.Deserialize<List<Photo>>(content, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.IsNotNull(photos);
        Assert.HasCount(1, photos);
        Assert.AreEqual(4.0, photos[0].IndependentScore ?? 0, 0.0001);
        Assert.AreEqual("photo2.jpg", photos[0].FilePath);
    }

    [TestMethod]
    public async Task TestFeaturedModeReturnsEmptyIfNoMatch()
    {
        // 1. Seed data
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album { AlbumId = "test-album-empty", Name = "Test Album Empty" };
            context.Albums.Add(album);
            context.Photos.Add(new Photo { FilePath = "photo-only.jpg", AlbumId = "test-album-empty", IndependentScore = 5.0, OverallScore = 5.0 });
            await context.SaveChangesAsync();
        }

        var response = await _http.GetAsync("/api/photos/discover?mode=featured&minScore=1.0&pageSize=10");
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var photos = JsonSerializer.Deserialize<List<Photo>>(content, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        Assert.IsNotNull(photos);
        Assert.IsEmpty(photos);
    }
}
