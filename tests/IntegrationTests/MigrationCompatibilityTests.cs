using Anduin.PhotoRanking.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class MigrationCompatibilityTests
{
    [TestMethod]
    public async Task ExistingPhotosScoresAndRatingLogsSurvivePersonalScoringUpgrade()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AppDbContext(options);
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260402112741_AddEstimatedScorePersistence");

        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO Albums
                (AlbumId, Name, AlbumScore, KnownRate, StandardDeviation, HighestScore,
                 LowestScore, PhotoCount, CreatedAt, UpdatedAt)
            VALUES
                ('existing-album', 'Existing Album', 4.2, 0.5, 0.3, 5, 2, 2,
                 '2026-01-01T00:00:00Z', '2026-01-02T00:00:00Z');

            INSERT INTO Photos
                (Id, FilePath, IndependentScore, OverallScore, Knownness, RatingCount,
                 IsFixed, ViewCount, LastRatedAt, CreatedAt, AlbumId, FileSize,
                 LastModified, FeatureVector, EstimatedScore, EstimatedScoreUpdatedAt)
            VALUES
                (42, 'existing/photo.jpg', 5, 4.7, 88, 12, 1, 9,
                 '2026-01-02T00:00:00Z', '2026-01-01T00:00:00Z', 'existing-album',
                 123456, '2026-01-01T00:00:00Z', NULL, 4.4, '2026-01-01T12:00:00Z');

            INSERT INTO RatingLogs (Id, PhotoId, Score, RatedAt)
            VALUES (7, 42, 5, '2026-01-02T00:00:00Z');
            """);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        var photo = await context.Photos.SingleAsync(p => p.Id == 42);
        var log = await context.RatingLogs.SingleAsync(l => l.Id == 7);
        var album = await context.Albums.SingleAsync(a => a.AlbumId == "existing-album");

        Assert.AreEqual("existing/photo.jpg", photo.FilePath);
        Assert.AreEqual(5.0, photo.IndependentScore);
        Assert.AreEqual(4.7, photo.OverallScore);
        Assert.AreEqual(4.4, photo.EstimatedScore);
        Assert.AreEqual(12, photo.RatingCount);
        Assert.IsTrue(photo.IsFixed);
        Assert.IsNull(photo.EstimatedScoreModelVersion);
        Assert.IsNull(photo.PredictionUncertainty);
        Assert.IsNull(photo.PredictionNovelty);
        Assert.AreEqual(5, log.Score);
        Assert.IsFalse(log.IsCorrection);
        Assert.IsNull(log.PreviousScore);
        Assert.IsNull(log.PredictionAtRating);
        Assert.AreEqual(0, album.RatedPhotoCount);
        Assert.IsNull(album.AverageManualScore);
        var predictionModelColumns = new List<string>();
        await using (var modelColumnCommand = connection.CreateCommand())
        {
            modelColumnCommand.CommandText = "PRAGMA table_info('PredictionModels')";
            await using var modelColumnReader = await modelColumnCommand.ExecuteReaderAsync();
            while (await modelColumnReader.ReadAsync()) predictionModelColumns.Add(modelColumnReader.GetString(1));
        }
        CollectionAssert.Contains(predictionModelColumns, "CoverageTrainingPhotoCount");

        var indexNames = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Photos'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) indexNames.Add(reader.GetString(0));

        CollectionAssert.Contains(indexNames, "IX_Photos_EstimatedScore");
        CollectionAssert.Contains(indexNames, "IX_Photos_IndependentScore");
        CollectionAssert.Contains(indexNames, "IX_Photos_PredictionUncertainty");
        CollectionAssert.Contains(indexNames, "IX_Photos_PredictionNovelty");
    }
}
