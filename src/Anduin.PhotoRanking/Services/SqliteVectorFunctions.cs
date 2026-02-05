using Microsoft.Data.Sqlite;

namespace Anduin.PhotoRanking.Services;

public static class SqliteVectorFunctions
{
    public static void RegisterVectorDistance(SqliteConnection connection)
    {
        connection.CreateFunction("VectorDistance", (byte[]? a, byte[]? b) =>
        {
            if (a == null || b == null) return double.MaxValue;
            
            var vectorA = ImageAnalysisService.ByteArrayToFloatArray(a);
            var vectorB = ImageAnalysisService.ByteArrayToFloatArray(b);
            
            return CosineDistance(vectorA, vectorB);
        });
    }

    private static double CosineDistance(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length) return 1.0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        if (normA == 0 || normB == 0) return 1.0;

        return 1.0 - (dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}
