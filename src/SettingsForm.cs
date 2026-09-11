using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Tsuru
{
    /// <summary>
    /// Live-applying settings window. Every change takes effect immediately so
    /// the feel can be judged by scrolling another window while it is open,
    /// which is why this is shown non-modally.
    ///
    /// Laid out on the dark design rather than on system controls: the switches
    /// and segmented sliders are drawn by <see cref="ToggleSwitch"/> and
    /// <see cref="SegmentBar"/>, and coordinates are fixed pixels transcribed
    /// from the design frame.
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly Action<Settings> _apply;
        private Settings _working;

        private readonly ToolTip _tips = new ToolTip();
        private readonly List<Action> _refreshers = new List<Action>();
        private bool _loading;

        private Panel _viewport;
        private Panel _content;
        private int _y;

        // ------------------------------------------------------------ metrics
        private const int FormW = 560;
        private const int FormH = 720;
        private const int HeaderH = 100;
        private const int FooterH = 88;

        /// <summary>Strip down the right of the viewport reserved for the scroll thumb.</summary>
        private const int ScrollStrip = 8;

        private const int ElemX = Theme.Gutter;
        private const int ElemW = FormW - ScrollStrip - Theme.Gutter * 2;

        private const int RowLabelH = 22;
        private const int ToggleRowH = 44;
        private const int SliderRowH = RowLabelH + Theme.BarHeight + 16;
        private const int CornerRadius = 18;

        public SettingsForm(Settings current, Action<Settings> apply)
        {
            _working = current.Clone();
            _apply = apply;

            BuildForm();
            BuildContent();
            Reload();
        }

        // ------------------------------------------------------------- chrome
        private void BuildForm()
        {
            Text = "Tsuru Settings";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(FormW, FormH);
            BackColor = Theme.Canvas;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = true;
            KeyPreview = true;
            Icon = IconFactory.Create(true, 32);

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            _viewport = new Panel();
            _viewport.Location = new Point(0, HeaderH);
            _viewport.Size = new Size(FormW, FormH - HeaderH - FooterH);
            _viewport.BackColor = Theme.Canvas;
            _viewport.Paint += PaintScrollThumb;
            Controls.Add(_viewport);

            _content = new Panel();
            _content.Location = new Point(0, 0);
            _content.Width = FormW - ScrollStrip;
            _content.BackColor = Theme.Canvas;
            _viewport.Controls.Add(_content);

            BuildFooter();
            _y = 8;
        }

        private void BuildFooter()
        {
            int faceW = (ElemW - 16) / 2;
            int top = FormH - FooterH + 22;

            ShadowButton reset = new ShadowButton("Reset to default", Theme.Peach);
            reset.Location = new Point(ElemX, top);
            reset.Size = new Size(faceW + Theme.ShadowOffset, 44 + Theme.ShadowOffset);
            reset.Click += delegate
            {
                Settings fresh = new Settings();
                // The things that are not part of "feel" survive a reset: they
                // are not what someone is trying to undo here.
                fresh.Enabled = _working.Enabled;
                fresh.StartWithWindows = _working.StartWithWindows;
                fresh.OnboardingComplete = _working.OnboardingComplete;
                fresh.Excluded = new List<string>(_working.Excluded);
                _working = fresh;
                Push();
                Reload();
            };
            _tips.SetToolTip(reset, "Puts every slider back to its shipped value.");
            Controls.Add(reset);

            ShadowButton save = new ShadowButton("Save settings", Theme.Violet);
            save.Location = new Point(ElemX + faceW + 16, top);
            save.Size = new Size(faceW + Theme.ShadowOffset, 44 + Theme.ShadowOffset);
            save.Click += delegate { _working.Save(); Close(); };
            _tips.SetToolTip(save, "Changes already apply as you make them; this writes them to disk and closes.");
            Controls.Add(save);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedCorners();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DwmWindowCornerPreference = 33;
        private const int DwmCornerRound = 2;

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

            using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width, Height), CornerRadius))
                Region = new Region(path);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------------------------------------------------------- title bar
        private Rectangle CloseBox
        {
            get { return new Rectangle(FormW - 42, 18, 26, 26); }
        }

        private bool _hoverClose;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 0x0002;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = CloseBox.Contains(e.Location);
            if (over == _hoverClose) return;
            _hoverClose = over;
            Cursor = over ? Cursors.Hand : Cursors.Default;
            Invalidate(CloseBox);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (CloseBox.Contains(e.Location)) { Close(); return; }

            // Borderless: dragging the chrome moves the window.
            if (e.Y < HeaderH)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Canvas);

            using (Brush b = new SolidBrush(Theme.Paper))
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                g.DrawString("Settings", Theme.Display(52f), b, ElemX - 3, 22, sf);

            Rectangle box = CloseBox;
            using (Pen p = new Pen(_hoverClose ? Theme.Paper : Theme.Dim, 1.6f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                int inset = 8;
                g.DrawLine(p, box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
                g.DrawLine(p, box.Right - inset, box.Top + inset, box.Left + inset, box.Bottom - inset);
            }
        }

        // ---------------------------------------------------------- scrolling
        /// <summary>
        /// The viewport scrolls by moving its content rather than through
        /// AutoScroll, so the window keeps its own slim thumb instead of a
        /// system scrollbar - which cannot be themed, and would be a bright
        /// strip down the side of an otherwise black window.
        /// </summary>
        /// <summary>
        /// Positive <paramref name="travel"/> scrolls towards the end of the
        /// content - the same sense as a wheel notch pulled towards you.
        /// </summary>
        private void ScrollBy(int travel)
        {
            int limit = Math.Min(0, _viewport.Height - _content.Height);
            int next = Math.Max(limit, Math.Min(0, _content.Top - travel));
            if (next == _content.Top) return;

            _content.Top = next;
            _viewport.Invalidate();
        }

        /// <summary>Pixels of travel for one full notch.</summary>
        private const float WheelStep = 54f;

        /// <summary>Carries the sub-pixel part of a wheel delta into the next event.</summary>
        private float _wheelRemainder;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            // Not delta/120: Tsuru is very likely smoothing this very wheel, so
            // what arrives here is a stream of small fractions of a notch rather
            // than one notch at a time. Integer-dividing those would floor every
            // one of them to zero and the window would never move.
            // Negated: a wheel pulled towards you reports a negative delta and
            // should move the content towards its end.
            float travel = -e.Delta * (WheelStep / 120f) + _wheelRemainder;
            int whole = (int)travel;
            _wheelRemainder = travel - whole;

            if (whole != 0) ScrollBy(whole);
        }

        private void PaintScrollThumb(object sender, PaintEventArgs e)
        {
            int total = _content.Height, view = _viewport.Height;
            if (total <= view) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int thumb = Math.Max(36, view * view / total);
            int range = view - thumb;
            float where = total == view ? 0f : -_content.Top / (float)(total - view);
            int y = (int)Math.Round(where * range);

            using (Brush b = new SolidBrush(Theme.Track))
                g.FillRectangle(b, _viewport.Width - 6, y, 4, thumb);
        }

        // ------------------------------------------------------------ content
        private void BuildContent()
        {
            ToggleRow("Tsuru effect",
                delegate { return _working.Enabled; },
                delegate(bool v) { _working.Enabled = v; },
                "Turn all smoothing on or off. Same as the tray menu.");

            Section("Feel");

            SliderRow("Step size", 30, 600, 1, "",
                delegate { return _working.StepSize; },
                delegate(double v) { _working.StepSize = v; },
                "Distance covered by one wheel notch. 120 matches how far Windows scrolls on its own.");

            SliderRow("Animation time", 50, 1500, 1, "ms",
                delegate { return _working.AnimationTime; },
                delegate(double v) { _working.AnimationTime = v; },
                "How long a single notch takes to play out. Higher is floatier.");

            SliderRow("Smoothness", 1, 20, 1, "",
                delegate { return _working.PulseScale; },
                delegate(double v) { _working.PulseScale = v; },
                "Shape of the easing curve. Higher settles more sharply at the end.");

            ToggleRow("Use pulse easing",
                delegate { return _working.PulseAlgorithm; },
                delegate(bool v) { _working.PulseAlgorithm = v; },
                "Off gives plain linear motion instead of the eased curve.");

            Separator();
            Section("Acceleration");

            SliderRow("Acceleration window", 0, 200, 1, "ms",
                delegate { return _working.AccelDelta; },
                delegate(double v) { _working.AccelDelta = v; },
                "Notches arriving within this window compound. 0 turns acceleration off.");

            SliderRow("Maximum boost", 10, 100, 0.1, "x",
                delegate { return _working.AccelMax; },
                delegate(double v) { _working.AccelMax = v; },
                "Upper limit on how much a fast flick is multiplied.");

            Separator();
            Section("Axes");

            ToggleRow("Smooth vertical scrolling",
                delegate { return _working.SmoothVertical; },
                delegate(bool v) { _working.SmoothVertical = v; }, null);

            ToggleRow("Smooth horizontal scrolling",
                delegate { return _working.SmoothHorizontal; },
                delegate(bool v) { _working.SmoothHorizontal = v; },
                "Applies to tilt wheels and Shift+wheel in some apps.");

            ToggleRow("Invert vertical direction",
                delegate { return _working.InvertVertical; },
                delegate(bool v) { _working.InvertVertical = v; },
                "Natural / reversed scrolling.");

            ToggleRow("Invert horizontal direction",
                delegate { return _working.InvertHorizontal; },
                delegate(bool v) { _working.InvertHorizontal = v; }, null);

            Separator();
            Section("Compatibility");

            SliderRow("Update rate", 30, 240, 1, "fps",
                delegate { return _working.FrameRate; },
                delegate(double v) { _working.FrameRate = (int)Math.Round(v); },
                "How often motion is delivered. Match your monitor for the smoothest result.");

            ToggleRow("Leave precision touchpads alone",
                delegate { return _working.IgnorePrecisionDevices; },
                delegate(bool v) { _working.IgnorePrecisionDevices = v; },
                "Touchpads already scroll smoothly; re-animating them adds lag.");

            ToggleRow("Ignore scrolling from other apps",
                delegate { return _working.IgnoreInjected; },
                delegate(bool v) { _working.IgnoreInjected = v; },
                "Prevents fighting other automation tools that synthesise scroll events.");

            Separator();
            Section("Excluded apps");

            Caption("One executable name per line, e.g. game.exe. These keep native scrolling.");
            ExcludedBox();

            Separator();
            Section("Startup");

            ToggleRow("Start Tsuru when I sign in",
                delegate { return _working.StartWithWindows; },
                delegate(bool v)
                {
                    _working.StartWithWindows = v;
                    StartupManager.SetEnabled(v);
                }, null);

            _content.Height = _y + 24;
        }

        /// <summary>
        /// Reflects a change made outside this window - the tray menu - so the
        /// two never disagree about whether smoothing is on.
        /// </summary>
        public void SyncEnabled(bool enabled)
        {
            if (_working.Enabled == enabled) return;
            _working.Enabled = enabled;
            Reload();
        }

        // ----------------------------------------------------------- builders
        private delegate double Getter();
        private delegate void Setter(double value);
        private delegate bool BoolGetter();
        private delegate void BoolSetter(bool value);

        private void Section(string text)
        {
            _y += 20;

            InkLabel header = new InkLabel(text.ToUpperInvariant(), Theme.SansSemi(13f),
                                           Theme.Dim, ContentAlignment.TopLeft, 1.6f);
            header.Location = new Point(ElemX, _y);
            header.Size = new Size(ElemW, 18);
            _content.Controls.Add(header);

            _y += 30;
        }

        private void Separator()
        {
            _y += 22;

            Panel rule = new Panel();
            rule.Location = new Point(ElemX, _y);
            rule.Size = new Size(ElemW, 1);
            rule.BackColor = Theme.Rule;
            _content.Controls.Add(rule);

            _y += 1;
        }

        private void Caption(string text)
        {
            InkLabel note = MakeLabel(text, Theme.Sans(14f), Theme.Dim, ContentAlignment.TopLeft);
            note.Location = new Point(ElemX, _y);
            note.Size = new Size(ElemW, 34);
            _content.Controls.Add(note);
            _y += 38;
        }

        private void ToggleRow(string text, BoolGetter get, BoolSetter set, string hint)
        {
            InkLabel label = MakeLabel(text, Theme.Sans(16f), Theme.Paper, ContentAlignment.MiddleLeft);
            label.Location = new Point(ElemX, _y);
            label.Size = new Size(ElemW - 80, ToggleRowH);
            _content.Controls.Add(label);

            ToggleSwitch toggle = new ToggleSwitch();
            toggle.Location = new Point(ElemX + ElemW - toggle.Width, _y + (ToggleRowH - toggle.Height) / 2);
            toggle.CheckedChanged += delegate
            {
                if (_loading) return;
                set(toggle.Checked);
                Push();
            };
            _content.Controls.Add(toggle);

            if (!string.IsNullOrEmpty(hint))
            {
                _tips.SetToolTip(label, hint);
                _tips.SetToolTip(toggle, hint);
            }

            _refreshers.Add(delegate { toggle.SetQuietly(get()); });
            _y += ToggleRowH;
        }

        /// <summary>
        /// A labelled slider. <paramref name="scale"/> converts one tick to a
        /// setting value, which is what lets an integer bar express fractions.
        /// </summary>
        private void SliderRow(string text, int min, int max, double scale, string suffix,
                               Getter get, Setter set, string hint)
        {
            InkLabel label = MakeLabel(text, Theme.Sans(16f), Theme.Paper, ContentAlignment.MiddleLeft);
            label.Location = new Point(ElemX, _y);
            label.Size = new Size(ElemW - 110, RowLabelH);
            _content.Controls.Add(label);

            InkLabel value = MakeLabel("", Theme.Sans(16f), Theme.Paper, ContentAlignment.MiddleRight);
            value.Location = new Point(ElemX + ElemW - 110, _y);
            value.Size = new Size(110, RowLabelH);
            _content.Controls.Add(value);

            SegmentBar bar = new SegmentBar();
            bar.Minimum = min;
            bar.Maximum = max;
            bar.Location = new Point(ElemX, _y + RowLabelH + 6);
            bar.Size = new Size(ElemW, Theme.BarHeight);
            _content.Controls.Add(bar);

            bar.ValueChanged += delegate
            {
                double v = bar.Value * scale;
                value.Text = Format(v, scale, suffix);
                if (_loading) return;
                set(v);
                Push();
            };

            if (!string.IsNullOrEmpty(hint))
            {
                _tips.SetToolTip(label, hint);
                _tips.SetToolTip(bar, hint);
            }

            _refreshers.Add(delegate
            {
                int ticks = (int)Math.Round(get() / scale);
                bar.SetQuietly(ticks);
                value.Text = Format(bar.Value * scale, scale, suffix);
            });

            _y += SliderRowH;
        }

        private void ExcludedBox()
        {
            Color well = Color.FromArgb(0x16, 0x16, 0x16);

            // A hairline frame drawn as a panel behind the field. The built-in
            // border and scrollbar are both system-coloured and cannot be
            // themed, and either one is a bright fitting on a black window.
            Panel frame = new Panel();
            frame.Location = new Point(ElemX, _y);
            frame.Size = new Size(ElemW, 98);
            frame.BackColor = Theme.Rule;
            _content.Controls.Add(frame);

            Panel inner = new Panel();
            inner.Location = new Point(1, 1);
            inner.Size = new Size(ElemW - 2, 96);
            inner.BackColor = well;
            frame.Controls.Add(inner);

            TextBox box = new TextBox();
            box.Multiline = true;
            // No scrollbar to theme: a multiline field still scrolls with the
            // wheel and follows the caret without one.
            box.ScrollBars = ScrollBars.None;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = well;
            box.ForeColor = Theme.Paper;
            box.Font = Theme.Sans(15f);
            box.Location = new Point(8, 7);
            box.Size = new Size(ElemW - 18, 82);
            box.TextChanged += delegate
            {
                if (_loading) return;
                List<string> list = new List<string>();
                foreach (string line in box.Lines)
                {
                    string t = line.Trim();
                    if (t.Length > 0) list.Add(t);
                }
                _working.Excluded = list;
                Push();
            };
            inner.Controls.Add(box);

            _refreshers.Add(delegate { box.Lines = _working.Excluded.ToArray(); });
            _y += 102;
        }

        private static InkLabel MakeLabel(string text, Font font, Color ink, ContentAlignment align)
        {
            return new InkLabel(text, font, ink, align, 0f);
        }

        private static string Format(double v, double scale, string suffix)
        {
            string number = scale < 1
                ? v.ToString("0.0", CultureInfo.InvariantCulture)
                : v.ToString("0", CultureInfo.InvariantCulture);
            return suffix.Length > 0 ? number + " " + suffix : number;
        }

        // -------------------------------------------------------------- state
        /// <summary>Repaints every control from <see cref="_working"/> without re-firing handlers.</summary>
        private void Reload()
        {
            _loading = true;
            try
            {
                foreach (Action refresh in _refreshers) refresh();
            }
            finally
            {
                _loading = false;
            }
        }

        private void Push()
        {
            _apply(_working.Clone());
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _working.Save();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tips.Dispose();
                if (Icon != null) Icon.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Every piece of text in the window.
        ///
        /// A plain <see cref="Label"/> renders through GDI, which means
        /// ClearType, which means coloured fringes around each glyph - barely
        /// visible on the grey Windows draws its own dialogs in, and obvious on
        /// near-black. This draws with grayscale antialiasing instead, so the
        /// type stays neutral against the canvas.
        ///
        /// It also carries optional letter-spacing, which the section headings
        /// need and no WinForms label can express.
        /// </summary>
        private sealed class InkLabel : Control
        {
            private readonly Font _face;
            private readonly Color _ink;
            private readonly ContentAlignment _align;
            private readonly float _tracking;

            public InkLabel(string text, Font face, Color ink, ContentAlignment align, float tracking)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer, true);

                Text = text;
                _face = face;
                _ink = ink;
                _align = align;
                _tracking = tracking;
                BackColor = Theme.Canvas;
            }

            /// <summary>
            /// Control does not repaint an owner-drawn child when its text
            /// changes - only the stock controls, which invalidate themselves.
            /// Without this the value beside a slider keeps whatever it was
            /// first painted with while the bar moves underneath it.
            /// </summary>
            protected override void OnTextChanged(EventArgs e)
            {
                base.OnTextChanged(e);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.Clear(Theme.Canvas);

                using (Brush b = new SolidBrush(_ink))
                {
                    if (_tracking <= 0f)
                    {
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = _align == ContentAlignment.MiddleRight
                                ? StringAlignment.Far : StringAlignment.Near;
                            sf.LineAlignment = _align == ContentAlignment.TopLeft
                                ? StringAlignment.Near : StringAlignment.Center;
                            g.DrawString(Text, _face, b, ClientRectangle, sf);
                        }
                        return;
                    }

                    using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                    {
                        float x = 0f;
                        foreach (char c in Text)
                        {
                            string s = c.ToString();
                            g.DrawString(s, _face, b, x, 0f, sf);
                            x += g.MeasureString(s, _face, PointF.Empty, sf).Width + _tracking;
                        }
                    }
                }
            }
        }
    }
}
