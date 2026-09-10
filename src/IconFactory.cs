using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Tsuru
{
    /// <summary>
    /// Draws the mouse glyph at runtime so the app ships as a single file with
    /// no image resources.
    ///
    /// The body is a solid silhouette with the scroll wheel punched clean
    /// through it: at 16px - the size that actually lands in the tray - a
    /// filled shape stays legible where an outline turns to mush.
    /// </summary>
    internal static class IconFactory
    {
        /// <summary>Used for the executable's own icon, which has to read on any background.</summary>
        private static readonly Color BrandInk = Color.FromArgb(59, 130, 246);

        /// <summary>Name given to the embedded logo by the build script.</summary>
        private const string LogoResource = "Tsuru.logo.png";

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static Image _logo;
        private static bool _logoChecked;

        /// <summary>
        /// The Tsuru wordmark, embedded in the executable so a published build
        /// needs no loose files. Falls back to a copy beside the exe, and then
        /// to the drawn glyph, so the app always has an icon of some sort.
        /// </summary>
        private static Image Logo()
        {
            if (_logoChecked) return _logo;
            _logoChecked = true;

            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LogoResource))
                {
                    if (stream != null)
                    {
                        // Copy out: an Image built on a stream needs that stream
                        // to stay open for its whole life.
                        using (Image embedded = Image.FromStream(stream))
                            _logo = new Bitmap(embedded);
                    }
                }

                if (_logo == null)
                {
                    string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ico.png");
                    if (File.Exists(beside))
                        using (Image loose = Image.FromFile(beside))
                            _logo = new Bitmap(loose);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Logo unavailable, falling back to the drawn glyph: " + ex.Message);
            }

            return _logo;
        }

        /// <summary>Greyscale plus reduced alpha, for the paused state.</summary>
        private static readonly ColorMatrix PausedMatrix = new ColorMatrix(new float[][]
        {
            new float[] { 0.30f, 0.30f, 0.30f, 0,     0 },
            new float[] { 0.59f, 0.59f, 0.59f, 0,     0 },
            new float[] { 0.11f, 0.11f, 0.11f, 0,     0 },
            new float[] { 0,     0,     0,     0.60f, 0 },
            new float[] { 0,     0,     0,     0,     1 }
        });

        /// <summary>Tray icon, inked to contrast with the current taskbar theme.</summary>
        public static Icon Create(bool active, int size)
        {
            return Create(active, size, SystemUsesLightTheme());
        }

        /// <summary>Tray icon for an explicit theme. Separate so both can be rendered for inspection.</summary>
        public static Icon Create(bool active, int size, bool lightTheme)
        {
            if (Logo() != null)
            {
                using (Bitmap bmp = RenderLogo(size, active))
                    return FromBitmap(bmp);
            }

            Color ink = lightTheme
                ? Color.FromArgb(32, 32, 32)
                : Color.FromArgb(255, 255, 255);

            if (!active) ink = Blend(ink, Color.Gray, 0.55f);

            return Render(size, ink, !active);
        }

        /// <summary>Paints the wordmark to a square bitmap at the requested size.</summary>
        private static Bitmap RenderLogo(int size, bool active)
        {
            if (size < 16) size = 16;
            Image logo = Logo();

            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle box = new Rectangle(0, 0, size, size);

                if (active)
                {
                    g.DrawImage(logo, box);
                }
                else
                {
                    using (ImageAttributes attributes = new ImageAttributes())
                    {
                        attributes.SetColorMatrix(PausedMatrix);
                        g.DrawImage(logo, box, 0, 0, logo.Width, logo.Height,
                                    GraphicsUnit.Pixel, attributes);
                    }

                    using (Pen slash = new Pen(Color.FromArgb(226, 74, 74), Math.Max(1.6f, size * 0.10f)))
                    {
                        slash.StartCap = LineCap.Round;
                        slash.EndCap = LineCap.Round;
                        float m = size * 0.14f;
                        g.DrawLine(slash, m, size - m, size - m, m);
                    }
                }
            }
            return bmp;
        }

        /// <summary>
        /// True when the taskbar and system chrome are light. Windows does not
        /// recolour notification icons, so a fixed white glyph would vanish.
        /// </summary>
        public static bool SystemUsesLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return false;
                    object value = key.GetValue("SystemUsesLightTheme");
                    if (value is int) return (int)value != 0;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Theme probe failed: " + ex.Message);
            }
            return false;
        }

        private static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static Icon Render(int size, Color ink, bool struck)
        {
            if (size < 16) size = 16;
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    Draw(g, size, ink, struck);
                return FromBitmap(bmp);
            }
        }

        private static void Draw(Graphics g, int size, Color ink, bool struck)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Mouse body: a filled capsule, generous enough to stay readable small.
            float w = size * 0.58f;
            float h = size * 0.80f;
            float x = (size - w) / 2f;
            float y = (size - h) / 2f;
            float r = w / 2f;

            using (GraphicsPath body = new GraphicsPath())
            {
                body.AddArc(x, y, w, r * 2, 180, 180);
                body.AddArc(x, y + h - r * 2, w, r * 2, 0, 180);
                body.CloseFigure();

                using (Brush fill = new SolidBrush(ink))
                    g.FillPath(fill, body);
            }

            // Punch the wheel slot straight through the silhouette so it reads
            // against whatever the taskbar is painted with.
            CompositingMode previous = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (Pen slot = new Pen(Color.Transparent, Math.Max(1.4f, w * 0.17f)))
            {
                slot.StartCap = LineCap.Round;
                slot.EndCap = LineCap.Round;
                float cx = size / 2f;
                g.DrawLine(slot, cx, y + h * 0.17f, cx, y + h * 0.37f);
            }
            g.CompositingMode = previous;

            if (struck)
            {
                using (Pen slash = new Pen(Color.FromArgb(226, 74, 74), Math.Max(1.6f, size * 0.10f)))
                {
                    slash.StartCap = LineCap.Round;
                    slash.EndCap = LineCap.Round;
                    float m = size * 0.14f;
                    g.DrawLine(slash, m, size - m, size - m, m);
                }
            }
        }

        /// <summary>
        /// Writes the multi-resolution .ico used as the executable's icon.
        /// Entries are PNG-encoded, which Windows has accepted since Vista, so
        /// the file stays small and the build needs no external art.
        /// </summary>
        public static void SaveIco(string path, int[] sizes)
        {
            bool haveLogo = Logo() != null;

            List<byte[]> images = new List<byte[]>();
            foreach (int size in sizes)
            {
                Bitmap bmp = haveLogo ? RenderLogo(size, true) : new Bitmap(size, size);
                try
                {
                    if (!haveLogo)
                        using (Graphics g = Graphics.FromImage(bmp))
                            Draw(g, size, BrandInk, false);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        images.Add(ms.ToArray());
                    }
                }
                finally
                {
                    bmp.Dispose();
                }
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0);              // reserved
                w.Write((short)1);              // type: icon
                w.Write((short)images.Count);

                int offset = 6 + 16 * images.Count;
                for (int i = 0; i < images.Count; i++)
                {
                    int size = sizes[i];
                    w.Write((byte)(size >= 256 ? 0 : size));   // 0 encodes 256
                    w.Write((byte)(size >= 256 ? 0 : size));
                    w.Write((byte)0);           // palette size
                    w.Write((byte)0);           // reserved
                    w.Write((short)1);          // colour planes
                    w.Write((short)32);         // bits per pixel
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }

                foreach (byte[] data in images) w.Write(data);
            }
        }

        private static Icon FromBitmap(Bitmap bmp)
        {
            IntPtr handle = bmp.GetHicon();
            try
            {
                using (Icon temp = Icon.FromHandle(handle))
                {
                    // Clone so the managed icon survives DestroyIcon below.
                    return (Icon)temp.Clone();
                }
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
    }
}
