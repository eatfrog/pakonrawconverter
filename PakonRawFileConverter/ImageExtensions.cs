using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;

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

        public static void SetWhiteAndBlackpoint(this Image<Rgb48> image, bool bwNegative)
        {
            // Note that for what is dark/bright depends on if the image is positive or negative
            // Naming here is based on a negative image which means low value is bright after inversion
            Rgb48 darkest = FindLargestValue(image, bwNegative);
            Rgb48 brightest = FindSmallestValue(image, bwNegative);

            image.ProcessPixelRows(accessor => { 

                for (int y = 0; y < image.Height; y++)
                {
                    Span<Rgb48> pixelRowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = pixelRowSpan[x];

                        /* Set levels
                         * ChannelValue = 65 535 * ( ( ChannelValue - BrightestValue ) /  ( DarkestValue - BrightestValue ) )
                         * Again, please note that Dark/Bright is depending on if the image is a positive or negative
                         * and here I am assuming we are looking at a negative image pre-inversion
                         */

                        double r = (double)(pixel.R - brightest.R) / (darkest.R - brightest.R);
                        double g = (double)(pixel.G - brightest.G) / (darkest.G - brightest.G);
                        double b = (double)(pixel.B - brightest.B) / (darkest.B - brightest.B);
                        r = Math.Clamp(r, 0, 1);
                        g = Math.Clamp(g, 0, 1);
                        b = Math.Clamp(b, 0, 1);
                        pixel = new Rgb48((ushort)(65_534 * r),
                                          (ushort)(65_534 * g),
                                          (ushort)(65_534 * b));

                        pixelRowSpan[x] = pixel;
                    }
                }
            });
        }

        // If pos image, 0 is black and 65535 is white
        // If neg image, 0 is white and 65535 is black
        private static Rgb48 FindLargestValue(Image<Rgb48> image, bool bwNegative)
        {
            ConcurrentDictionary<string, ushort> darkestValues = new ConcurrentDictionary<string, ushort>();
            darkestValues.TryAdd("R", 0);
            darkestValues.TryAdd("G", 0);
            darkestValues.TryAdd("B", 0);

            image.ProcessPixelRows(accessor => { 
                for (int y = 0; y < image.Height; y++)
                { 
                    Span<Rgb48> pixelRowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        if (pixelRowSpan[x].G > darkestValues["R"])
                            darkestValues.TryUpdate("R", pixelRowSpan[x].R, darkestValues["R"]);

                        if (pixelRowSpan[x].G > darkestValues["G"])
                            darkestValues.TryUpdate("G", pixelRowSpan[x].G, darkestValues["G"]);

                        if (pixelRowSpan[x].B > darkestValues["B"])
                            darkestValues.TryUpdate("B", pixelRowSpan[x].B, darkestValues["B"]);
                    }
                }
            });

            if (bwNegative)
            {
                darkestValues["R"] -= darkestValues["R"] > 99 ? (ushort) 100 : darkestValues["R"];
                darkestValues["G"] -= darkestValues["G"] > 99 ? (ushort) 100 : darkestValues["G"];
                darkestValues["B"] -= darkestValues["B"] > 99 ? (ushort) 100 : darkestValues["B"];
            }

            return new Rgb48(darkestValues["R"], darkestValues["G"], darkestValues["B"]);
        }

        // If pos image, 0 is black and 65535 is white
        // If neg image, 0 is white and 65535 is black
        private static Rgb48 FindSmallestValue(Image<Rgb48> image, bool bwNegative)
        {
            ConcurrentDictionary<string, ushort> smallestValues = new ConcurrentDictionary<string, ushort>();
            smallestValues.TryAdd("R", 65_534);
            smallestValues.TryAdd("G", 65_534);
            smallestValues.TryAdd("B", 65_534);

            image.ProcessPixelRows(accessor => {
                for (int y = 0; y < image.Height; y++)
                {
                    Span<Rgb48> pixelRowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        if (pixelRowSpan[x].R < smallestValues["R"])
                            smallestValues.TryUpdate("R", pixelRowSpan[x].R, smallestValues["R"]);

                        if (pixelRowSpan[x].G < smallestValues["G"])
                            smallestValues.TryUpdate("G", pixelRowSpan[x].G, smallestValues["G"]);

                        if (pixelRowSpan[x].B < smallestValues["B"])
                            smallestValues.TryUpdate("B", pixelRowSpan[x].B, smallestValues["B"]);
                    }
                }
            });

            // bump it up just a notch
            if (bwNegative)
            {
                smallestValues["R"] = Math.Clamp(smallestValues["R"], (ushort)0, (ushort)65_454);
                smallestValues["G"] = Math.Clamp(smallestValues["G"], (ushort)0, (ushort)65_454);
                smallestValues["B"] = Math.Clamp(smallestValues["B"], (ushort)0, (ushort)65_454);

                smallestValues["R"] += 80; smallestValues["G"] += 80; smallestValues["B"] += 80;
            }

            return new Rgb48(smallestValues["R"], smallestValues["G"], smallestValues["B"]);
        }
    }
}