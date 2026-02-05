using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Anduin.PhotoRanking.Services;

public class ImageAnalysisService
{
    private const int ResizeWidth = 32;
    private const int ResizeHeight = 32;

    public byte[]? GenerateVector(string filePath)
    {
        try
        {
            using var image = Image.Load<Rgba32>(filePath);
            
            // Resize to 32x32 to reduce dimensionality while keeping structure
            image.Mutate(x => x
                .Resize(ResizeWidth, ResizeHeight)
                .Grayscale());

            var floatArray = new float[ResizeWidth * ResizeHeight];
            var index = 0;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < pixelRow.Length; x++)
                    {
                        // R, G, B are same in grayscale. Normalize to 0-1.
                        floatArray[index++] = pixelRow[x].R / 255f;
                    }
                }
            });

            return FloatArrayToByteArray(floatArray);
        }
        catch
        {
            return null;
        }
    }

    private byte[] FloatArrayToByteArray(float[] floatArray)
    {
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
