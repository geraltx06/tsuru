using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Tsuru
{
    /// <summary>
    /// The slider from the design: a row of narrow vertical segments, mint up
    /// to the current value and grey beyond it.
    ///
    /// Values are whole ticks, as <see cref="TrackBar"/> uses them, so the
    /// caller keeps its existing tick-to-value scaling. The segments are only a
    /// readout - dragging sets a value from the pointer position continuously,
    /// then the bar quantises what it draws. Snapping the value itself to
    /// segments would put a floor of about 1/60th of the range on every
    /// setting, which is far too coarse for something like animation time.
    ///
    /// The mouse wheel is deliberately not handled: this window scrolls, and a
    /// wheel that silently edited whichever slider it passed over would be a
    /// trap - in an app whose entire purpose is the wheel, especially.
    /// </summary>
    internal sealed class SegmentBar : Control
    {
        private int _min;
        private int _max = 100;
        private int _value;
        private bool _dragging;

        public event EventHandler ValueChanged;

        public SegmentBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            Height = Theme.BarHeight;
            BackColor = Theme.Canvas;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int Minimum
        {
            get { return _min; }
            set { _min = value; if (_value < _min) _value = _min; Invalidate(); }
        }

        public int Maximum
        {
            get { return _max; }
            set { _max = value; if (_value > _max) _value = _max; Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set { Apply(value, false); }
        }

        /// <summary>Sets the value without raising <see cref="ValueChanged"/>.</summary>
        public void SetQuietly(int value)
        {
            _value = Clamp(value);
            Invalidate();
        }

        private void Apply(int candidate, bool notify)
        {
            int next = Clamp(candidate);
            if (next == _value) return;

            _value = next;
            Invalidate();
            if (notify && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        private int Clamp(int v)
        {
            if (v < _min) return _min;
            if (v > _max) return _max;
            return v;
        }

        /// <summary>Where the value sits in its range, 0 to 1.</summary>
        private float Fraction
        {
            get { return _max <= _min ? 0f : (float)(_value - _min) / (_max - _min); }
        }

        private void TakeFromPointer(int x)
        {
            int span = Math.Max(1, Width - Theme.SegmentWidth);
            float t = (x - Theme.SegmentWidth / 2f) / span;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            Apply(_min + (int)Math.Round(t * (_max - _min)), true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            Focus();
            _dragging = true;
            Capture = true;
            TakeFromPointer(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) TakeFromPointer(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // A page is a twentieth of the range, so a long slider is still
            // crossable without holding an arrow key down.
            int page = Math.Max(1, (_max - _min) / 20);

            switch (e.KeyCode)
            {
                case Keys.Left: Apply(_value - 1, true); break;
                case Keys.Right: Apply(_value + 1, true); break;
                case Keys.PageDown: Apply(_value - page, true); break;
                case Keys.PageUp: Apply(_value + page, true); break;
                case Keys.Home: Apply(_min, true); break;
                case Keys.End: Apply(_max, true); break;
                default: return;
            }
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Theme.Canvas);

            int count = Math.Max(1, (Width + Theme.SegmentPitch - Theme.SegmentWidth) / Theme.SegmentPitch);

            // At least one segment lights as soon as the value leaves the floor,
            // so a low-but-not-minimum setting never reads as "off".
            float f = Fraction;
            int lit = (int)Math.Round(f * count);
            if (f > 0f && lit == 0) lit = 1;

            int h = Height;
            using (Brush on = new SolidBrush(Theme.Mint))
            using (Brush off = new SolidBrush(Focused && ShowFocusCues ? Lighten(Theme.Track) : Theme.Track))
                for (int i = 0; i < count; i++)
                    g.FillRectangle(i < lit ? on : off,
                                    i * Theme.SegmentPitch, 0, Theme.SegmentWidth, h);
        }

        /// <summary>
        /// Focus is shown by lifting the unfilled segments rather than by a ring
        /// around the control - a 32px-tall outline the full width of the window
        /// would shout far louder than the thing it is marking.
        /// </summary>
        private static Color Lighten(Color c)
        {
            return Color.FromArgb(Math.Min(255, c.R + 30),
                                  Math.Min(255, c.G + 30),
                                  Math.Min(255, c.B + 30));
        }
    }
}
