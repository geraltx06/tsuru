using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Tsuru
{
    /// <summary>
    /// The pill switch from the design: a rounded track with a circular knob
    /// that slides between the ends.
    ///
    /// The design only draws the off state - knob left, peach, on a grey track -
    /// so the on state extends it with the language the sliders already use:
    /// mint is "active", and the knob inverts to the canvas colour so it still
    /// reads against a light track.
    /// </summary>
    internal sealed class ToggleSwitch : Control
    {
        private const int TrackW = 64;
        private const int TrackH = 26;
        private const int KnobInset = 3;

        /// <summary>Per second. Matched to the collage raise so the window feels of a piece.</summary>
        private const float SlideRate = 16f;

        private bool _checked;
        private float _slide;          // 0 at the left stop, 1 at the right
        private readonly Timer _anim;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);

            Size = new Size(TrackW, TrackH);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;

            _anim = new Timer();
            _anim.Interval = 16;
            _anim.Tick += OnFrame;
        }

        public bool Checked
        {
            get { return _checked; }
            set { SetChecked(value, false); }
        }

        /// <summary>
        /// Sets the state without raising <see cref="CheckedChanged"/> and
        /// without animating, for loading stored settings into the window.
        /// </summary>
        public void SetQuietly(bool value)
        {
            _checked = value;
            _slide = value ? 1f : 0f;
            _anim.Stop();
            Invalidate();
        }

        private void SetChecked(bool value, bool notify)
        {
            if (_checked == value) return;
            _checked = value;

            if (!_anim.Enabled) _anim.Start();
            if (notify && CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            Invalidate();
        }

        private void OnFrame(object sender, EventArgs e)
        {
            float target = _checked ? 1f : 0f;
            float delta = target - _slide;

            if (Math.Abs(delta) < 0.002f)
            {
                _slide = target;
                _anim.Stop();
            }
            else
            {
                _slide += delta * (1f - (float)Math.Exp(-SlideRate * 0.016f));
            }
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            SetChecked(!_checked, true);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter) return;
            SetChecked(!_checked, true);
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Canvas);

            Rectangle track = new Rectangle(0, 0, TrackW - 1, TrackH - 1);
            Color trackInk = Blend(Theme.Track, Theme.Mint, _slide);
            using (GraphicsPath pill = Pill(track))
            using (Brush b = new SolidBrush(trackInk))
                g.FillPath(b, pill);

            // ShowFocusCues, not Focused: something has to hold focus the moment
            // the window opens, and ringing it before anyone has touched the
            // keyboard just looks like a stray selection.
            if (Focused && ShowFocusCues)
                using (GraphicsPath pill = Pill(track))
                using (Pen p = new Pen(Theme.Violet, 1.6f))
                    g.DrawPath(p, pill);

            int knob = TrackH - KnobInset * 2;
            float travel = TrackW - KnobInset * 2 - knob;
            float x = KnobInset + travel * _slide;

            using (Brush b = new SolidBrush(Blend(Theme.Peach, Theme.Canvas, _slide)))
                g.FillEllipse(b, x, KnobInset, knob, knob);
        }

        private static GraphicsPath Pill(Rectangle r)
        {
            GraphicsPath path = new GraphicsPath();
            int d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }

        private static Color Blend(Color from, Color to, float t)
        {
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null) _anim.Dispose();
            base.Dispose(disposing);
        }
    }
}
