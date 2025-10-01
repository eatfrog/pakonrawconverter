using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace PakonRawFileLib
{
    public class PakonRawProcessor
    {
        public Image<Rgb48> ProcessImage(string filename, bool isBwImage, double gamma, float contrast, float saturation)
        {
            StreamReader ms = new StreamReader(filename);

            var header = new byte[16];

            ms.BaseStream.Read(header, 0, 16);
            int width = (int)BitConverter.ToUInt32(header, 4);
            int height = (int)BitConverter.ToUInt32(header, 8);

            if (width > 5000 || height > 5000) throw new InvalidOperationException("You are probably not processing a pakon raw file");

            byte[] buffer = new byte[width * height * 6];
            byte[] interleaved = new byte[width * height * 6];

            ms.BaseStream.Read(buffer, 0, width * height * 6);

            InterleaveBuffer(width, height, buffer, interleaved);
            var image = Image.LoadPixelData<Rgb48>(interleaved, width, height);
            SetWhiteAndBlackpoint(image, isBwImage);

            GammaCorrection(image, gamma);

            if (isBwImage)
            {
                image.Mutate(x => x.Invert());
                image.Mutate(x => x.Saturate(0f));
            }
            else
            {
                image.Mutate(x => x.Contrast(contrast));
                image.Mutate(x => x.Saturate(saturation));
            }

            return image;
        }

        private static void InterleaveBuffer(int width, int height, byte[] buffer, byte[] interleaved)
        {
            int pixelSize = 6;

            for (int i = 0; i != width * height * 2; i += 2)
            {
                interleaved[i / 2 * pixelSize + 0] = buffer[i];
                interleaved[i / 2 * pixelSize + 1] = buffer[i + 1];

                interleaved[i / 2 * pixelSize + 2] = buffer[(2 * width * height) + i];
                interleaved[i / 2 * pixelSize + 3] = buffer[(2 * width * height) + i + 1];

                interleaved[i / 2 * pixelSize + 4] = buffer[(2 * 2 * width * height) + i];
                interleaved[i / 2 * pixelSize + 5] = buffer[(2 * 2 * width * height) + i + 1];
            }
        }

        private void GammaCorrection(Image<Rgb48> image, double gamma)
        {
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgb48> row = accessor.GetRowSpan(y);
                    foreach (ref Rgb48 pixel in row)
                    {
                        double rangeR = (double)pixel.R / 65500;
                        double correctionR = Math.Pow(rangeR, gamma * 0.98);
                        pixel.R = (ushort)(correctionR * 65500);

                        double rangeG = (double)pixel.G / 65500;
                        double correctionG = Math.Pow(rangeG, gamma * 1.02);
                        pixel.G = (ushort)(correctionG * 65500);

                        double rangeB = (double)pixel.B / 65500;
                        double correctionB = Math.Pow(rangeB, gamma * 1.03);
                        pixel.B = (ushort)(correctionB * 65500);
                    }
                }
            });
        }

        public (Rgb48, Rgb48) SetWhiteAndBlackpoint(Image<Rgb48> image, bool bwNegative)
        {
            (Rgb48 darkest, Rgb48 brightest) = FindDarkestAndBrightestValues(image, bwNegative);

            image.ProcessPixelRows(accessor => {

                for (int y = 0; y < image.Height; y++)
                {
                    Span<Rgb48> pixelRowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = pixelRowSpan[x];

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

            return (darkest, brightest);
        }

        private (Rgb48, Rgb48) FindDarkestAndBrightestValues(Image<Rgb48> image, bool bwNegative)
        {
            ConcurrentDictionary<string, ushort> darkestValues = new ConcurrentDictionary<string, ushort>();
            darkestValues.TryAdd("R", 0);
            darkestValues.TryAdd("G", 0);
            darkestValues.TryAdd("B", 0);

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
                        if (pixelRowSpan[x].R > darkestValues["R"])
                            darkestValues.TryUpdate("R", pixelRowSpan[x].R, darkestValues["R"]);
                        if (pixelRowSpan[x].G > darkestValues["G"])
                            darkestValues.TryUpdate("G", pixelRowSpan[x].G, darkestValues["G"]);
                        if (pixelRowSpan[x].B > darkestValues["B"])
                            darkestValues.TryUpdate("B", pixelRowSpan[x].B, darkestValues["B"]);

                        if (pixelRowSpan[x].R < smallestValues["R"])
                            smallestValues.TryUpdate("R", pixelRowSpan[x].R, smallestValues["R"]);
                        if (pixelRowSpan[x].G < smallestValues["G"])
                            smallestValues.TryUpdate("G", pixelRowSpan[x].G, smallestValues["G"]);
                        if (pixelRowSpan[x].B < smallestValues["B"])
                            smallestValues.TryUpdate("B", pixelRowSpan[x].B, smallestValues["B"]);
                    }
                }
            });

            if (bwNegative)
            {
                darkestValues["R"] -= darkestValues["R"] > 99 ? (ushort)100 : darkestValues["R"];
                darkestValues["G"] -= darkestValues["G"] > 99 ? (ushort)100 : darkestValues["G"];
                darkestValues["B"] -= darkestValues["B"] > 99 ? (ushort)100 : darkestValues["B"];

                smallestValues["R"] = Math.Clamp(smallestValues["R"], (ushort)0, (ushort)65_454);
                smallestValues["G"] = Math.Clamp(smallestValues["G"], (ushort)0, (ushort)65_454);
                smallestValues["B"] = Math.Clamp(smallestValues["B"], (ushort)0, (ushort)65_454);

                smallestValues["R"] += 80; smallestValues["G"] += 80; smallestValues["B"] += 80;
            }

            return (new Rgb48(darkestValues["R"], darkestValues["G"], darkestValues["B"]), new Rgb48(smallestValues["R"], smallestValues["G"], smallestValues["B"]));
        }
    }
}