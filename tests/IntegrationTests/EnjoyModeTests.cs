using System.Text.Json;
using Aiursoft.CSTools.Tools;
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
    public async Task TestEnjoyModeIncludesAllPhotos()
    {
        // 1. Seed data
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album { AlbumId = "test-album", Name = "Test Album" };
            context.Albums.Add(album);
            
            // Photo with high score and rated
            context.Photos.Add(new Photo 
            { 
                FilePath = "photo1.jpg", 
                AlbumId = "test-album", 
                OverallScore = 5, 
                RatingCount = 1 
            });
            
            // Photo with low score and NOT rated (was excluded before)
            context.Photos.Add(new Photo 
            { 
                FilePath = "photo2.jpg", 
                AlbumId = "test-album", 
                OverallScore = 1, 
                RatingCount = 0 
            });
            
            await context.SaveChangesAsync();
        }

        // 2. Call API multiple times to verify unrated photo can be selected
        bool gotPhoto2 = false;
        for (int i = 0; i < 50; i++)
        {
            var response = await _http.GetAsync("/api/photos/discover?mode=enjoy&pageSize=10");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var photos = JsonSerializer.Deserialize<List<Photo>>(content, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (photos!.Any(x => x.FilePath == "photo2.jpg"))
            {
                gotPhoto2 = true;
                break;
            }
        }
        
        Assert.IsTrue(gotPhoto2, "Unrated photo should be included in enjoy mode");
    }
}
