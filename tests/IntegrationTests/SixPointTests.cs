using System.Net;
using System.Net.Http.Json;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Aiursoft.WebTools.Extends;

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class SixPointTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public SixPointTests()
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
    public async Task TestSixPointUnlockLogic()
    {
        int photoId;
        // 1. Seed data: Photo with 9 ratings, score 5, album score 4.5
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album 
            { 
                AlbumId = "high-score-album", 
                Name = "High Score Album",
                AlbumScore = 4.5 
            };
            context.Albums.Add(album);
            
            var photo = new Photo 
            { 
                FilePath = "awesome.jpg", 
                AlbumId = "high-score-album", 
                IndependentScore = 5.0, 
                OverallScore = 5.0,
                RatingCount = 9
            };
            context.Photos.Add(photo);
            await context.SaveChangesAsync();
            photoId = photo.Id;
        }

        // 2. Try to rate 6
        var response = await _http.PostAsJsonAsync($"/api/photos/{photoId}/rate", new { Score = 6 });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        
        var updatedPhoto = await response.Content.ReadFromJsonAsync<Photo>();
        Assert.IsNotNull(updatedPhoto);
        Assert.AreEqual(6.0, updatedPhoto.IndependentScore ?? 0, 0.0001);
    }

    [TestMethod]
    public async Task TestSixPointLockLogic_LowRatingCount()
    {
        int photoId;
        // 1. Seed data: Photo with 5 ratings, score 5, album score 4.5
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album 
            { 
                AlbumId = "album1", 
                Name = "Album 1",
                AlbumScore = 4.5 
            };
            context.Albums.Add(album);
            
            var photo = new Photo 
            { 
                FilePath = "photo1.jpg", 
                AlbumId = "album1", 
                IndependentScore = 5.0, 
                OverallScore = 5.0,
                RatingCount = 5
            };
            context.Photos.Add(photo);
            await context.SaveChangesAsync();
            photoId = photo.Id;
        }

        // 2. Try to rate 6
        var response = await _http.PostAsJsonAsync($"/api/photos/{photoId}/rate", new { Score = 6 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task TestSixPointLockLogic_LowAlbumScore()
    {
        int photoId;
        // 1. Seed data: Photo with 10 ratings, score 5, album score 3.5
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var album = new Album 
            { 
                AlbumId = "album2", 
                Name = "Album 2",
                AlbumScore = 3.5 
            };
            context.Albums.Add(album);
            
            var photo = new Photo 
            { 
                FilePath = "photo2.jpg", 
                AlbumId = "album2", 
                IndependentScore = 5.0, 
                OverallScore = 5.0,
                RatingCount = 10
            };
            context.Photos.Add(photo);
            await context.SaveChangesAsync();
            photoId = photo.Id;
        }

        // 2. Try to rate 6
        var response = await _http.PostAsJsonAsync($"/api/photos/{photoId}/rate", new { Score = 6 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
