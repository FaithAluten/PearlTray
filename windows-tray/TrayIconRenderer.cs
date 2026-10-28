using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;

namespace PearlTray
{
    public static class TrayIconRenderer
    {
        public static bool IsLightTaskbar()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key != null && Convert.ToInt32(key.GetValue("SystemUsesLightTheme", 0)) == 1;
            }
            catch { return false; }
        }

        public static Color Foreground(bool warning, bool light)
        {
            return warning ? (light ? Color.FromArgb(151, 83, 0) : Color.FromArgb(255, 199, 69))
                : (light ? Color.FromArgb(22, 24, 28) : Color.FromArgb(250, 250, 250));
        }

        public static Bitmap Render(string text, int size, Color color)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var path = new GraphicsPath())
            using (var family = new FontFamily("Arial"))
            using (var brush = new SolidBrush(color))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                path.AddString(text, family, (int)FontStyle.Bold, size,
                    PointF.Empty, StringFormat.GenericTypographic);
                RectangleF bounds = path.GetBounds();
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    // Keep the 1.1 text height and compress only the width to fit the tray.
                    float scaleY = size * 0.74f / bounds.Height;
                    float scaleX = Math.Min(scaleY, (size - 1f) / bounds.Width);
                    using (var transform = new Matrix())
                    {
                        transform.Translate(-bounds.X, -bounds.Y, MatrixOrder.Append);
                        transform.Scale(scaleX, scaleY, MatrixOrder.Append);
                        transform.Translate((size - bounds.Width * scaleX) / 2f,
                            (size - bounds.Height * scaleY) / 2f, MatrixOrder.Append);
                        path.Transform(transform);
                    }
                    graphics.FillPath(brush, path);
                }
            }
            return bitmap;
        }

        public static string Compact(string fullValue)
        {
            if (fullValue.Length <= 5) return fullValue;
            decimal value = Decimal.Parse(fullValue, System.Globalization.CultureInfo.InvariantCulture);
            if (value > 0 && value < 0.01m)
            {
                string scientific = value.ToString("0E+0",
                    System.Globalization.CultureInfo.InvariantCulture).Replace("E+", "e").Replace("E-", "e-");
                return scientific;
            }
            decimal divisor = value >= 1000000000m ? 1000000000m :
                value >= 1000000m ? 1000000m : value >= 1000m ? 1000m : 1m;
            string suffix = divisor == 1000000000m ? "B" :
                divisor == 1000000m ? "M" : divisor == 1000m ? "K" : "";
            return (value / divisor).ToString("0.##",
                System.Globalization.CultureInfo.InvariantCulture) + suffix;
        }

        public static string Compact(string fullValue, int decimalPlaces)
        {
            if (decimalPlaces < 0 || decimalPlaces > 2)
                throw new ArgumentOutOfRangeException("decimalPlaces");
            decimal value = Decimal.Parse(fullValue, System.Globalization.CultureInfo.InvariantCulture);
            decimal divisor = value >= 1000000000m ? 1000000000m :
                value >= 1000000m ? 1000000m : value >= 1000m ? 1000m : 1m;
            string suffix = divisor == 1000000000m ? "B" :
                divisor == 1000000m ? "M" : divisor == 1000m ? "K" : "";
            return Math.Round(value / divisor, decimalPlaces, MidpointRounding.AwayFromZero)
                .ToString("F" + decimalPlaces, System.Globalization.CultureInfo.InvariantCulture) + suffix;
        }

        public static Icon Create(string text, bool warning)
        {
            return Create(text, Foreground(warning, IsLightTaskbar()));
        }

        public static Icon Create(string text, Color color)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64 };
            var images = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (var bitmap = Render(text, sizes[i], color))
                using (var png = new MemoryStream())
                {
                    bitmap.Save(png, ImageFormat.Png);
                    images[i] = png.ToArray();
                }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((short)0);
                writer.Write((short)1);
                writer.Write((short)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)sizes[i]);
                    writer.Write((byte)sizes[i]);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((short)1);
                    writer.Write((short)32);
                    writer.Write(images[i].Length);
                    writer.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] image in images) writer.Write(image);
                stream.Position = 0;
                using (var icon = new Icon(stream))
                    return (Icon)icon.Clone();
            }
        }
    }
}
