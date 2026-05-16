using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Anduin.PhotoRanking.Services;

public class ImageAnalysisService
{
    // CLIP output dimension is 512

    private static Lazy<InferenceSession> _session = new(() => {
        try
        {
            var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "clip-visual.onnx");
            var options = new Microsoft.ML.OnnxRuntime.SessionOptions();
            return new InferenceSession(modelPath, options);
        }
        catch (Exception e)
        {
            Console.WriteLine($"CRITICAL ERROR initializing OnnxRuntime: {e}");
            if (e.InnerException != null) Console.WriteLine($"Inner: {e.InnerException}");
            throw;
        }
    });

    public byte[]? GenerateVector(string filePath)
    {
        try
        {
            using var original = SKBitmap.Decode(filePath);
            if (original == null) return null;

            // Resize to 224x224 (CLIP standard) with center crop
            float scale = Math.Max(224f / original.Width, 224f / original.Height);
            int resizedW = (int)(original.Width * scale);
            int resizedH = (int)(original.Height * scale);
            using var resized = original.Resize(new SKSizeI(resizedW, resizedH), new SKSamplingOptions(SKFilterMode.Linear));
            if (resized == null) return null;

            using var image = new SKBitmap(224, 224);
            int cropX = (resizedW - 224) / 2;
            int cropY = (resizedH - 224) / 2;
            using (var canvas = new SKCanvas(image))
            {
                canvas.DrawBitmap(resized,
                    new SKRect(cropX, cropY, cropX + 224, cropY + 224),
                    new SKRect(0, 0, 224, 224));
            }

            var input = new DenseTensor<float>(new[] { 1, 3, 224, 224 });

            // CLIP Mean and Std
            var mean = new[] { 0.48145466f, 0.4578275f, 0.40821073f };
            var std = new[] { 0.26862954f, 0.26130258f, 0.27577711f };

            for (int y = 0; y < 224; y++)
            {
                for (int x = 0; x < 224; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    // Normalize: (Pixel - Mean) / Std
                    input[0, 0, y, x] = ((pixel.Red / 255f) - mean[0]) / std[0];
                    input[0, 1, y, x] = ((pixel.Green / 255f) - mean[1]) / std[1];
                    input[0, 2, y, x] = ((pixel.Blue / 255f) - mean[2]) / std[2];
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("pixel_values", input)
            };

            using var results = _session.Value.Run(inputs);
            var output = results.First().AsTensor<float>();

            return FloatArrayToByteArray(output.ToArray());
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error processing image {filePath}: {e.Message}");
            return null;
        }
    }

    private byte[] FloatArrayToByteArray(float[] floatArray)
    {
        // 512 floats * 4 bytes = 2048 bytes
        var byteArray = new byte[floatArray.Length * 4];
        Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }

    public static float[] ByteArrayToFloatArray(byte[] byteArray)
    {
        var floatArray = new float[byteArray.Length / 4];
        Buffer.BlockCopy(byteArray, 0, floatArray, 0, byteArray.Length);
        return floatArray;
    }

    public static double CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length) return 0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        if (normA == 0 || normB == 0) return 0;

        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
