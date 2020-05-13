using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Channels;
using System.Threading.Tasks;
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
        public bool BwNegative { get; set; }
        public MainWindow()
        {
            InitializeComponent();
            checkBox.DataContext = this;
            DataContext = this;
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
                LoadingProgress.Maximum = files.Count() * 2;
                LoadingProgress.Value = 0;
                Task.Run(() =>
                {
                    // TODO: can be improved through parallell processing
                    foreach (var filename in files)
                    {
                        using StreamReader ms = new StreamReader(filename);
                        ms.BaseStream.Read(buffer, 2, (3000 * 2000 * 6) - 2);

                        Application.Current.Dispatcher.Invoke(() => LoadingProgress.Value++);

                        InterleaveBuffer(buffer, interleaved);

                        using Image<Rgb48> image = Image.LoadPixelData<Rgb48>(interleaved, 3000, 2000);

                        // TODO: We should do this for black point also, currently we are pushing shadows too far up
                        Rgb48 brightest = FindBrightestValues(image);

                        double factorR = 65600 / (double)brightest.R;
                        double factorG = 65600 / (double)brightest.G;
                        double factorB = 65600 / (double)brightest.B;
                        const double C = 1;

                        for (int y = 0; y < image.Height; y++)
                        {
                            Span<Rgb48> pixelRowSpan = image.GetPixelRowSpan(y);
                            for (int x = 0; x < image.Width; x++)
                            {
                                var pixel = pixelRowSpan[x];
                                pixel = new Rgb48((ushort)(pixel.R * factorR), (ushort)(pixel.G * factorG), (ushort)(pixel.B * factorB));

                                // TODO: these variable color balance adjustments should have a setting
                                double rangeR = (double)pixel.R / 65500;
                                double correctionR = C * Math.Pow(rangeR, _gamma * 0.98);
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

                        // TODO: folder setting
                        // TODO: preview before save

                        if (BwNegative)
                        {
                            image.Mutate(x => x.Invert()); // We probably want separate adjustments for bw raws
                            image.Mutate(x => x.Saturate(0f)); // TODO: setting
                        }
                        else
                        {
                            image.Mutate(x => x.Contrast(1.05f)); // TODO: setting
                            image.Mutate(x => x.Saturate(1.05f)); // TODO: setting
                        }

                        image.Save(filename.Split("\\")[^1].Replace("raw", "png"), new PngEncoder() { BitDepth = PngBitDepth.Bit16 });

                        Application.Current.Dispatcher.Invoke(() => LoadingProgress.Value++);
                    }
                    SystemSounds.Beep.Play();
                });
            }
        }

        private static Rgb48 FindBrightestValues(Image<Rgb48> image)
        {
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
            return new Rgb48(brightestR, brightestG, brightestB);
        }

        private static void InterleaveBuffer(byte[] buffer, byte[] interleaved)
        {
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
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _gamma = e.NewValue;
            if (gammaLabel != null)
                gammaLabel.Content = "Gamma: " + e.NewValue;
        }
        

        // TODO: can we load a bytearray through this into wpf?
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
