using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using static Aiursoft.WebTools.Extends;
using Newtonsoft.Json;

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class RatingHistoryTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public RatingHistoryTests()
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
        
        var testDbFiles = Directory.GetFiles(".", "test-db-*.db*");
        foreach (var file in testDbFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    [TestMethod]
    public async Task TestRatingHistoryEndpoint()
    {
        // 1. Prepare data
        using (var scope = _server!.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album { AlbumId = "test-album", Name = "Test Album" };
            dbContext.Albums.Add(album);
            
            var photo1 = new Photo { FilePath = "photo1.jpg", AlbumId = "test-album", LastRatedAt = DateTime.UtcNow.AddMinutes(-10) };
            var photo2 = new Photo { FilePath = "photo2.jpg", AlbumId = "test-album", LastRatedAt = DateTime.UtcNow.AddMinutes(-5) };
            var photo3 = new Photo { FilePath = "photo3.jpg", AlbumId = "test-album", LastRatedAt = null }; // Not rated
            
            dbContext.Photos.AddRange(photo1, photo2, photo3);
            await dbContext.SaveChangesAsync();
        }

        // 2. Test rating-history endpoint
        var response = await _http.GetAsync("/api/photos/rating-history");
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var photos = JsonConvert.DeserializeObject<List<Photo>>(content);
        
        Assert.IsNotNull(photos);
        Assert.AreEqual(2, photos.Count, "Should only return rated photos");
        Assert.AreEqual("photo2.jpg", photos[0].FilePath, "Should be ordered by LastRatedAt DESC");
        Assert.AreEqual("photo1.jpg", photos[1].FilePath);

        // 3. Test stats/top endpoint
        var statsResponse = await _http.GetAsync("/api/photos/stats/top");
        statsResponse.EnsureSuccessStatusCode();
        
        var statsContent = await statsResponse.Content.ReadAsStringAsync();
        var stats = JsonConvert.DeserializeObject<dynamic>(statsContent);
        
        Assert.IsNotNull(stats);
        Assert.IsNotNull(stats!.ratingHistory);
        Assert.AreEqual(2, ((Newtonsoft.Json.Linq.JArray)stats.ratingHistory).Count);
    }
}
