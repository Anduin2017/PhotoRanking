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
            var topVectorsBytes = await _context.Photos
                .Where(p => p.FeatureVector != null)
                .OrderByDescending(p => p.OverallScore)
                .Take(takeCount)
                .Select(p => p.FeatureVector)
                .ToListAsync();

            if (topVectorsBytes.Count == 0) return null;

            var vectors = new List<float[]>();
            foreach (var bytes in topVectorsBytes)
            {
                if (bytes != null)
                {
                    vectors.Add(ImageAnalysisService.ByteArrayToFloatArray(bytes));
                }
            }

            if (vectors.Count == 0) return null;

            float[] selectedVector;

            // 3. Strategy Selection: K-Means Clustering vs Global Average
            // If we have enough data points, use clustering to find distinct tastes.
            // Otherwise, fallback to global average.
            if (vectors.Count >= 10)
            {
                // Dynamic K: roughly 1 cluster per 10 photos.
                // Min 2: Force separation to avoid "average face".
                // Max 6: Cap to prevent over-segmentation and performance issues.
                int k = Math.Clamp(vectors.Count / 10, 2, 6);
                
                var centroids = RunKMeans(vectors, k);
                
                // Randomly select one cluster center to diversify the feed
                // This solves the "Average Face" problem where averaging anime + landscape = nonsense.
                selectedVector = centroids[RandomNumberGenerator.GetInt32(centroids.Count)];
                _logger.LogInformation("Clustered {Count} photos into {K} tastes. Selected taste index {Index}.", vectors.Count, centroids.Count, centroids.IndexOf(selectedVector));
            }
            else
            {
                // Fallback: Global Average
                selectedVector = CalculateMeanVector(vectors);
                _logger.LogInformation("Calculated global average preference from {Count} photos (top {Percent}%)", vectors.Count, percentile);
            }

            // 4. Normalize (unit vector)
            Normalize(selectedVector);

            // 5. Convert back to byte[]
            return FloatArrayToByteArray(selectedVector);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating user preference vector");
            return null;
        }
    }

    private float[] CalculateMeanVector(List<float[]> vectors)
    {
        int dimension = vectors[0].Length;
        var sumVector = new float[dimension];

        foreach (var vector in vectors)
        {
            if (vector.Length != dimension) continue;
            for (int i = 0; i < dimension; i++)
            {
                sumVector[i] += vector[i];
            }
        }
        return sumVector;
    }

    private List<float[]> RunKMeans(List<float[]> vectors, int k)
    {
        int n = vectors.Count;
        int dim = vectors[0].Length;
        int maxIterations = 20;

        // 1. Initialize Centroids using K-Means++ style
        var centroids = new List<float[]>();
        
        // Pick first centroid randomly
        int firstIdx = RandomNumberGenerator.GetInt32(n);
        var firstCentroid = new float[dim];
        Array.Copy(vectors[firstIdx], firstCentroid, dim);
        centroids.Add(firstCentroid);

        while (centroids.Count < k)
        {
            // Pick next centroid with probability proportional to distance squared from nearest existing centroid
            var distances = new double[n];
            double sumDist = 0;
            for (int i = 0; i < n; i++)
            {
                double minDistance = double.MaxValue;
                foreach (var c in centroids)
                {
                    // Use (1 - similarity) as distance
                    double sim = ImageAnalysisService.CalculateCosineSimilarity(vectors[i], c);
                    double dist = 1.0 - sim;
                    if (dist < minDistance) minDistance = dist;
                }
                distances[i] = Math.Pow(minDistance, 2);
                sumDist += distances[i];
            }

            double r = Random.Shared.NextDouble() * sumDist;
            double cumulative = 0;
            int nextIdx = n - 1;
            for (int i = 0; i < n; i++)
            {
                cumulative += distances[i];
                if (r <= cumulative)
                {
                    nextIdx = i;
                    break;
                }
            }

            var nextCentroid = new float[dim];
            Array.Copy(vectors[nextIdx], nextCentroid, dim);
            centroids.Add(nextCentroid);
        }

        int[] assignments = new int[n];
        Array.Fill(assignments, -1);

        for (int iter = 0; iter < maxIterations; iter++)
        {
            bool changed = false;

            // 2. Assignment Step
            var newClusterSums = new float[k][];
            var newClusterCounts = new int[k];
            for (int i = 0; i < k; i++) newClusterSums[i] = new float[dim];

            for (int i = 0; i < n; i++)
            {
                double bestSim = -2.0;
                int bestCluster = 0;

                for (int c = 0; c < k; c++)
                {
                    double sim = ImageAnalysisService.CalculateCosineSimilarity(vectors[i], centroids[c]);
                    if (sim > bestSim)
                    {
                        bestSim = sim;
                        bestCluster = c;
                    }
                }

                if (assignments[i] != bestCluster)
                {
                    assignments[i] = bestCluster;
                    changed = true;
                }

                for (int d = 0; d < dim; d++)
                {
                    newClusterSums[bestCluster][d] += vectors[i][d];
                }
                newClusterCounts[bestCluster]++;
            }

            if (!changed) break;

            // 3. Update Step
            for (int c = 0; c < k; c++)
            {
                if (newClusterCounts[c] > 0)
                {
                    centroids[c] = newClusterSums[c];
                    Normalize(centroids[c]);
                }
                else
                {
                    var randomVector = vectors[RandomNumberGenerator.GetInt32(n)];
                    Array.Copy(randomVector, centroids[c], dim);
                }
            }
        }

        return centroids;
    }

    private void Normalize(float[] vector)
    {
        double normSq = 0;
        for (int i = 0; i < vector.Length; i++)
        {
            normSq += vector[i] * vector[i];
        }
        
        double norm = Math.Sqrt(normSq);
        if (norm > 1e-9)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= (float)norm;
            }
        }
    }

    private byte[] FloatArrayToByteArray(float[] floatArray)
    {
        var byteArray = new byte[floatArray.Length * 4];
        Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }
}
