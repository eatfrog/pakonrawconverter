using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PakonRawFileLib
{
    public static class ImageExtensions
    {
        public static Bitmap ToBitmap(this byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                return new Bitmap(ms);
            }
        }

        public static BitmapSource ToBitmapSource(this Bitmap bmp)
        {
            var bitmapData = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                bmp.HorizontalResolution, bmp.VerticalResolution,
                PixelFormats.Rgb48, null,
                bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);

            bmp.UnlockBits(bitmapData);
            return bitmapSource;
        }

        public static SixLabors.ImageSharp.Image<TPixel> ToImage<TPixel>(this BitmapSource bmp)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var width = bmp.PixelWidth;
            var height = bmp.PixelHeight;
            var stride = width * Unsafe.SizeOf<TPixel>();
            var buffer = new byte[height * stride];
            bmp.CopyPixels(buffer, stride, 0);
            return SixLabors.ImageSharp.Image.LoadPixelData<TPixel>(buffer, width, height);
        }
    }
}