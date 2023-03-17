using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using PakonRawFileLib;

namespace PakonImageConverter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private double _gamma = 0.4545454545454545;
        private float _contrast = 1.08f;
        private float _saturation = 1.08f;
        private bool _isBwImage { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            BwNegativeCheckbox.DataContext = this;
            DataContext = this;
            imageFormat.ItemsSource = Enum.GetValues(typeof(ImageFormats)).Cast<ImageFormats>();
            imageFormat.SelectedIndex = 0;
            SetValuesFromSettings();
        }

        private void SetValuesFromSettings()
        {
            _isBwImage = WindowsClient.Properties.Settings.Default.BwImage;
            BwNegativeCheckbox.IsChecked = _isBwImage;
            _contrast = WindowsClient.Properties.Settings.Default.Contrast;
            contrastSlider.Value = _contrast / 2;
            contrastLabel.Content = "Contrast: " + String.Format("{0:0}%", _contrast * 100);
            _saturation = WindowsClient.Properties.Settings.Default.Saturation;
            saturationSlider.Value = _saturation / 2;
            saturationLabel.Content = "Saturation: " + String.Format("{0:0}%", _saturation * 100);
            _gamma = WindowsClient.Properties.Settings.Default.Gamma;
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
                    ImageFormats format = ImageFormats.PNG16;
                    Application.Current.Dispatcher.Invoke(() => format = (ImageFormats)imageFormat.SelectedItem);

                    // TODO: we are allocating a ton of buffers now, what happens on a low mem machine?
                    Parallel.ForEach(files, filename =>
                    {
                        Image<Rgb48> image = ProcessImage(filename, format);

                        var preview = image.ToArray(new BmpEncoder());
                        Application.Current.Dispatcher.Invoke(() => imageBox.Source = preview.ToBitmap().ToBitmapSource());
                        Application.Current.Dispatcher.Invoke(() => LoadingProgress.Value++);
                    });
                    SystemSounds.Beep.Play();
                });
            }
        }

        private Image<Rgb48> ProcessImage(string filename, ImageFormats format)
        {
            StreamReader ms = new StreamReader(filename);

            // TODO: What if there is no header saved?
            var header = new byte[16];

            ms.BaseStream.Read(header, 0, 16);
            int width = (int)BitConverter.ToUInt32(header, 4);
            int height = (int)BitConverter.ToUInt32(header, 8);

            // The file is in planar mode so RRRRRGGGGGBBBB

            byte[] buffer = new byte[width * height * 6];
            // But imagesharp wants it in interleaved mode so RGBRGBRGBRGB
            byte[] interleaved = new byte[width * height * 6];

            ms.BaseStream.Read(buffer, 0, width * height * 6);

            Application.Current.Dispatcher.Invoke(() => LoadingProgress.Value++);

            InterleaveBuffer(width, height, buffer, interleaved);
            var image = Image.LoadPixelData<Rgb48>(interleaved, width, height);
            (Rgb48, Rgb48) darkestAndBrightest = image.SetWhiteAndBlackpoint(_isBwImage);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var brightest = _isBwImage ? darkestAndBrightest.Item1 : darkestAndBrightest.Item2;
                var darkest = _isBwImage ? darkestAndBrightest.Item2 : darkestAndBrightest.Item1;

                darkestImageInfo.Content = $"Darkest R: {darkest.R} \r\n";
                darkestImageInfo.Content += $"Darkest G: {darkest.G} \r\n";
                darkestImageInfo.Content += $"Darkest B: {darkest.B} \r\n";
                brightestImageInfo.Content = $"Brightest R: {brightest.R} \r\n";
                brightestImageInfo.Content += $"Brightest G: {brightest.G} \r\n";
                brightestImageInfo.Content += $"Brightest B: {brightest.B} \r\n";
            });

            GammaCorrection(image);

            // TODO: folder setting
            // TODO: preview before save

            if (_isBwImage)
            {
                image.Mutate(x => x.Invert()); // We probably want separate adjustments for bw raws
                image.Mutate(x => x.Saturate(0f));                         
                image.Save(filename.Replace(".raw", ".png"), new PngEncoder() { ColorType = PngColorType.Grayscale, BitDepth = PngBitDepth.Bit16 });
            }
            else
            {
                image.Mutate(x => x.Contrast(_contrast));
                image.Mutate(x => x.Saturate(_saturation)); 


                switch (format)
                {
                    case ImageFormats.PNG16:
                        image.Save(filename.Replace(".raw", ".png"), new PngEncoder() { BitDepth = PngBitDepth.Bit16 });
                        break;
                    case ImageFormats.JPG:
                        image.Save(filename.Replace(".raw", ".jpg"), new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
                        break;
                    case ImageFormats.TIFF8:
                        image.Save(filename.Replace(".raw", ".tiff"), new TiffEncoder { BitsPerPixel = TiffBitsPerPixel.Bit16 });
                        break;
                    default:
                        break;
                }
            }

            return image;
        }

        private void GammaCorrection(Image<Rgb48> image)
        {
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgb48> row = accessor.GetRowSpan(y);
                    foreach (ref Rgb48 pixel in row)
                    {
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
                    }
                }
            });
        }

        private static void InterleaveBuffer(int width, int height, byte[] buffer, byte[] interleaved)
        {
            int pixelSize = 6;

            // Interleave the buffer
            for (int i = 0; i != width * height * 2; i += 2)
            {
                // R
                interleaved[i / 2 * pixelSize + 0] = buffer[i];
                interleaved[i / 2 * pixelSize + 1] = buffer[i + 1];

                // G - we got to jump over all R bytes first
                interleaved[i / 2 * pixelSize + 2] = buffer[(2 * width * height) + i];
                interleaved[i / 2 * pixelSize + 3] = buffer[(2 * width * height) + i + 1];

                // B - we got to jump over all G bytes first
                interleaved[i / 2 * pixelSize + 4] = buffer[(2 * 2 * width * height) + i];
                interleaved[i / 2 * pixelSize + 5] = buffer[(2 * 2 * width * height) + i + 1];
            }
        }

        private void GammaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _gamma = e.NewValue;
            if (gammaLabel != null)
                gammaLabel.Content = "Gamma: " + String.Format("{0:0.00}", 1 / e.NewValue);
        }

        private void ContrastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _contrast = (float)e.NewValue * 2;
            if (contrastLabel != null)
                contrastLabel.Content = "Contrast: " + String.Format("{0:0}%", e.NewValue * 2 * 100);
        }

        private void SaturationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _saturation = (float)(e.NewValue * 2);
            if (saturationLabel != null)
                saturationLabel.Content = "Saturation: " + String.Format("{0:0}%", e.NewValue * 2 * 100);
        }

        private void checkBox_Click(object sender, RoutedEventArgs e)
        {
            saturationSlider.IsEnabled = !_isBwImage;
            if (_isBwImage)
            {
                if (saturationLabel != null)
                    saturationLabel.Content = "Saturation: -";
            }
            else
                saturationLabel.Content = "Saturation: " + String.Format("{0:0}%", _saturation * 100);

        }

        private void Window_Closed(object sender, EventArgs e)
        {
            WindowsClient.Properties.Settings.Default.BwImage = _isBwImage;
            WindowsClient.Properties.Settings.Default.Contrast = _contrast;
            WindowsClient.Properties.Settings.Default.Saturation = _saturation;
            WindowsClient.Properties.Settings.Default.Gamma = (float)_gamma;
            WindowsClient.Properties.Settings.Default.Save();
        }
    }
}
