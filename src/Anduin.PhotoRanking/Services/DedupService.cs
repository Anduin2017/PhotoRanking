using Anduin.PhotoRanking.Models;

namespace Anduin.PhotoRanking.Services;

public class DisjointSetUnion
{
    private readonly int[] _parent;
    private readonly int[] _size;

    public DisjointSetUnion(int n)
    {
        _parent = new int[n];
        _size = new int[n];
        for (var i = 0; i < n; i++)
        {
            _parent[i] = i;
            _size[i] = 1;
        }
    }

    private int Find(int i)
    {
        if (_parent[i] == i)
        {
            return i;
        }

        return _parent[i] = Find(_parent[i]);
    }

    public void Union(int i, int j)
    {
        var iRoot = Find(i);
        var jRoot = Find(j);

        if (iRoot != jRoot)
        {
            if (_size[iRoot] < _size[jRoot])
            {
                _parent[iRoot] = jRoot;
                _size[jRoot] += _size[iRoot];
            }
            else
            {
                _parent[jRoot] = iRoot;
                _size[iRoot] += _size[jRoot];
            }
        }
    }

    public IEnumerable<int[]> AsGroups(bool ignoreSingletons = true)
    {
        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < _parent.Length; i++)
        {
            var root = Find(i);
            if (!groups.ContainsKey(root))
            {
                groups[root] = new List<int>();
            }

            groups[root].Add(i);
        }

        var results = groups.Values.Select(g => g.ToArray());
        if (ignoreSingletons)
        {
            results = results.Where(g => g.Length > 1);
        }

        return results;
    }
}

public class DedupService
{
    public List<Photo[]> GetDuplicateGroups(List<Photo> photos, double similarityBarStr)
    {
        var similarityThreshold = similarityBarStr / 100.0;
        
        // Only consider photos that have a valid FeatureVector
        var validPhotos = photos.Where(p => p.FeatureVector != null).ToArray();
        var n = validPhotos.Length;

        // Convert byte[] vectors to float[] ahead of time
        var vectors = new float[n][];
        for (var i = 0; i < n; i++)
        {
            vectors[i] = ImageAnalysisService.ByteArrayToFloatArray(validPhotos[i].FeatureVector!);
        }

        var dsu = new DisjointSetUnion(n);

        // O(N^2) comparison. Since N is typically < 10,000, this loop is very fast in C# memory.
        Parallel.For(0, n, i =>
        {
            for (var j = i + 1; j < n; j++)
            {
                var sim = ImageAnalysisService.CalculateCosineSimilarity(vectors[i], vectors[j]);
                if (sim >= similarityThreshold)
                {
                    lock (dsu)
                    {
                        dsu.Union(i, j);
                    }
                }
            }
        });

        // Get groups from DSU
        var dsuGroups = dsu.AsGroups(ignoreSingletons: true);

        var resultGroups = new List<Photo[]>();

        foreach (var groupIndices in dsuGroups)
        {
            var groupPhotos = groupIndices.Select(i => validPhotos[i]).ToList();

            // Find the "Best" photo to place first.
            // Prefer the final manual score; use the prediction only for unrated duplicates.
            var sortedGroup = groupPhotos
                .OrderByDescending(p => p.IndependentScore ?? p.EstimatedScore ?? -1)
                .ThenByDescending(p => p.FileSize)
                .ToArray();

            resultGroups.Add(sortedGroup);
        }

        return resultGroups;
    }
}
