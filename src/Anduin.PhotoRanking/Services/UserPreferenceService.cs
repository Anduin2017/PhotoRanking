using Anduin.PhotoRanking.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;

namespace Anduin.PhotoRanking.Services;

public class UserPreferenceService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserPreferenceService> _logger;

    public UserPreferenceService(
        AppDbContext context,
        IMemoryCache cache,
        ILogger<UserPreferenceService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<byte[]?> GetUserPreferenceVectorAsync()
    {
        // Cache for 6 minutes
        return await _cache.GetOrCreateAsync("UserPreferenceVector", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(6);
            return await CalculateUserPreferenceVectorAsync();
        });
    }

    private async Task<byte[]?> CalculateUserPreferenceVectorAsync()
    {
        try
        {
            // 1. Determine percentile (random between 1.7% and 2.3%)
            var random = RandomNumberGenerator.GetInt32(17, 24); // 17 to 23
            var percentile = random / 10.0; // 1.7 to 2.3

            // 2. Get top N% photos by OverallScore
            var totalCount = await _context.Photos.CountAsync(p => p.FeatureVector != null);
            if (totalCount == 0) return null;

            var takeCount = (int)Math.Max(1, Math.Ceiling(totalCount * percentile / 100.0));

            // Optimize: Only fetch FeatureVector for top photos
            var topVectors = await _context.Photos
                .Where(p => p.FeatureVector != null)
                .OrderByDescending(p => p.OverallScore)
                .Take(takeCount)
                .Select(p => p.FeatureVector)
                .ToListAsync();

            if (topVectors.Count == 0) return null;

            // 3. Sum vectors
            // Assuming 512 dimensions (CLIP standard). 
            // We can detect dimension from the first vector.
            int dimension = 0;
            float[]? sumVector = null;

            foreach (var vectorBytes in topVectors)
            {
                if (vectorBytes == null) continue;

                var vector = ImageAnalysisService.ByteArrayToFloatArray(vectorBytes);
                
                if (sumVector == null)
                {
                    dimension = vector.Length;
                    sumVector = new float[dimension];
                }

                if (vector.Length != dimension)
                {
                    _logger.LogWarning("Vector dimension mismatch. Expected {Dim}, got {Len}", dimension, vector.Length);
                    continue;
                }

                for (int i = 0; i < dimension; i++)
                {
                    sumVector[i] += vector[i];
                }
            }

            if (sumVector == null) return null;

            // 4. Normalize (Take the module? user said "take modulo", likely means normalize to unit vector)
            // Even for cosine distance, normalizing the query vector is good practice though not strictly required if only ranking (since magnitude is constant for all comparisons).
            // But let's normalize it to be safe and consistent.
            
            double normSq = 0;
            for (int i = 0; i < dimension; i++)
            {
                normSq += sumVector[i] * sumVector[i];
            }
            
            double norm = Math.Sqrt(normSq);
            if (norm > 0)
            {
                for (int i = 0; i < dimension; i++)
                {
                    sumVector[i] /= (float)norm;
                }
            }

            _logger.LogInformation("Calculated user preference vector from top {Count} photos (top {Percent}%)", topVectors.Count, percentile);

            // 5. Convert back to byte[]
            return FloatArrayToByteArray(sumVector);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating user preference vector");
            return null;
        }
    }

    private byte[] FloatArrayToByteArray(float[] floatArray)
    {
        var byteArray = new byte[floatArray.Length * 4];
        Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }
}
