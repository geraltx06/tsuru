using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Tsuru
{
    /// <summary>
    /// The welcome screen's dot grid, with dots that part around the cursor and
    /// spring back once it moves on.
    ///
    /// The reference interaction (the DotGrid component from React Bits) drives
    /// each dot with a GSAP inertia tween fired by pointer *speed*. That reads
    /// well on a web page but leaves the field twitching after the cursor stops,
    /// so here each dot instead springs towards an offset derived from where the
    /// cursor simply is: the grid parts while the cursor rests somewhere and
    /// closes again when it leaves.
    ///
    /// Only the disc around the cursor ever deviates, so the grid at rest is
    /// painted once into <see cref="RestLayer"/> and only the disturbed region
    /// is redrawn each frame. Repaint cost tracks the disturbance rather than
    /// the size of the window.
    /// </summary>
    internal sealed class DotField : IDisposable
    {
        private struct Dot
        {
            public float Cx, Cy;   // resting centre
            public float Ox, Oy;   // offset from it
            public float Vx, Vy;   // and how fast that is changing
        }

        /// <summary>How far a dot can be from the cursor and still feel it, in canvas px.</summary>
        private const float Proximity = 116f;

        /// <summary>Displacement of a dot sitting right under the cursor.</summary>
        private const float MaxPush = 10f;

        // Only just underdamped - critical damping for this stiffness is about
        // 2*sqrt(Stiffness) = 29.7, so this keeps a trace of give without the
        // visible wobble a looser spring would leave behind.
        private const float Stiffness = 220f;
        private const float Damping = 25f;

        // Below these a dot counts as settled, so the animation can stop rather
        // than spin the timer forever on sub-pixel motion.
        private const float RestOffset = 0.06f;
        private const float RestSpeed = 0.6f;

        private readonly Dot[] _dots;
        private readonly int _cols, _rows;
        private readonly float _tile, _diameter;
        private readonly Color _canvas, _base, _active;
        private readonly Bitmap _rest;

        private PointF _cursor;
        private bool _engaged;
        private Rectangle _live;

        public DotField(int width, int height, float spacing, float diameter,
                        Color canvas, Color baseDot, Color activeDot)
        {
            _tile = Math.Max(2f, (float)Math.Round(spacing));
            _diameter = diameter;
            _canvas = canvas;
            _base = baseDot;
            _active = activeDot;

            _cols = (int)Math.Ceiling(width / _tile) + 1;
            _rows = (int)Math.Ceiling(height / _tile) + 1;
            _dots = new Dot[_cols * _rows];

            for (int row = 0; row < _rows; row++)
                for (int col = 0; col < _cols; col++)
                {
                    Dot d = new Dot();
                    d.Cx = _tile / 2f + col * _tile;
                    d.Cy = _tile / 2f + row * _tile;
                    _dots[row * _cols + col] = d;
                }

            _rest = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(_rest))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(_canvas);
                using (Brush b = new SolidBrush(_base))
                    for (int i = 0; i < _dots.Length; i++)
                        g.FillEllipse(b, _dots[i].Cx - _diameter / 2f,
                                         _dots[i].Cy - _diameter / 2f, _diameter, _diameter);
            }
        }

        /// <summary>The grid as it looks undisturbed, including the canvas behind it.</summary>
        public Bitmap RestLayer { get { return _rest; } }

        /// <summary>The region currently deviating from <see cref="RestLayer"/>.</summary>
        public Rectangle LiveBounds { get { return _live; } }

        public void SetCursor(PointF p, bool inside)
        {
            _cursor = p;
            _engaged = inside;
        }

        /// <summary>
        /// Advances every dot one frame. Returns false once the field has
        /// settled and the cursor has gone, which is the caller's cue to stop
        /// animating.
        /// </summary>
        public bool Step(float dt)
        {
            float px = _cursor.X, py = _cursor.Y;
            bool moving = false;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < _dots.Length; i++)
            {
                Dot d = _dots[i];

                float tx = 0f, ty = 0f;
                if (_engaged)
                {
                    float dx = d.Cx - px, dy = d.Cy - py;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < Proximity * Proximity && distSq > 0.0001f)
                    {
                        float dist = (float)Math.Sqrt(distSq);
                        // Eased towards the outside rather than squared. A
                        // squared falloff collapses the whole effect into the
                        // couple of cells directly under the pointer, which at
                        // this dot size is almost invisible; this carries a
                        // readable amount of movement out to the full radius.
                        float fall = Falloff(dist);
                        float push = MaxPush * fall;
                        tx = dx / dist * push;
                        ty = dy / dist * push;
                    }
                }

                // Settled means "already where the cursor wants it", not "back
                // at the origin" - otherwise a dot held aside by a stationary
                // cursor would count as moving forever and the timer could
                // never stop while the pointer sat inside the window.
                bool settled = Math.Abs(tx - d.Ox) < RestOffset &&
                               Math.Abs(ty - d.Oy) < RestOffset &&
                               Math.Abs(d.Vx) < RestSpeed && Math.Abs(d.Vy) < RestSpeed;
                if (settled)
                {
                    if (d.Ox != tx || d.Oy != ty || d.Vx != 0f || d.Vy != 0f)
                    {
                        d.Ox = tx; d.Oy = ty;
                        d.Vx = 0f; d.Vy = 0f;
                        _dots[i] = d;
                    }
                    if (tx == 0f && ty == 0f) continue;
                }
                else
                {
                    d.Vx += ((tx - d.Ox) * Stiffness - d.Vx * Damping) * dt;
                    d.Vy += ((ty - d.Oy) * Stiffness - d.Vy * Damping) * dt;
                    d.Ox += d.Vx * dt;
                    d.Oy += d.Vy * dt;
                    _dots[i] = d;
                    moving = true;
                }

                if (d.Cx < minX) minX = d.Cx;
                if (d.Cy < minY) minY = d.Cy;
                if (d.Cx > maxX) maxX = d.Cx;
                if (d.Cy > maxY) maxY = d.Cy;
            }

            Rectangle bounds = Rectangle.Empty;
            if (minX <= maxX)
            {
                int pad = (int)Math.Ceiling(MaxPush + _diameter) + 2;
                bounds = Rectangle.FromLTRB((int)minX - pad, (int)minY - pad,
                                            (int)maxX + pad, (int)maxY + pad);
            }

            // Dots inside the disc are tinted before they have moved far, so the
            // disc has to repaint whenever the cursor is in play.
            if (_engaged)
            {
                int reach = (int)Math.Ceiling(Proximity + MaxPush + _diameter) + 2;
                Rectangle disc = new Rectangle((int)px - reach, (int)py - reach, reach * 2, reach * 2);
                bounds = bounds.IsEmpty ? disc : Rectangle.Union(bounds, disc);
            }

            _live = bounds;
            return moving;
        }

        /// <summary>
        /// Repaints the disturbed region over the top of <see cref="RestLayer"/>.
        /// The region is cleared to the canvas colour first, which is safe only
        /// because the grid sits directly on flat colour - everything else on
        /// the screen is drawn after it.
        /// </summary>
        public void DrawLive(Graphics g)
        {
            if (_live.IsEmpty) return;

            Region prior = g.Clip;
            g.SetClip(_live, CombineMode.Intersect);
            using (Brush back = new SolidBrush(_canvas))
                g.FillRectangle(back, _live);

            int c0 = Index(_live.Left), c1 = Index(_live.Right) + 1;
            int r0 = Index(_live.Top), r1 = Index(_live.Bottom) + 1;
            if (c0 < 0) c0 = 0;
            if (r0 < 0) r0 = 0;
            if (c1 > _cols) c1 = _cols;
            if (r1 > _rows) r1 = _rows;

            float px = _cursor.X, py = _cursor.Y;
            for (int row = r0; row < r1; row++)
                for (int col = c0; col < c1; col++)
                {
                    Dot d = _dots[row * _cols + col];

                    Color ink = _base;
                    if (_engaged)
                    {
                        float dx = d.Cx - px, dy = d.Cy - py;
                        float distSq = dx * dx + dy * dy;
                        if (distSq < Proximity * Proximity)
                            ink = Mix(_base, _active, Falloff((float)Math.Sqrt(distSq)));
                    }

                    using (Brush b = new SolidBrush(ink))
                        g.FillEllipse(b, d.Cx + d.Ox - _diameter / 2f,
                                         d.Cy + d.Oy - _diameter / 2f, _diameter, _diameter);
                }

            g.Clip = prior;
        }

        /// <summary>
        /// Strength of the cursor at a given distance: 1 underneath it, easing
        /// out to 0 at <see cref="Proximity"/>. Drives displacement and tint
        /// together so a dot that has moved is also the one that has lit up.
        /// </summary>
        private static float Falloff(float dist)
        {
            float t = 1f - dist / Proximity;
            if (t <= 0f) return 0f;
            return t * (2f - t);
        }

        private int Index(float coordinate)
        {
            return (int)Math.Floor((coordinate - _tile / 2f) / _tile);
        }

        private static Color Mix(Color from, Color to, float t)
        {
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t));
        }

        public void Dispose()
        {
            if (_rest != null) _rest.Dispose();
        }
    }
}
