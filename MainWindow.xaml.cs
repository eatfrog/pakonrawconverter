using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
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
                    //ms.BaseStream.Read(new byte[2], 0, 2);
                    ms.BaseStream.Read(buffer, 2, (3000 * 2000 * 6) - 2);

                    int pixelSize = 6;

                    // Interleave the buffer
                    for (int i = 0; i != 3000 * 2000 * 2; i += 2)
                    {
                        // R
                        interleaved[i / 2 * pixelSize + 4] = buffer[i];
                        interleaved[i / 2* pixelSize + 5] = buffer[i + 1];


                        // G - we got to jump over all R bytes first
                        interleaved[i / 2 * pixelSize + 2] = buffer[(2 * 3000 * 2000) + i];
                        interleaved[i / 2 * pixelSize + 3] = buffer[(2 * 3000 * 2000) + i + 1];
                         

                        // B - we got to jump over all G bytes first
                        interleaved[i / 2 * pixelSize + 0] = buffer[(2 * 2 * 3000 * 2000) + i];
                        interleaved[i / 2 * pixelSize + 1] = buffer[(2 * 2 * 3000 * 2000) + i + 1];
                    }

                    var bmp = new Bitmap(3000, 2000, PixelFormat.Format48bppRgb);
                    Rectangle dimension = new Rectangle(0, 0, bmp.Width, bmp.Height);
                    BitmapData picData = bmp.LockBits(dimension, ImageLockMode.ReadWrite, bmp.PixelFormat);
                    IntPtr pixelStartAddress = picData.Scan0;

                    //Copy the pixel data into the bitmap structure
                    System.Runtime.InteropServices.Marshal.Copy(interleaved, 0, pixelStartAddress, interleaved.Length);
                    bmp.UnlockBits(picData);
                    bmp.Save("bitmaptest.jpg", ImageFormat.Jpeg);

                }
            }

        }
    }
}
