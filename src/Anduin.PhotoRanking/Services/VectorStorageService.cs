using System.Collections.Concurrent;
using System.Diagnostics;
using Anduin.PhotoRanking.Data;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Services;

public class VectorStorageService(IServiceScopeFactory scopeFactory, ILogger<VectorStorageService> logger)
{
    // Use ConcurrentDictionary for thread-safe updates, though search iterates values.
    // Key: PhotoId, Value: Normalized Float Vector
    private readonly ConcurrentDictionary<int, float[]> _vectorIndex = new();
    private bool _initialized;

    public int Count => _vectorIndex.Count;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        logger.LogInformation("Initializing In-Memory Vector Index...");
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Load all IDs and Vectors. This might take memory.
        // We select strictly what we need.
        var data = await context.Photos
            .Where(p => p.FeatureVector != null)
            .Select(p => new { p.Id, p.FeatureVector })
            .AsNoTracking()
            .ToListAsync();

        var sw = Stopwatch.StartNew();
        int count = 0;
        foreach (var item in data)
        {
            if (item.FeatureVector != null)
            {
                var floatVec = ImageAnalysisService.ByteArrayToFloatArray(item.FeatureVector);
                // Pre-normalize if possible? The ByteArrayToFloatArray logic in ImageAnalysisService 
                // generates 0-1 values but not unit vectors. 
                // Cosine Similarity requires magnitude division if not unit vectors.
                // For performance, we assume standard Cosine Distance calculation is affordable or we optimize later.
                _vectorIndex[item.Id] = floatVec;
                count++;
            }
        }
        
        sw.Stop();
        _initialized = true;
        logger.LogInformation("Loaded {Count} vectors into memory in {Elapsed}.", count, sw.Elapsed);
    }

    public void UpdateOrAdd(int id, byte[] vectorBytes)
    {
        var floatVec = ImageAnalysisService.ByteArrayToFloatArray(vectorBytes);
        _vectorIndex[id] = floatVec;
    }

    public void Remove(int id)
    {
        _vectorIndex.TryRemove(id, out _);
    }

    public List<int> Search(float[] targetVector, int topK = 10)
    {
        // Full scan calculation.
        // For 400k items, simple loop is okay in C# (~100-200ms).
        // SIMD optimization could be added here if needed.

        // Calculate distances
        // Using a PriorityQueue would be slightly better for TopK than sorting all, 
        // but LINQ OrderBy is easiest to implement first.
        
        var results = _vectorIndex
            .Select(kvp => new { Id = kvp.Key, Distance = CosineDistance(targetVector, kvp.Value) })
            .OrderBy(x => x.Distance)
            .Take(topK)
            .Select(x => x.Id)
            .ToList();

        return results;
    }

    private static double CosineDistance(float[] vectorA, float[] vectorB)
    {
        // Standard Cosine Distance: 1 - (A . B) / (|A| * |B|)
        // If we pre-calculate magnitudes, we can speed this up.
        
        if (vectorA.Length != vectorB.Length) return 1.0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            var a = vectorA[i];
            var b = vectorB[i];
            dotProduct += a * b;
            normA += a * a;
            normB += b * b;
        }

        if (normA == 0 || normB == 0) return 1.0;

        return 1.0 - (dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}
