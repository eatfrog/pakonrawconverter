using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

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


                LoadingProgress.Maximum = files.Count() * 2;
                LoadingProgress.Value = 0;
                Task.Run(() =>
                {
                    // TODO: we are allocating a ton of buffers now, what happens on a low mem machine?
                    Parallel.ForEach(files, filename =>
                    {
                        // The file is in planar mode so RRRRRGGGGGBBBB
                        // TODO: setting for different sized images, works only in 3000*2000 now (non-plus users take note)
                        byte[] buffer = new byte[3000 * 2000 * 6];
                        // But imagesharp wants it in interleaved mode so RGBRGBRGBRGB
                        byte[] interleaved = new byte[3000 * 2000 * 6];

                        using StreamReader ms = new StreamReader(filename);
                        var _ = new byte[16];
                        ms.BaseStream.Read(_, 0, 16);

                        ms.BaseStream.Read(buffer, 0, 3000 * 2000 * 6);

                        Application.Current.Dispatcher.Invoke(() => LoadingProgress.Value++);

                        InterleaveBuffer(buffer, interleaved);

                        using Image<Rgb48> image = Image.LoadPixelData<Rgb48>(interleaved, 3000, 2000);

                        SetWhiteAndBlackpoint(image);

                        GammaCorrection(image);

                        // TODO: folder setting
                        // TODO: preview before save
                        string pngFilename = filename.Replace("raw", "png");

                        if (BwNegative)
                        {
                            image.Mutate(x => x.Invert()); // We probably want separate adjustments for bw raws
                            image.Mutate(x => x.Saturate(0f)); // TODO: setting                            
                            image.Save(pngFilename, new PngEncoder() { ColorType = PngColorType.Grayscale, BitDepth = PngBitDepth.Bit16 });
                        }
                        else
                        {
                            image.Mutate(x => x.Contrast(1.05f)); // TODO: setting
                            image.Mutate(x => x.Saturate(1.05f)); // TODO: setting
                            var test = image.ToArray(new BmpEncoder());
                            Application.Current.Dispatcher.Invoke(() => imageBox.Source = test.ToBitmap().ToBitmapSource());

                            image.Save(pngFilename, new PngEncoder() { BitDepth = PngBitDepth.Bit16 });
                        }

                        Application.Current.Dispatcher.Invoke(() => LoadingProgress.Value++);
                    });
                    SystemSounds.Beep.Play();
                });
            }
        }

        private void GammaCorrection(Image<Rgb48> image) => Parallel.For(0, image.Height, y =>
        {
            Span<Rgb48> pixelRowSpan = image.GetPixelRowSpan(y);
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = pixelRowSpan[x];

                // TODO: these variable color balance adjustments should have a setting
                double rangeR = (double)pixel.R / 65500;
                double correctionR = Math.Pow(rangeR, _gamma * 0.98);
                pixel.R = (ushort)(correctionR * 65500);

                double rangeG = (double)pixel.G / 65500;
                double correctionG = Math.Pow(rangeG, _gamma * 1.02);
                pixel.G = (ushort)(correctionG * 65500);

                double rangeB = (double)pixel.B / 65500;
                double correctionB = Math.Pow(rangeB, _gamma * 1.03);
                pixel.B = (ushort)(correctionB * 65500);

                pixelRowSpan[x] = pixel;
            }
        });
        private static void SetWhiteAndBlackpoint(Image<Rgb48> image)
        {
            // Note that for what is dark/bright depends on if the image is positive or negative
            // Naming here is based on a negative image which means low value is bright after inversion
            Rgb48 darkest = FindDarkestPixel(image);
            Rgb48 brightest = FindBrightestPixel(image);
            Parallel.For(0, image.Height, y =>
            {
                Span<Rgb48> pixelRowSpan = image.GetPixelRowSpan(y);
                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = pixelRowSpan[x];

                    /* Set levels
                     * ChannelValue = 65 535 * ( ( ChannelValue - BrightestValue ) /  ( DarkestValue - BrightestValue ) )
                     * Again, please note that Dark/Bright is depending on if the image is a positive or negative
                     * and here I am assuming we are looking at a negative image pre-inversion
                     */                    
                    pixel = new Rgb48(  (ushort)Math.Round(65535 * (((double)pixel.R - brightest.R) / (darkest.R - brightest.R)), 0),
                                        (ushort)Math.Round(65535 * (((double)pixel.G - brightest.G) / (darkest.G - brightest.G)), 0),
                                        (ushort)Math.Round(65535 * (((double)pixel.B - brightest.B) / (darkest.B - brightest.B)), 0));
                                        
                    pixelRowSpan[x] = pixel;
                }
            });
        }

        private static object _locker = new object();

        private static Rgb48 FindDarkestPixel(Image<Rgb48> image)
        {
            ushort darkestR = 0;
            ushort darkestG = 0;
            ushort darkestB = 0;
            Parallel.For(0, image.Height, y =>
            {
                Span<Rgb48> pixelRowSpan = image.GetPixelRowSpan(y);
                for (int x = 0; x < image.Width; x++)
                {
                    if (pixelRowSpan[x].R > darkestR)
                    {
                        lock (_locker)
                        {
                            if (pixelRowSpan[x].R > darkestR)
                            {
                                darkestR = pixelRowSpan[x].R;
                            }
                        }
                    }

                    if (pixelRowSpan[x].G > darkestG)
                    {
                        lock (_locker)
                        {
                            if (pixelRowSpan[x].G > darkestG)
                            {
                                darkestG = pixelRowSpan[x].G;
                            }
                        }
                    }

                    if (pixelRowSpan[x].B > darkestB)
                    {
                        lock (_locker)
                        {
                            if (pixelRowSpan[x].B > darkestB)
                            {
                                darkestB = pixelRowSpan[x].B;
                            }
                        }
                    }
                }
            });
            return new Rgb48(darkestR, darkestG, darkestB);
        }

        private static Rgb48 FindBrightestPixel(Image<Rgb48> image)
        {
            ushort brightestR = 65_535;
            ushort brightestG = 65_535;
            ushort brightestB = 65_535;
            Parallel.For(0, image.Height, y =>
            {
                Span<Rgb48> pixelRowSpan = image.GetPixelRowSpan(y);
                for (int x = 0; x < image.Width; x++)
                {
                    if (pixelRowSpan[x].R < brightestR)
                    {
                        lock (_locker)
                        {
                            if (pixelRowSpan[x].R < brightestR)
                            {
                                brightestR = pixelRowSpan[x].R;
                            }
                        }
                    }

                    if (pixelRowSpan[x].G < brightestG)
                    {
                        lock (_locker)
                        {
                            if (pixelRowSpan[x].G < brightestG)
                            {
                                brightestG = pixelRowSpan[x].G;
                            }
                        }
                    }

                    if (pixelRowSpan[x].B < brightestB)
                    {
                        lock (_locker)
                        {
                            if (pixelRowSpan[x].B < brightestB)
                            {
                                brightestB = pixelRowSpan[x].B;
                            }
                        }
                    }
                }
            });
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
                gammaLabel.Content = "Gamma conversion: " + String.Format("{0:0.00}", 1 / e.NewValue);
        }
        
    }
}
