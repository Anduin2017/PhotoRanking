using System.Net;
using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Microsoft.EntityFrameworkCore;
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
    public async Task SixIsAlwaysAValidFirstRatingAndCapturesBlindPrediction()
    {
        var photoId = await SeedPhotoAsync("first-rating", null, 4.25, "test-model-v1", 0, false);

        var response = await _http.PostAsJsonAsync($"/api/photos/{photoId}/rate", new { Score = 6 });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var scope = _server!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var photo = await context.Photos.SingleAsync(p => p.Id == photoId);
        var log = await context.RatingLogs.SingleAsync(l => l.PhotoId == photoId);

        Assert.AreEqual(6.0, photo.IndependentScore);
        Assert.AreEqual(6.0, photo.OverallScore);
        Assert.AreEqual(0, photo.RatingCount, "Legacy rating count must no longer be maintained.");
        Assert.IsFalse(log.IsCorrection);
        Assert.IsNull(log.PreviousScore);
        Assert.AreEqual(4.25, log.PredictionAtRating);
        Assert.AreEqual("test-model-v1", log.PredictionModelVersion);
    }

    [TestMethod]
    public async Task ReRatingOverwritesTheFinalScoreWithoutAveragingOrLocking()
    {
        var photoId = await SeedPhotoAsync("correction", 2, 4.8, "old-model", 999, true);

        (await _http.PostAsJsonAsync($"/api/photos/{photoId}/rate", new { Score = 5 })).EnsureSuccessStatusCode();
        (await _http.PostAsJsonAsync($"/api/photos/{photoId}/rate", new { Score = 3 })).EnsureSuccessStatusCode();

        using var scope = _server!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var photo = await context.Photos.SingleAsync(p => p.Id == photoId);
        var logs = await context.RatingLogs.Where(l => l.PhotoId == photoId).OrderBy(l => l.Id).ToListAsync();

        Assert.AreEqual(3.0, photo.IndependentScore, "The latest correction is the only source of truth.");
        Assert.AreEqual(999, photo.RatingCount, "Legacy count must not influence or track corrections.");
        Assert.IsFalse(photo.IsFixed);
        Assert.HasCount(2, logs);
        Assert.IsTrue(logs.All(l => l.IsCorrection));
        Assert.AreEqual(2.0, logs[0].PreviousScore);
        Assert.AreEqual(5.0, logs[1].PreviousScore);
        Assert.IsTrue(logs.All(l => l.PredictionAtRating == null), "Corrections are not prediction evaluation samples.");
    }

    [TestMethod]
    public async Task RatingOnePhotoNeverChangesAnotherPhotosFinalScore()
    {
        int targetId;
        int neighborId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Albums.Add(new Album { AlbumId = "album-isolation", Name = "Album Isolation", AlbumScore = 1 });
            var target = new Photo { FilePath = "target.jpg", AlbumId = "album-isolation", IndependentScore = 2, OverallScore = 2 };
            var neighbor = new Photo { FilePath = "neighbor.jpg", AlbumId = "album-isolation", IndependentScore = 5, OverallScore = 5 };
            context.Photos.AddRange(target, neighbor);
            await context.SaveChangesAsync();
            targetId = target.Id;
            neighborId = neighbor.Id;
        }

        (await _http.PostAsJsonAsync($"/api/photos/{targetId}/rate", new { Score = 6 })).EnsureSuccessStatusCode();

        using var verificationScope = _server!.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var neighborAfter = await verificationContext.Photos.SingleAsync(p => p.Id == neighborId);
        Assert.AreEqual(5.0, neighborAfter.IndependentScore);
        Assert.AreEqual(5.0, neighborAfter.OverallScore);
    }

    [TestMethod]
    public async Task ScoresOutsideTheCompatibleZeroToSixRangeAreRejected()
    {
        var photoId = await SeedPhotoAsync("invalid", null, null, null, 0, false);
        var response = await _http.PostAsJsonAsync($"/api/photos/{photoId}/rate", new { Score = 7 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<int> SeedPhotoAsync(
        string albumId,
        double? manualScore,
        double? predictedScore,
        string? predictionVersion,
        int legacyRatingCount,
        bool legacyFixed)
    {
        using var scope = _server!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Albums.Add(new Album { AlbumId = albumId, Name = albumId, AlbumScore = 0.5 });
        var photo = new Photo
        {
            FilePath = $"{albumId}.jpg",
            AlbumId = albumId,
            IndependentScore = manualScore,
            OverallScore = manualScore ?? predictedScore ?? 0,
            EstimatedScore = predictedScore,
            EstimatedScoreModelVersion = predictionVersion,
            RatingCount = legacyRatingCount,
            IsFixed = legacyFixed
        };
        context.Photos.Add(photo);
        await context.SaveChangesAsync();
        return photo.Id;
    }
}
