using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Tsuru
{
    /// <summary>
    /// First-run screen. Smoothing starts paused so this window has something
    /// to hand over: pressing Start switches it on and opens Settings.
    ///
    /// Drawn entirely in OnPaint rather than assembled from controls, because
    /// the design needs offset colour shadows, overlapping sparkles and exact
    /// proportional placement - none of which WinForms controls do well.
    /// Positions are fractions of the canvas, taken from the source frame, so
    /// the whole screen scales as one piece.
    /// </summary>
    public class WelcomeForm : Form
    {
        private readonly Action _onStart;
        private readonly Action _onSettings;

        /// <summary>
        /// Drives the call to action. With smoothing off the button starts it;
        /// with smoothing already running there is nothing to start, so it
        /// becomes the way through to Settings.
        /// </summary>
        private bool _smoothingActive;

        /// <summary>True when the user pressed the button rather than dismissing the window.</summary>
        public bool Started { get; private set; }

        public WelcomeForm(bool smoothingActive, Action onStart, Action onSettings)
        {
            _smoothingActive = smoothingActive;
            _onStart = onStart;
            _onSettings = onSettings;
            BuildForm();
        }

        /// <summary>Re-reads the state when the window is reopened or smoothing was toggled elsewhere.</summary>
        public void Reflect(bool smoothingActive)
        {
            Started = false;
            if (_smoothingActive == smoothingActive) return;

            _smoothingActive = smoothingActive;
            if (_buttonFont != null) { _buttonFont.Dispose(); _buttonFont = null; }
            Invalidate();
        }

        private string ButtonLabel
        {
            get { return _smoothingActive ? SettingsButtonText : StartButtonText; }
        }

        // ------------------------------------------------------------ behaviour
        private void BuildForm()
        {
            Text = "Welcome to Tsuru";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(CanvasWidth, CanvasHeight);
            BackColor = Canvas;
            ShowInTaskbar = true;
            KeyPreview = true;
            Icon = IconFactory.Create(true, 32);

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Commit();
            };
        }

        private void Commit()
        {
            if (Started) return;
            Started = true;

            Action act = _smoothingActive ? _onSettings : _onStart;
            if (act != null) act();
            Close();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedCorners();
        }

        // ----------------------------------------------------------- chrome
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DwmWindowCornerPreference = 33;
        private const int DwmCornerRound = 2;

        /// <summary>
        /// Windows 11 rounds the window itself, which antialiases properly.
        /// Older versions get a clipped region instead - harder edges, but the
        /// silhouette is right.
        /// </summary>
        private void ApplyRoundedCorners()
        {
            try
            {
                int preference = DwmCornerRound;
                if (DwmSetWindowAttribute(Handle, DwmWindowCornerPreference,
                                          ref preference, sizeof(int)) == 0)
                    return;
            }
            catch (Exception ex)
            {
                Log.Write("DWM corner rounding unavailable: " + ex.Message);
            }

            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius))
                Region = new Region(path);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 0x0002;

        // ---------------------------------------------------------- interaction
        private bool _hoverStart;
        private bool _pressStart;
        private bool _hoverClose;

        private Rectangle StartFace
        {
            get
            {
                return new Rectangle(
                    R(ButtonLeft, CanvasWidth), R(ButtonTop, CanvasHeight),
                    R(ButtonWidth, CanvasWidth), R(ButtonHeight, CanvasHeight));
            }
        }

        private Rectangle CloseBox
        {
            get { return new Rectangle(CanvasWidth - 40, 14, 26, 26); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // Fed before the early-out below: the dots and the collage follow
            // the pointer continuously, not only when a button state flips.
            Field().SetCursor(e.Location, true);
            if (Art() != null) Art().SetCursor(e.Location, true);
            Animate();

            bool overStart = StartFace.Contains(e.Location);
            bool overClose = CloseBox.Contains(e.Location);
            if (overStart == _hoverStart && overClose == _hoverClose) return;

            _hoverStart = overStart;
            _hoverClose = overClose;
            Cursor = (overStart || overClose) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (StartFace.Contains(e.Location))
            {
                _pressStart = true;
                Invalidate();
                return;
            }

            if (CloseBox.Contains(e.Location)) return;

            // Borderless: dragging the background moves the window.
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            bool wasPressed = _pressStart;
            _pressStart = false;

            if (CloseBox.Contains(e.Location)) { Close(); return; }
            if (wasPressed && StartFace.Contains(e.Location)) { Commit(); return; }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverStart = _hoverClose = _pressStart = false;

            Field().SetCursor(PointF.Empty, false);
            if (Art() != null) Art().SetCursor(PointF.Empty, false);
            Animate();

            Invalidate();
        }

        // ---------------------------------------------------------- appearance
        // Proportions and palette transcribed from the Tsuru design frame.

        private const int CanvasWidth = 560;
        private const int CanvasHeight = 643;
        private const int CornerRadius = 18;

        // Colours marked "API" came from the design file; the rest are measured
        // from the reference export and want confirming when the API frees up.
        private static readonly Color Canvas = Color.FromArgb(0x0D, 0x0D, 0x0D);
        private static readonly Color DotGrid = Color.FromArgb(0x23, 0x23, 0x23);
        private static readonly Color Paper = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color TitleShadow = Color.FromArgb(0x7E, 0x79, 0xFF);   // API
        private static readonly Color ButtonShadow = Color.FromArgb(0x7E, 0x79, 0xFF);  // matches the title
        private static readonly Color ButtonInk = Color.FromArgb(0x0D, 0x0D, 0x0D);
        private static readonly Color Dim = Color.FromArgb(0x6A, 0x6A, 0x6A);

        private static readonly Color SparklePeach = Color.FromArgb(0xFF, 0xC5, 0xAF);
        private static readonly Color SparklePink = Color.FromArgb(0xFF, 0xAF, 0xDB);
        private static readonly Color SparkleMint = Color.FromArgb(0xAF, 0xFF, 0xD1);

        private const string TitleText = "Tsuru";
        private const string SubtitleText = "scroll, super, smooth";
        private const string StartButtonText = "Start it up";
        private const string SettingsButtonText = "Go to settings";

        // Fractions of the canvas.
        private const float TitleCentreY = 0.250f;
        // Text layer is 1109 wide in a 1400 frame.
        private const float TitleWidth = 0.792f;
        // The design offsets the colour copy by an equal 12px in a 1400x1607
        // frame, so the two axes are not the same fraction.
        private const float TitleShadowDx = 12f / 1400f;
        private const float TitleShadowDy = 12f / 1607f;

        private const float SubtitleCentreY = 0.394f;
        private const float SubtitleWidth = 0.268f;
        /// <summary>Design tracking of -5.3 at 66.5pt, as a fraction of the em.</summary>
        private const float SubtitleTracking = -5.3f / 66.5f;

        private const float ButtonLeft = 0.313f;
        private const float ButtonTop = 0.5825f;
        private const float ButtonWidth = 0.376f;
        private const float ButtonHeight = 0.095f;
        private const float ButtonTextWidth = 0.336f;   // of the button face
        private const float ShadowOffset = 0.009f;

        // Background dot grid, as fractions of the canvas width.
        private const float DotSpacing = 0.0253f;
        private const float DotDiameter = 0.0040f;

        private static int R(float fraction, int extent)
        {
            return (int)Math.Round(fraction * extent);
        }

        private Font _titleFont;
        private Font _subtitleFont;
        private Font _buttonFont;

        // Painted extents of each string, so text is placed by what is actually
        // inked rather than by the font's line box.
        private RectangleF _titleInk;
        private RectangleF _subtitleInk;
        private RectangleF _buttonInk;
        private float _subtitleTrackingPx;

        private Image _titleArt;
        private bool _titleArtChecked;

        private DotField _dots;
        private Collage _collage;
        private Timer _anim;

        /// <summary>
        /// Wordmark, sparkles and subtitle, rendered once. They never change,
        /// and re-running the type fitting and DrawString for each of them on
        /// every animation frame would dominate the frame time.
        /// </summary>
        private Bitmap _foreground;

        /// <summary>
        /// Fixed rather than measured. A spring integrated with the real
        /// interval would change character whenever a frame ran late; at this
        /// step size the motion is identical on every machine.
        /// </summary>
        private const float FrameSeconds = 1f / 60f;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // The dot field carries the canvas colour with it, so there is no
            // separate background fill: its rest layer is the background.
            g.DrawImageUnscaled(Field().RestLayer, 0, 0);
            Field().DrawLive(g);

            if (Art() != null) Art().Draw(g);

            g.DrawImageUnscaled(Foreground(), 0, 0);
            DrawStartButton(g);
            DrawClose(g);
        }

        /// <summary>
        /// Everything above the dot field that never moves. Drawn on a
        /// transparent layer so the live dots underneath still show through
        /// around the lettering.
        /// </summary>
        private Bitmap Foreground()
        {
            if (_foreground != null) return _foreground;

            _foreground = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_foreground))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                DrawTitle(g);
                DrawSparkles(g);
                DrawSubtitle(g);
            }
            return _foreground;
        }

        private DotField Field()
        {
            if (_dots == null)
                _dots = new DotField(CanvasWidth, CanvasHeight,
                                     DotSpacing * CanvasWidth,
                                     Math.Max(1.4f, DotDiameter * CanvasWidth),
                                     Canvas, DotGrid, TitleShadow);
            return _dots;
        }

        private Collage Art()
        {
            if (_collage == null)
            {
                _collage = new Collage(CanvasWidth, CanvasHeight);
                if (_collage.IsEmpty)
                    Log.Write("No collage cut-outs found; the bottom of the welcome screen will be bare.");
            }
            return _collage.IsEmpty ? null : _collage;
        }

        /// <summary>
        /// Runs only while something is actually in motion. With the pointer
        /// away - or resting still, once the dots have taken their shape around
        /// it - the timer stops and the screen costs nothing.
        /// </summary>
        private void Animate()
        {
            if (_anim == null)
            {
                _anim = new Timer();
                _anim.Interval = 16;
                _anim.Tick += OnFrame;
            }
            if (!_anim.Enabled) _anim.Start();
        }

        private void OnFrame(object sender, EventArgs e)
        {
            Rectangle before = Merge(Field().LiveBounds,
                                     Art() == null ? Rectangle.Empty : Art().LiveBounds);

            bool dotsMoving = Field().Step(FrameSeconds);
            bool artMoving = Art() != null && Art().Step(FrameSeconds);

            Rectangle after = Merge(Field().LiveBounds,
                                    Art() == null ? Rectangle.Empty : Art().LiveBounds);

            Rectangle dirty = Merge(before, after);
            if (!dirty.IsEmpty)
            {
                dirty.Inflate(2, 2);
                Invalidate(dirty);
            }

            if (!dotsMoving && !artMoving) _anim.Stop();
        }

        /// <summary>Union that treats an empty rectangle as nothing, rather than as the origin.</summary>
        private static Rectangle Merge(Rectangle a, Rectangle b)
        {
            if (a.IsEmpty) return b;
            if (b.IsEmpty) return a;
            return Rectangle.Union(a, b);
        }

        private void DrawTitle(Graphics g)
        {
            float targetWidth = TitleWidth * CanvasWidth;
            float centreY = TitleCentreY * CanvasHeight;

            // A wordmark exported from the design drops straight in and wins,
            // since the layered lettering is artwork rather than plain text.
            Image art = TitleArt();
            if (art != null)
            {
                float scale = targetWidth / art.Width;
                float w = targetWidth, h = art.Height * scale;
                g.DrawImage(art, (CanvasWidth - w) / 2f, centreY - h / 2f, w, h);
                return;
            }

            if (_titleFont == null)
                _titleFont = FitInk(TitleText, TitleFace, FontStyle.Regular, targetWidth, 400f, out _titleInk);

            // Offset colour copy first, white lettering over it.
            using (Brush b = new SolidBrush(TitleShadow))
                DrawInked(g, TitleText, _titleFont, _titleInk, b, CanvasWidth / 2f, centreY,
                          TitleShadowDx * CanvasWidth, TitleShadowDy * CanvasHeight);

            using (Brush b = new SolidBrush(Paper))
                DrawInked(g, TitleText, _titleFont, _titleInk, b, CanvasWidth / 2f, centreY, 0, 0);
        }

        /// <summary>
        /// Draws text so its painted extents - not its line box - are centred on
        /// a point. Text with no descenders would otherwise float high, and GDI+
        /// clips a string outright if given a rectangle shorter than its line.
        /// </summary>
        private static void DrawInked(Graphics g, string text, Font font, RectangleF ink,
                                      Brush brush, float centreX, float centreY, float dx, float dy)
        {
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(text, font, brush,
                             centreX - ink.Left - ink.Width / 2f + dx,
                             centreY - ink.Top - ink.Height / 2f + dy,
                             format);
            }
        }

        private void DrawSubtitle(Graphics g)
        {
            if (_subtitleFont == null)
            {
                _subtitleFont = FitTracked(g, SubtitleText, SubtitleFace,
                                           SubtitleWidth * CanvasWidth, SubtitleTracking,
                                           40f, out _subtitleTrackingPx);
                _subtitleInk = MeasureInk(SubtitleText, _subtitleFont);
            }

            // Horizontally by tracked advance, vertically by ink, so the line
            // sits where the design puts it regardless of the face in use.
            float width = TrackedWidth(g, SubtitleText, _subtitleFont, _subtitleTrackingPx);
            float x = (CanvasWidth - width) / 2f;
            float y = SubtitleCentreY * CanvasHeight - _subtitleInk.Top - _subtitleInk.Height / 2f;

            using (Brush b = new SolidBrush(Paper))
                DrawTracked(g, SubtitleText, _subtitleFont, b, x, y, _subtitleTrackingPx);
        }

        private void DrawStartButton(Graphics g)
        {
            Rectangle face = StartFace;
            int offset = R(ShadowOffset, CanvasWidth);

            // Hard offset shadow, no blur - it is a solid shape in the design.
            Rectangle shadow = face;
            shadow.Offset(offset, offset);
            using (Brush b = new SolidBrush(ButtonShadow))
                g.FillRectangle(b, shadow);

            // Pressing pushes the face onto its shadow.
            if (_pressStart) face.Offset(offset, offset);

            using (Brush b = new SolidBrush(_hoverStart ? Color.FromArgb(0xF2, 0xF2, 0xF2) : Paper))
                g.FillRectangle(b, face);

            if (_buttonFont == null)
            {
                // Size is always solved against the design's own label, so both
                // captions render at the same weight and only their width
                // differs - rather than the longer one shrinking to fit.
                RectangleF reference;
                _buttonFont = FitInk(StartButtonText, ButtonFace, FontStyle.Regular,
                                     ButtonTextWidth * face.Width, 40f, out reference);
                _buttonInk = MeasureInk(ButtonLabel, _buttonFont);
            }

            using (Brush b = new SolidBrush(ButtonInk))
                DrawInked(g, ButtonLabel, _buttonFont, _buttonInk, b,
                          face.Left + face.Width / 2f, face.Top + face.Height / 2f, 0, 0);
        }

        /// <summary>
        /// The design has no close affordance, but a window needs one. Kept
        /// deliberately dim so it does not compete with the artwork.
        /// </summary>
        private void DrawClose(Graphics g)
        {
            Rectangle box = CloseBox;
            using (Pen pen = new Pen(_hoverClose ? Paper : Dim, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int inset = 8;
                g.DrawLine(pen, box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
                g.DrawLine(pen, box.Right - inset, box.Top + inset, box.Left + inset, box.Bottom - inset);
            }
        }

        private void DrawSparkles(Graphics g)
        {
            Sparkle(g, 0.103f, 0.2325f, 0.017f, SparklePeach);
            Sparkle(g, 0.481f, 0.1950f, 0.015f, SparklePink);
            Sparkle(g, 0.898f, 0.3400f, 0.014f, SparkleMint);
        }

        /// <summary>Four-pointed star with concave sides, drawn about a centre.</summary>
        private void Sparkle(Graphics g, float xFraction, float yFraction, float radiusFraction, Color colour)
        {
            float cx = xFraction * CanvasWidth;
            float cy = yFraction * CanvasHeight;
            float r = radiusFraction * CanvasWidth;
            float waist = r * 0.20f;   // how sharply the sides pinch toward the centre

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddBezier(cx, cy - r, cx + waist, cy - waist, cx + waist, cy - waist, cx + r, cy);
                path.AddBezier(cx + r, cy, cx + waist, cy + waist, cx + waist, cy + waist, cx, cy + r);
                path.AddBezier(cx, cy + r, cx - waist, cy + waist, cx - waist, cy + waist, cx - r, cy);
                path.AddBezier(cx - r, cy, cx - waist, cy - waist, cx - waist, cy - waist, cx, cy - r);
                path.CloseFigure();

                using (Brush b = new SolidBrush(colour))
                    g.FillPath(b, path);
            }
        }

        // ------------------------------------------------------------ resources
        private static string TitleFace
        {
            get { return FontLoader.FirstAvailable("Monograph", "Georgia", "Times New Roman"); }
        }

        /// <summary>
        /// The button label is set in the sans, not the display serif - the
        /// redesign moved it off Monograph.
        /// </summary>
        private static string ButtonFace
        {
            get
            {
                return FontLoader.FirstAvailable(
                    "Instrument Sans SemiBold", "Instrument Sans", "Segoe UI Semibold", "Segoe UI");
            }
        }

        private static string SubtitleFace
        {
            get
            {
                // Instrument Sans is what the design uses. It is OFL-licensed,
                // so it ships in fonts\ rather than being hoped for.
                return FontLoader.FirstAvailable(
                    "Instrument Sans", "Inter", "Segoe UI");
            }
        }

        /// <summary>Optional wordmark exported from the design, if one was placed beside the exe.</summary>
        private Image TitleArt()
        {
            return Asset("title.png", ref _titleArt, ref _titleArtChecked);
        }

        /// <summary>
        /// Loads optional artwork from an assets\ folder beside the executable.
        /// A missing file is normal: the screen simply draws what it can.
        /// </summary>
        private static Image Asset(string fileName, ref Image cache, ref bool alreadyChecked)
        {
            if (alreadyChecked) return cache;
            alreadyChecked = true;
            try
            {
                string path = Path.Combine(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets"), fileName);

                // Copied out of the file so the handle is not held open, which
                // would stop an installer replacing the asset.
                if (File.Exists(path))
                    using (Image loaded = Image.FromFile(path))
                        cache = new Bitmap(loaded);
            }
            catch (Exception ex)
            {
                Log.Write("Asset '" + fileName + "' could not be loaded: " + ex.Message);
            }
            return cache;
        }

        /// <summary>
        /// Picks the point size whose painted width matches
        /// <paramref name="targetInkWidth"/>, and reports the resulting extents.
        /// Measuring ink rather than advance width keeps the layout matched to
        /// the design regardless of a face's side bearings - which matters most
        /// when the intended font is missing and something else stands in.
        /// </summary>
        private static Font FitInk(string text, string family, FontStyle style,
                                   float targetInkWidth, float maxSize, out RectangleF ink)
        {
            const float probeSize = 100f;

            float size = maxSize;
            using (Font probe = FontLoader.Get(family, probeSize, style, "Segoe UI"))
            {
                RectangleF probeInk = MeasureInk(text, probe);
                if (probeInk.Width > 0.01f)
                    size = probeSize * targetInkWidth / probeInk.Width;
            }

            if (size > maxSize) size = maxSize;
            if (size < 6f) size = 6f;

            // Hinting means the scaled guess is close but not exact, so the
            // extents actually used are measured at the final size.
            Font font = FontLoader.Get(family, size, style, "Segoe UI");
            ink = MeasureInk(text, font);
            return font;
        }

        private static StringFormat Typographic()
        {
            StringFormat format = new StringFormat(StringFormat.GenericTypographic);
            // MeasureTrailingSpaces matters when measuring one glyph at a time:
            // without it a lone space measures as zero and words run together.
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            return format;
        }

        /// <summary>Em size in device pixels, which is what tracking is measured against.</summary>
        private static float EmPixels(Graphics g, Font font)
        {
            return font.SizeInPoints * g.DpiY / 72f;
        }

        /// <summary>
        /// Advance width of a string drawn with letter spacing. GDI+ has no
        /// tracking of its own, so spacing is applied per glyph and the trailing
        /// gap after the last one is discounted.
        /// </summary>
        private static float TrackedWidth(Graphics g, string text, Font font, float trackingPx)
        {
            using (StringFormat format = Typographic())
            {
                float width = 0;
                foreach (char ch in text)
                    width += g.MeasureString(ch.ToString(), font, PointF.Empty, format).Width + trackingPx;
                return text.Length > 0 ? width - trackingPx : 0;
            }
        }

        /// <summary>
        /// Draws text glyph by glyph to apply tracking. This forgoes kerning
        /// pairs, which is an acceptable trade for a short line in a geometric
        /// sans where the design's own tracking dominates anyway.
        /// </summary>
        private static void DrawTracked(Graphics g, string text, Font font, Brush brush,
                                        float x, float y, float trackingPx)
        {
            using (StringFormat format = Typographic())
            {
                foreach (char ch in text)
                {
                    string s = ch.ToString();
                    g.DrawString(s, font, brush, x, y, format);
                    x += g.MeasureString(s, font, PointF.Empty, format).Width + trackingPx;
                }
            }
        }

        /// <summary>
        /// Sizes a face so its tracked width matches the target. Tracking is
        /// held as a fraction of the em, so width stays linear in point size and
        /// one probe measurement solves it.
        /// </summary>
        private static Font FitTracked(Graphics g, string text, string family, float targetWidth,
                                       float trackingEm, float maxSize, out float trackingPx)
        {
            const float probeSize = 100f;

            float size = maxSize;
            using (Font probe = FontLoader.Get(family, probeSize, FontStyle.Regular, "Segoe UI"))
            {
                float width = TrackedWidth(g, text, probe, trackingEm * EmPixels(g, probe));
                if (width > 0.01f) size = probeSize * targetWidth / width;
            }

            if (size > maxSize) size = maxSize;
            if (size < 6f) size = 6f;

            Font font = FontLoader.Get(family, size, FontStyle.Regular, "Segoe UI");
            trackingPx = trackingEm * EmPixels(g, font);
            return font;
        }

        /// <summary>
        /// Painted bounds of <paramref name="text"/> relative to the origin it
        /// would be drawn at. Found by rendering once and scanning for coverage,
        /// which no font metric exposes directly.
        /// </summary>
        private static RectangleF MeasureInk(string text, Font font)
        {
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.FormatFlags |= StringFormatFlags.NoWrap;

                SizeF advance;
                using (Bitmap scratch = new Bitmap(1, 1))
                using (Graphics probe = Graphics.FromImage(scratch))
                    advance = probe.MeasureString(text, font, PointF.Empty, format);

                RectangleF fallback = new RectangleF(0, 0, advance.Width, advance.Height);

                int pad = (int)Math.Ceiling(font.Size) + 8;
                int w = (int)Math.Ceiling(advance.Width) + pad * 2;
                int h = (int)Math.Ceiling(advance.Height) + pad * 2;
                if (w < 2 || h < 2 || w > 4096 || h > 4096) return fallback;

                try
                {
                    using (Bitmap canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(canvas))
                        {
                            g.Clear(Color.Transparent);
                            g.TextRenderingHint = TextRenderingHint.AntiAlias;
                            using (Brush b = new SolidBrush(Color.White))
                                g.DrawString(text, font, b, pad, pad, format);
                        }
                        return ScanCoverage(canvas, pad, fallback);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Ink measurement failed: " + ex.Message);
                    return fallback;
                }
            }
        }

        private static RectangleF ScanCoverage(Bitmap canvas, int pad, RectangleF fallback)
        {
            int w = canvas.Width, h = canvas.Height;
            BitmapData data = canvas.LockBits(new Rectangle(0, 0, w, h),
                                              ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] pixels = new byte[data.Stride * h];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        // Alpha only; anything faint enough to ignore is noise.
                        if (pixels[row + x * 4 + 3] <= 8) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX < 0) return fallback;
                return new RectangleF(minX - pad, minY - pad, maxX - minX + 1, maxY - minY + 1);
            }
            finally
            {
                canvas.UnlockBits(data);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_titleFont != null) _titleFont.Dispose();
                if (_subtitleFont != null) _subtitleFont.Dispose();
                if (_buttonFont != null) _buttonFont.Dispose();
                if (_titleArt != null) _titleArt.Dispose();
                if (_anim != null) _anim.Dispose();
                if (_dots != null) _dots.Dispose();
                if (_collage != null) _collage.Dispose();
                if (_foreground != null) _foreground.Dispose();
                if (Icon != null) Icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
