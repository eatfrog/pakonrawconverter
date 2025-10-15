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
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;

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
        private class ProcessedImage
        {
            public string Filename { get; set; }
            public byte[] PixelData { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        private readonly System.Collections.Generic.List<ProcessedImage> _processedImages = new System.Collections.Generic.List<ProcessedImage>();
        private int _currentImageIndex = -1;


        public MainWindow()
        {
            InitializeComponent();
            BwNegativeCheckbox.DataContext = this;           
            DataContext = this;
            imageFormat.ItemsSource = Enum.GetValues(typeof(ImageFormats)).Cast<ImageFormats>();
            imageFormat.SelectedIndex = 0;
            SetValuesFromSettings();
            PreviousButton.IsEnabled = false;
            NextButton.IsEnabled = false;
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

        private void UpdateDisplayedImage()
        {
            if (!_processedImages.Any() || _currentImageIndex < 0 || _currentImageIndex >= _processedImages.Count)
            { 
                imageBox.Source = null;
                imageFilenameLabel.Content = string.Empty;
                return;
            }

            var currentImage = _processedImages[_currentImageIndex];
            var stride = currentImage.Width * 6;
            var bitmapSource = BitmapSource.Create(currentImage.Width, currentImage.Height, 96, 96, PixelFormats.Rgb48, null, currentImage.PixelData, stride);
            imageBox.Source = bitmapSource;
            imageFilenameLabel.Content = System.IO.Path.GetFileName(currentImage.Filename);
            UpdateHistograms(bitmapSource.ToImage<Rgb48>());
            UpdateNavigationButtonsState();
        }

        public void NextImage()
        {
            if (!_processedImages.Any() || _currentImageIndex >= _processedImages.Count - 1)
                return;
            _currentImageIndex++;
            UpdateDisplayedImage();
        }
        
        public void PreviousImage()
        { 
            if (!_processedImages.Any() || _currentImageIndex <= 0)
                return;
            _currentImageIndex--;
            UpdateDisplayedImage();
        }
        
        private void UpdateNavigationButtonsState()
        {
            PreviousButton.IsEnabled = _currentImageIndex > 0;
            NextButton.IsEnabled = _currentImageIndex < _processedImages.Count - 1;
        }

        private void ImagePanel_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = ((string[])e.Data.GetData(DataFormats.FileDrop)).OrderBy(f => f).ToArray();
                _processedImages.Clear();


                LoadingProgress.Maximum = files.Count() * 2;
                LoadingProgress.Value = 0;
                Task.Run(() =>
                {
                    ImageFormats format = ImageFormats.PNG16;
                    Application.Current.Dispatcher.Invoke(() => format = (ImageFormats)imageFormat.SelectedItem);

                    var processedImagesBag = new System.Collections.Concurrent.ConcurrentBag<ProcessedImage>();
                    Parallel.ForEach(files, filename =>
                    {
                        Application.Current.Dispatcher.Invoke(() => LoadingProgress.Value++);

                        var processor = new PakonRawProcessor();
                        Image<Rgb48> image = processor.ProcessImage(filename, _isBwImage, _gamma, _contrast, _saturation);

                        var pixelBuffer = new byte[image.Width * image.Height * 6];
                        image.CopyPixelDataTo(pixelBuffer);
                        processedImagesBag.Add(new ProcessedImage { Filename = filename, PixelData = pixelBuffer, Width = image.Width, Height = image.Height });

                        Application.Current.Dispatcher.Invoke(() => LoadingProgress.Value++);
                    });
                    _processedImages.AddRange(processedImagesBag.OrderBy(p => p.Filename));

                    if (_processedImages.Any())
                    {
                        _currentImageIndex = 0;
                        Application.Current.Dispatcher.Invoke(UpdateDisplayedImage);
                    }

                    SystemSounds.Beep.Play();
                });
            }
        }

        private void SaveImage(ImageFormats format, string filename, Image<Rgb48> image)
        {
            if (_isBwImage)
            {
                image.Save(filename.Replace(".raw", ".png"), new PngEncoder() { ColorType = PngColorType.Grayscale, BitDepth = PngBitDepth.Bit16 });
            }
            else
            {
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
        }


        private void UpdateHistograms(Image<Rgb48> image)
        {
            if (_isBwImage)
            {
                var histogram = CalculateHistogram(image, true);
                DrawHistogram(redHistogram, histogram[0], Colors.Gray);
            }
            else
            {
                var histograms = CalculateHistogram(image, false);
                DrawHistogram(redHistogram, histograms[0], Colors.DarkRed);
                DrawHistogram(greenHistogram, histograms[1], Colors.DarkGreen);
                DrawHistogram(blueHistogram, histograms[2], Colors.DarkBlue);
            }
        }

        private int[][] CalculateHistogram(Image<Rgb48> image, bool isGrayScale)
        {
            var histograms = new int[isGrayScale ? 1 : 3][];
            for (int i = 0; i < histograms.Length; i++)
            {
                histograms[i] = new int[256];
            }

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgb48> row = accessor.GetRowSpan(y);
                    foreach (ref Rgb48 pixel in row)
                    {
                        if (isGrayScale)
                        {
                            histograms[0][(pixel.R + pixel.G + pixel.B) / 3 / 256]++;
                        }
                        else
                        {
                            histograms[0][pixel.R / 256]++;
                            histograms[1][pixel.G / 256]++;
                            histograms[2][pixel.B / 256]++;
                        }
                    }
                }
            });

            return histograms;
        }

        private void DrawHistogram(Canvas canvas, int[] histogram, System.Windows.Media.Color color)
        {
            canvas.Children.Clear();
            int max = histogram.Max();

            for (int i = 0; i < 256; i++)
            {
                var bar = new System.Windows.Shapes.Rectangle();
                bar.Width = 1;
                bar.Height = (double)histogram[i] / max * canvas.Height;
                bar.Fill = new System.Windows.Media.SolidColorBrush(color);
                Canvas.SetLeft(bar, i);
                Canvas.SetBottom(bar, 0);
                canvas.Children.Add(bar);
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
            var newVal = ((CheckBox)sender).IsChecked;
            if (newVal.HasValue)
                _isBwImage = newVal.Value;
            saturationSlider.IsEnabled = !_isBwImage;
            if (_isBwImage)
            {
                if (saturationLabel != null)
                    saturationLabel.Content = "Saturation: -";

                redLabel.Content = "Gray";
                greenLabel.Visibility = Visibility.Collapsed;
                blueLabel.Visibility = Visibility.Collapsed;
                greenHistogram.Visibility = Visibility.Collapsed;
                blueHistogram.Visibility = Visibility.Collapsed;
            }
            else
            {
                saturationLabel.Content = "Saturation: " + String.Format("{0:0}%", _saturation * 100);
                redLabel.Content = "Red";
                greenLabel.Visibility = Visibility.Visible;
                blueLabel.Visibility = Visibility.Visible;
                greenHistogram.Visibility = Visibility.Visible;
                blueHistogram.Visibility = Visibility.Visible;
            }

            if (imageBox.Source != null)
            {
                var bmp = (BitmapSource)imageBox.Source;
                var image = bmp.ToImage<Rgb48>();
                UpdateHistograms(image);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            WindowsClient.Properties.Settings.Default.BwImage = _isBwImage;
            WindowsClient.Properties.Settings.Default.Contrast = _contrast;
            WindowsClient.Properties.Settings.Default.Saturation = _saturation;
            WindowsClient.Properties.Settings.Default.Gamma = (float)_gamma;
            WindowsClient.Properties.Settings.Default.Save();
        }

        private void NextImage_Click(object sender, RoutedEventArgs e)
        {
            NextImage();
        }

        private void PreviousImage_Click(object sender, RoutedEventArgs e)
        { 
            PreviousImage();
        }
    }
}
