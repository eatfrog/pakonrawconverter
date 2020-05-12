using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = System.Drawing.Rectangle;

namespace PakonImageConverter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private double _gamma = 0.6;


        public MainWindow()
        {
            InitializeComponent();
        }

        private void ImagePanel_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                // The file is in planar mode so RRRRRGGGGGBBBB
                byte[] buffer = new byte[3000 * 2000 * 6];
                // But imagesharp wants it in interleaved mode so RGBRGBRGBRGB
                byte[] interleaved = new byte[3000 * 2000 * 6];

                foreach (var filename in files)
                {
                    using System.IO.StreamReader ms = new System.IO.StreamReader(filename);
                    ms.BaseStream.Read(buffer, 2, (3000 * 2000 * 6) - 2);

                    int pixelSize = 6;

                    // Interleave the buffer
                    for (int i = 0; i != 3000 * 2000 * 2; i += 2)
                    {
                        // R
                        interleaved[i / 2 * pixelSize + 0] = buffer[i];
                        interleaved[i / 2 * pixelSize + 1] = buffer[i + 1];

                        // G - we got to jump over all R bytes first
                        interleaved[i / 2 * pixelSize + 2] = buffer[(2 * 3000 * 2000) + i];
                        interleaved[i / 2 * pixelSize + 3] = buffer[(2 * 3000 * 2000) + i + 1];

                        // B - we got to jump over all G bytes first
                        interleaved[i / 2 * pixelSize + 4] = buffer[(2 * 2 * 3000 * 2000) + i];
                        interleaved[i / 2 * pixelSize + 5] = buffer[(2 * 2 * 3000 * 2000) + i + 1];
                    }

                    using Image<Rgb48> image = Image.LoadPixelData<Rgb48>(interleaved, 3000, 2000);
                    ushort brightestR = 0;
                    ushort brightestG = 0;
                    ushort brightestB = 0;
                    for (int y = 0; y < image.Height; y++)
                    {
                        Span<Rgb48> pixelRowSpan = image.GetPixelRowSpan(y);
                        for (int x = 0; x < image.Width; x++)
                        {
                            var pixel = pixelRowSpan[x];
                            brightestR = pixel.R > brightestR ? pixel.R : brightestR;
                            brightestG = pixel.G > brightestG ? pixel.G : brightestG;
                            brightestB = pixel.B > brightestB ? pixel.B : brightestB;

                        }
                    }
                    double factorR = 65600 / (double)brightestR;
                    double factorG = 65600 / (double)brightestG;
                    double factorB = 65600 / (double)brightestB;
                    const double C = 1;
                    for (int y = 0; y < image.Height; y++)
                    {
                        Span<Rgb48> pixelRowSpan = image.GetPixelRowSpan(y);
                        for (int x = 0; x < image.Width; x++)
                        {
                            var pixel = pixelRowSpan[x];
                            pixel = new Rgb48((ushort)(pixel.R * factorR), (ushort)(pixel.G * factorG), (ushort)(pixel.B * factorB));

                            double rangeR = (double)pixel.R / 65500;
                            double correctionR = C * Math.Pow(rangeR, _gamma * 0.97);
                            pixel.R = (ushort)(correctionR * 65500);

                            double rangeG = (double)pixel.G / 65500;
                            double correctionG = C * Math.Pow(rangeG, _gamma * 1.02);
                            pixel.G = (ushort)(correctionG * 65500);

                            double rangeB = (double)pixel.B / 65500;
                            double correctionB = C * Math.Pow(rangeB, _gamma * 1.03);
                            pixel.B = (ushort)(correctionB * 65500);

                            pixelRowSpan[x] = pixel;
                        }
                    }

                    image.Save("test.png", new PngEncoder() { BitDepth = PngBitDepth.Bit16});
                }
            }
        }

        private void slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _gamma = e.NewValue;
            if (gammaLabel != null)
                gammaLabel.Content = "Gamma: " + e.NewValue;
        }

        private static BitmapImage LoadImage(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0) return null;
            var image = new BitmapImage();
            using (var mem = new MemoryStream(imageData))
            {
                mem.Position = 0;
                image.BeginInit();
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = null;
                image.StreamSource = mem;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }
    }
}
