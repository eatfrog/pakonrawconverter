using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;

namespace PakonRawFileLib
{
    public static class ImageExtensions
    {
        public static byte[] ToArray<TPixel>(this Image<TPixel> image, IImageEncoder encoder) where TPixel : unmanaged, IPixel<TPixel>
        {
            using (var memoryStream = new MemoryStream())
            {
                image.Save(memoryStream, encoder);
                return memoryStream.ToArray();
            }
        }
    }
}