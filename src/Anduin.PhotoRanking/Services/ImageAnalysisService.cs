using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Anduin.PhotoRanking.Services;

public class ImageAnalysisService
{
    // CLIP output dimension is 512


    private static Lazy<InferenceSession> _session = new(() => {
        var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "clip-visual.onnx");
        return new InferenceSession(modelPath);
    });

    public byte[]? GenerateVector(string filePath)
    {
        try
        {
            using var image = Image.Load<Rgba32>(filePath);
            
            // Resize to 224x224 (CLIP standard)
            image.Mutate(x => x
                .Resize(new ResizeOptions 
                {
                    Size = new Size(224, 224),
                    Mode = ResizeMode.Crop
                }));

            var input = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
            
            // CLIP Mean and Std
            var mean = new[] { 0.48145466f, 0.4578275f, 0.40821073f };
            var std = new[] { 0.26862954f, 0.26130258f, 0.27577711f };

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < pixelRow.Length; x++)
                    {
                        var pixel = pixelRow[x];
                        
                        // Normalize: (Pixel - Mean) / Std
                        input[0, 0, y, x] = ((pixel.R / 255f) - mean[0]) / std[0];
                        input[0, 1, y, x] = ((pixel.G / 255f) - mean[1]) / std[1];
                        input[0, 2, y, x] = ((pixel.B / 255f) - mean[2]) / std[2];
                    }
                }
            });

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
}
