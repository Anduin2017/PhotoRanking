using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Anduin.PhotoRanking.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anduin.PhotoRanking.Tests.Services;

[TestClass]
public class PersonalizedPredictionServiceTests
{
    private AppDbContext _context = null!;
    private IMemoryCache _cache = null!;
    private PersonalizedPredictionService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new AppDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _service = new PersonalizedPredictionService(
            _context,
            _cache,
            NullLogger<PersonalizedPredictionService>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        _cache.Dispose();
    }

    [TestMethod]
    public async Task ModelLearnsFromOneCurrentFinalScorePerPhotoAndPersistsPredictions()
    {
        _context.Albums.Add(new Album { AlbumId = "training", Name = "Training" });
        for (var i = 0; i < 40; i++)
        {
            var high = i % 2 == 0;
            var photo = new Photo
            {
                FilePath = $"train-{i}.jpg",
                AlbumId = "training",
                IndependentScore = high ? 5 : 2,
                FeatureVector = Vector(high ? 1f : -1f, (i % 5) * 0.01f),
                LastRatedAt = DateTime.UtcNow.AddMinutes(-40 + i)
            };
            _context.Photos.Add(photo);
        }

        // Arbitrarily many old events must not create extra training samples.
        await _context.SaveChangesAsync();
        var firstPhotoId = await _context.Photos.Select(p => p.Id).FirstAsync();
        for (var i = 0; i < 10; i++)
        {
            _context.RatingLogs.Add(new RatingLog { PhotoId = firstPhotoId, Score = i % 7, IsCorrection = true });
        }
        await _context.SaveChangesAsync();

        var model = await _service.TrainAndActivateAsync();

        Assert.IsNotNull(model);
        Assert.AreEqual(40, model.TrainingPhotoCount);
        Assert.AreEqual(40, model.TrainingCandidatePhotoCount);
        Assert.AreEqual(40, model.CoverageTrainingPhotoCount);
        Assert.AreEqual(5, model.EnsembleSize);
        Assert.AreEqual(4, model.CoverageCentroidCount);
        Assert.IsNotEmpty(model.ModelData);

        // Force a database round-trip to verify the multi-model bundle is restart-safe.
        _cache.Remove("personal-score-model:" + model.Version);

        var highPrediction = await _service.PredictAsync(Vector(1f, 0));
        var lowPrediction = await _service.PredictAsync(Vector(-1f, 0));
        var uncoveredPrediction = await _service.PredictAsync(Vector(0, 0, 1f));
        Assert.IsNotNull(highPrediction);
        Assert.IsNotNull(lowPrediction);
        Assert.IsNotNull(uncoveredPrediction);
        Assert.IsTrue(highPrediction.Score > lowPrediction.Score);
        Assert.IsTrue(highPrediction.Score is >= 0 and <= 6);
        Assert.IsTrue(lowPrediction.Score is >= 0 and <= 6);
        Assert.IsNotNull(highPrediction.Uncertainty);
        Assert.IsNotNull(lowPrediction.Uncertainty);
        Assert.IsNotNull(highPrediction.Novelty);
        Assert.IsNotNull(lowPrediction.Novelty);
        Assert.IsGreaterThan(highPrediction.Novelty.Value, uncoveredPrediction.Novelty!.Value,
            "A vector outside the rated visual coverage should be selected earlier for anchor work.");

        var unratedHigh = new Photo { FilePath = "unrated-high.jpg", AlbumId = "training", FeatureVector = Vector(1f, 0) };
        var unratedLow = new Photo { FilePath = "unrated-low.jpg", AlbumId = "training", FeatureVector = Vector(-1f, 0) };
        var rated = new Photo
        {
            FilePath = "already-rated.jpg",
            AlbumId = "training",
            IndependentScore = 4,
            EstimatedScore = 1.23,
            FeatureVector = Vector(1f, 0)
        };
        _context.Photos.AddRange(unratedHigh, unratedLow, rated);
        await _context.SaveChangesAsync();

        Assert.IsTrue(await _service.PredictAndPersistBatchAsync([unratedHigh, unratedLow, rated]));
        Assert.IsTrue(unratedHigh.EstimatedScore!.Value > unratedLow.EstimatedScore!.Value);
        Assert.IsNotNull(unratedHigh.PredictionUncertainty);
        Assert.IsNotNull(unratedLow.PredictionUncertainty);
        Assert.IsNotNull(unratedHigh.PredictionNovelty);
        Assert.IsNotNull(unratedLow.PredictionNovelty);
        Assert.AreEqual(model.Version, unratedHigh.EstimatedScoreModelVersion);
        Assert.AreEqual(1.23, rated.EstimatedScore, "A rated photo's pre-rating prediction must remain frozen.");
    }

    private static byte[] Vector(float first, float second, float third = 0)
    {
        var values = new float[512];
        values[0] = first;
        values[1] = second;
        values[2] = third;
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
