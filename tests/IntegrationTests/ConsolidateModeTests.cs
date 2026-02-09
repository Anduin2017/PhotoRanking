using System.Text.Json;
using Aiursoft.CSTools.Tools;
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
    public async Task TestConsolidateModePrioritizesHighKnownRateAlbums()
    {
        // 1. Seed data
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Album A: 90% known rate
            var albumA = new Album { AlbumId = "album-a", Name = "Album A", KnownRate = 0.9 };
            context.Albums.Add(albumA);
            
            // Unrated photo in Album A
            context.Photos.Add(new Photo 
            { 
                FilePath = "photo-a.jpg", 
                AlbumId = "album-a", 
                IndependentScore = null,
                OverallScore = 3.0
            });
            
            // Album B: 10% known rate
            var albumB = new Album { AlbumId = "album-b", Name = "Album B", KnownRate = 0.1 };
            context.Albums.Add(albumB);
            
            // Unrated photo in Album B
            context.Photos.Add(new Photo 
            { 
                FilePath = "photo-b.jpg", 
                AlbumId = "album-b", 
                IndependentScore = null,
                OverallScore = 3.0
            });
            
            await context.SaveChangesAsync();
        }

        // 2. Call API multiple times. Photo A should appear more frequently than Photo B.
        int countA = 0;
        int countB = 0;
        for (int i = 0; i < 50; i++)
        {
            var response = await _http.GetAsync("/api/photos/discover?mode=consolidate&pageSize=1");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var photos = JsonSerializer.Deserialize<List<Photo>>(content, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (photos!.Any(x => x.FilePath == "photo-a.jpg")) countA++;
            if (photos!.Any(x => x.FilePath == "photo-b.jpg")) countB++;
        }

        Console.WriteLine($"Photo A (90% album): {countA}, Photo B (10% album): {countB}");
        Assert.IsTrue(countA > countB, $"Photo from higher known rate album (A: {countA}) should appear more frequently than lower (B: {countB})");
    }
}
