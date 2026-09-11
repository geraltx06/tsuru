using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Tsuru
{
    /// <summary>
    /// A white slab over a hard offset shadow in an accent colour, as the
    /// design draws it - no blur, no rounding, no gradient. Pressing pushes the
    /// face down onto its shadow, which is the whole of the affordance.
    ///
    /// The control's bounds include the shadow, so <see cref="FaceSize"/> is
    /// what the caller should reason about when placing two side by side.
    /// </summary>
    internal sealed class ShadowButton : Control
    {
        private readonly Color _shadow;
        private bool _hover;
        private bool _pressed;

        public ShadowButton(string caption, Color shadow)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            _shadow = shadow;
            Text = caption;
            BackColor = Theme.Canvas;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        /// <summary>Size of the white face alone, without the shadow underneath it.</summary>
        public Size FaceSize
        {
            get { return new Size(Width - Theme.ShadowOffset, Height - Theme.ShadowOffset); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter) return;
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        /// <summary>Owner-drawn controls are not repainted for this on their own.</summary>
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Theme.Canvas);

            int off = Theme.ShadowOffset;
            Size face = FaceSize;

            using (Brush b = new SolidBrush(_shadow))
                g.FillRectangle(b, off, off, face.Width, face.Height);

            Rectangle top = _pressed
                ? new Rectangle(off, off, face.Width, face.Height)
                : new Rectangle(0, 0, face.Width, face.Height);

            using (Brush b = new SolidBrush(_hover ? Color.FromArgb(0xF2, 0xF2, 0xF2) : Theme.Paper))
                g.FillRectangle(b, top);

            if (Focused && ShowFocusCues)
                using (Pen p = new Pen(_shadow, 2f))
                    g.DrawRectangle(p, top.X + 1, top.Y + 1, top.Width - 3, top.Height - 3);

            // Theme fonts are cached and shared, so this one is not disposed.
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (Brush b = new SolidBrush(Theme.Ink))
                    g.DrawString(Text, Theme.SansSemi(17f), b, top, sf);
            }
        }
    }
}
