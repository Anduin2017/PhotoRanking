using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Anduin.PhotoRanking.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anduin.PhotoRanking.Tests.Services;

[TestClass]
public class UserPreferenceServiceTests
{
    private AppDbContext _context = null!;
    private UserPreferenceService _service = null!;
    private IMemoryCache _cache = null!;

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
        _service = new UserPreferenceService(_context, _cache, new NullLogger<UserPreferenceService>());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        _cache.Dispose();
    }

    private byte[] CreateVector(float[] values)
    {
        var vector = new float[512]; // CLIP dimension
        Array.Copy(values, vector, Math.Min(values.Length, 512));
        
        var byteArray = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }

    private float[] BytesToFloats(byte[] bytes)
    {
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private double CosineSimilarity(float[] a, float[] b)
    {
        return ImageAnalysisService.CalculateCosineSimilarity(a, b);
    }

    [TestMethod]
    public async Task TestEmptyDatabase_ReturnsNull()
    {
        var result = await _service.GetUserPreferenceVectorAsync();
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task TestSmallDataset_ReturnsAverage()
    {
        // Insert a dummy album
        var album = new Album { AlbumId = "test-album", Name = "Test Album" };
        _context.Albums.Add(album);

        // Insert 5 photos (N < 10, trigger fallback average)
        // Even with percentile logic, if Total=5, Take=Ceiling(5*0.02)=1.
        // Wait, if Take=1, Average is just that vector.
        // To test "Average" logic effectively with the percentile constraint (approx 2%),
        // we need Total large enough so Take > 1, BUT Take < 10.
        // Target Take = 5.
        // Total * 0.02 = 5 => Total = 250.
        
        // Construct 250 photos.
        // Top 5 are [1,0...] and [0,1...] mixed.
        // Expect result to be average ~[0.7, 0.7...] (normalized)
        
        var photos = new List<Photo>();
        
        // 3 photos pointing X
        for (int i = 0; i < 3; i++)
        {
            photos.Add(new Photo 
            { 
                FilePath = $"x_{i}.jpg", 
                AlbumId = "test-album",
                OverallScore = 5.0, // High score
                FeatureVector = CreateVector([10f, 0f]) // Magnitude doesn't matter for direction
            });
        }
        
        // 2 photos pointing Y
        for (int i = 0; i < 2; i++)
        {
            photos.Add(new Photo 
            { 
                FilePath = $"y_{i}.jpg", 
                AlbumId = "test-album",
                OverallScore = 5.0, // High score
                FeatureVector = CreateVector([0f, 10f]) 
            });
        }

        // 245 dummy photos with low score
        for (int i = 0; i < 245; i++)
        {
            photos.Add(new Photo 
            { 
                FilePath = $"dummy_{i}.jpg", 
                AlbumId = "test-album",
                OverallScore = 1.0, 
                FeatureVector = CreateVector([0f, 0f]) 
            });
        }

        await _context.Photos.AddRangeAsync(photos);
        await _context.SaveChangesAsync();

        // Act
        var resultBytes = await _service.GetUserPreferenceVectorAsync();
        Assert.IsNotNull(resultBytes);

        var resultVector = BytesToFloats(resultBytes);
        
        // Expected average: 3*X + 2*Y = (3, 2). Normalized.
        // Vector (3, 2, 0...)
        // Normalized: length = sqrt(9+4) = 3.605
        // (0.832, 0.554, 0...)
        
        var targetX = 3.0f;
        var targetY = 2.0f;
        var len = Math.Sqrt(targetX*targetX + targetY*targetY);
        
        // Verification: Cosine similarity with expected average should be very high (~1.0)
        var expectedAverage = new float[512];
        expectedAverage[0] = (float)(targetX/len);
        expectedAverage[1] = (float)(targetY/len);

        var sim = CosineSimilarity(resultVector, expectedAverage);
        
        // Should be almost identical
        Assert.IsTrue(sim > 0.99, $"Expected average vector, got similarity {sim}");
    }

    [TestMethod]
    public async Task TestLargeDataset_TriggersKMeans_AndAvoidsAverage()
    {
        // Insert a dummy album
        var album = new Album { AlbumId = "test-album-large", Name = "Test Album Large" };
        _context.Albums.Add(album);

        // Target: Take >= 10. Let's aim for Take = 12.
        // Total = 600.
        
        // We create two distinct clusters in Top 12.
        // Cluster A: 6 vectors along X axis [1, 0...]
        // Cluster B: 6 vectors along Y axis [0, 1...]
        // Global Average would be [0.707, 0.707...] (45 degrees)
        
        // With K-Means (K=Clamp(1.2) -> 2), we expect centroids near X or Y.
        // The result is RANDOMLY picked from centroids.
        // So result should be close to X OR close to Y, but NOT close to 45 degrees.

        var photos = new List<Photo>();

        // 6 photos X
        for (int i = 0; i < 6; i++)
        {
            photos.Add(new Photo 
            { 
                FilePath = $"x_{i}.jpg", 
                AlbumId = "test-album-large",
                OverallScore = 5.0, 
                FeatureVector = CreateVector([1f, 0.05f * i]) // Slight noise
            });
        }

        // 6 photos Y
        for (int i = 0; i < 6; i++)
        {
            photos.Add(new Photo 
            { 
                FilePath = $"y_{i}.jpg", 
                AlbumId = "test-album-large",
                OverallScore = 5.0, 
                FeatureVector = CreateVector([0.05f * i, 1f]) // Slight noise
            });
        }

        // 588 dummy photos
        for (int i = 0; i < 588; i++)
        {
            photos.Add(new Photo 
            { 
                FilePath = $"dummy_{i}.jpg", 
                AlbumId = "test-album-large",
                OverallScore = 1.0, 
                FeatureVector = CreateVector([0f, 0f]) 
            });
        }

        await _context.Photos.AddRangeAsync(photos);
        await _context.SaveChangesAsync();

        // Try multiple times since KMeans implies randomness in initialization and selection
        int successCount = 0;
        bool pickedX = false;
        bool pickedY = false;
        
        for (int i = 0; i < 20; i++)
        {
            _cache.Remove("UserPreferenceVector");
            var resultBytes = await _service.GetUserPreferenceVectorAsync();
            Assert.IsNotNull(resultBytes);
            var resultVector = BytesToFloats(resultBytes);

            var simX = CosineSimilarity(resultVector, BytesToFloats(CreateVector([1f, 0f])));
            var simY = CosineSimilarity(resultVector, BytesToFloats(CreateVector([0f, 1f])));
            
            if (simX > 0.9) 
            {
                successCount++;
                pickedX = true;
            }
            else if (simY > 0.9)
            {
                successCount++;
                pickedY = true;
            }
        }

        // Verify that the algorithm is reliable (at least 90% success rate)
        Assert.IsTrue(successCount >= 18, $"Clustering should be reliable. Successes: {successCount}/20");
        // Verify that we can pick different clusters (randomness works)
        Assert.IsTrue(pickedX && pickedY, "Should eventually pick both clusters over multiple runs");
    }
}
