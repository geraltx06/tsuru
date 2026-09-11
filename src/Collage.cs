using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace Tsuru
{
    /// <summary>
    /// The cut-out collage along the bottom edge, composited from its separate
    /// pieces so each one can answer the cursor on its own: the piece under the
    /// pointer rises and grows slightly, and settles back when the pointer
    /// leaves.
    ///
    /// Layout comes from the design rather than from anything measurable at
    /// runtime, so the table below records where each cut-out sits inside the
    /// 4200x2181 collage export, in that export's own pixels. Positions were
    /// recovered by matching each piece against the flat collage and confirmed
    /// by recompositing: 99.7% of the original is covered, with the remainder
    /// accounted for by antialiasing along the cut edges.
    ///
    /// Order is back to front. It was derived the same way - a piece that is
    /// largely hidden in the flat export must be behind the ones hiding it.
    /// </summary>
    internal sealed class Collage : IDisposable
    {
        /// <summary>Where one cut-out sits in the export. Fixed, and shared by every instance.</summary>
        private sealed class Spec
        {
            public readonly string File;
            public readonly float X, Y, W, H;   // in collage-export pixels

            public Spec(string file, float x, float y, float w, float h)
            {
                File = file; X = x; Y = y; W = w; H = h;
            }
        }

        /// <summary>One loaded cut-out and its live animation state.</summary>
        private sealed class Piece
        {
            public Bitmap Art;         // scaled to the size it is drawn at
            public RectangleF Rest;    // where that lands on the canvas
            public float Lift;         // 0 at rest, 1 fully raised
        }

        /// <summary>Dimensions of the collage export the table is expressed in.</summary>
        private const float SourceWidth = 4200f;
        private const float SourceHeight = 2181f;

        /// <summary>How far a raised piece rises, as a fraction of canvas width.</summary>
        private const float RiseFraction = 0.0125f;

        /// <summary>And how much larger it gets while raised.</summary>
        private const float GrowScale = 0.05f;

        /// <summary>
        /// How quickly the raise closes on its target, per second.
        ///
        /// An exponential approach rather than a spring, and deliberately so: a
        /// spring loose enough to feel soft also overshoots, and on a movement
        /// this small the overshoot does not read as bounce - it reads as the
        /// picture shaking. This eases in and settles, and never passes 1.
        /// </summary>
        private const float LiftRate = 11f;

        private const float Settled = 0.002f;

        private static readonly Spec[] Table = new Spec[]
        {
            new Spec("36 1.png",      1358f, 1169f, 1542f,  870f),
            new Spec("gameboy.png",    189f,  510f, 1922f, 1671f),
            new Spec("rio.png",          0f,    0f, 1458f, 2181f),
            new Spec("lamp.png",      3426f,  936f,  774f, 1245f),
            new Spec("reasy.png",     2259f,  504f, 1941f, 1677f),
            new Spec("shashank.png",  1362f,  771f, 1368f,  858f),
            new Spec("m.png",         2454f, 1455f,  990f,  726f),
            new Spec("a.png",         1746f, 1291f, 1124f,  891f),
            new Spec("ain.png",          0f,  393f, 1035f, 1788f),
            new Spec("enma.png",      2592f,  885f, 1608f, 1296f),
            new Spec("dorawemon.png",  726f, 1371f, 1308f,  810f),
        };

        private readonly Piece[] _pieces;
        private readonly float _rise;
        private Rectangle _live;
        private int _hovered = -1;

        /// <summary>
        /// Loads whatever pieces are present. A missing folder is normal - the
        /// screen simply draws without the collage, as it did before the
        /// artwork existed.
        /// </summary>
        public Collage(int canvasWidth, int canvasHeight)
        {
            _rise = RiseFraction * canvasWidth;

            float k = canvasWidth / SourceWidth;
            float top = canvasHeight - SourceHeight * k;

            System.Collections.Generic.List<Piece> loaded =
                new System.Collections.Generic.List<Piece>();

            foreach (Spec spec in Table)
            {
                int w = (int)Math.Ceiling(spec.W * k);
                int h = (int)Math.Ceiling(spec.H * k);

                Bitmap art = Load(spec.File, w, h);
                if (art == null) continue;

                Piece piece = new Piece();
                piece.Art = art;
                // Snapped to whole pixels and sized from the bitmap that was
                // actually produced, so the resting draw is a plain blit with
                // no resampling and no half-pixel shift when a raise ends.
                piece.Rest = new RectangleF((float)Math.Round(spec.X * k),
                                            (float)Math.Round(top + spec.Y * k), w, h);
                loaded.Add(piece);
            }

            _pieces = loaded.ToArray();
        }

        public bool IsEmpty { get { return _pieces.Length == 0; } }

        /// <summary>The region currently displaced by a hover.</summary>
        public Rectangle LiveBounds { get { return _live; } }

        /// <summary>
        /// Scales a shipped cut-out to the size it is actually drawn at, once,
        /// so each frame is a straight blit. The full-size bitmap is released -
        /// hit testing reads the scaled copy, whose alpha is the same shape.
        /// </summary>
        private static Bitmap Load(string fileName, int width, int height)
        {
            if (width < 1 || height < 1) return null;
            try
            {
                string path = Path.Combine(Path.Combine(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "assets"), "collage"), fileName);
                if (!File.Exists(path)) return null;

                using (Image source = Image.FromFile(path))
                {
                    Bitmap scaled = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(scaled))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(source, new Rectangle(0, 0, width, height));
                    }
                    return scaled;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Collage piece '" + fileName + "' could not be loaded: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Points the collage at the cursor. Hit testing walks front to back and
        /// reads the artwork's alpha, so the cut-out shape is what responds
        /// rather than its bounding box - the gaps between pieces stay inert.
        /// </summary>
        public void SetCursor(PointF p, bool inside)
        {
            int hit = -1;
            if (inside)
            {
                for (int i = _pieces.Length - 1; i >= 0 && hit < 0; i--)
                {
                    Piece piece = _pieces[i];
                    if (!piece.Rest.Contains(p)) continue;

                    int tx = (int)((p.X - piece.Rest.X) / piece.Rest.Width * piece.Art.Width);
                    int ty = (int)((p.Y - piece.Rest.Y) / piece.Rest.Height * piece.Art.Height);
                    if (tx < 0 || ty < 0 || tx >= piece.Art.Width || ty >= piece.Art.Height) continue;

                    if (piece.Art.GetPixel(tx, ty).A > 40) hit = i;
                }
            }
            _hovered = hit;
        }

        /// <summary>
        /// Advances the rise of every piece. Returns false once they have all
        /// settled, so the caller can stop animating.
        /// </summary>
        public bool Step(float dt)
        {
            bool moving = false;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < _pieces.Length; i++)
            {
                Piece piece = _pieces[i];
                float target = i == _hovered ? 1f : 0f;
                float delta = target - piece.Lift;

                if (Math.Abs(delta) < Settled)
                {
                    piece.Lift = target;
                    if (piece.Lift == 0f) continue;
                }
                else
                {
                    // Framing the step as a fraction of the remaining distance
                    // keeps the motion identical whatever the frame interval.
                    piece.Lift += delta * (1f - (float)Math.Exp(-LiftRate * dt));
                    moving = true;
                }

                RectangleF r = Raised(piece);
                if (r.Left < minX) minX = r.Left;
                if (r.Top < minY) minY = r.Top;
                if (r.Right > maxX) maxX = r.Right;
                if (r.Bottom > maxY) maxY = r.Bottom;
            }

            _live = minX > maxX
                ? Rectangle.Empty
                : Rectangle.FromLTRB((int)minX - 2, (int)minY - 2, (int)maxX + 2, (int)maxY + 2);

            return moving;
        }

        private RectangleF Raised(Piece piece)
        {
            if (piece.Lift == 0f) return piece.Rest;

            float grow = 1f + GrowScale * piece.Lift;
            float w = piece.Rest.Width * grow, h = piece.Rest.Height * grow;
            return new RectangleF(
                piece.Rest.X - (w - piece.Rest.Width) / 2f,
                piece.Rest.Y - (h - piece.Rest.Height) / 2f - _rise * piece.Lift,
                w, h);
        }

        /// <summary>
        /// Draws the collage strictly back to front, in the design's own
        /// stacking order. A raised piece keeps its place in that order rather
        /// than jumping to the front - re-stacking on hover is a far louder
        /// change than the raise itself, and reads as a glitch.
        /// </summary>
        public void Draw(Graphics g)
        {
            for (int i = 0; i < _pieces.Length; i++)
            {
                Piece piece = _pieces[i];

                // Rest is integer-aligned, so at rest this lands on exactly the
                // pixels the scaled bitmap was prepared for. Anything raised is
                // drawn through the resampler instead, which is what lets it
                // move by fractions of a pixel without stepping.
                if (piece.Lift == 0f)
                    g.DrawImageUnscaled(piece.Art, (int)piece.Rest.X, (int)piece.Rest.Y);
                else
                    g.DrawImage(piece.Art, Raised(piece));
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _pieces.Length; i++)
                if (_pieces[i].Art != null) _pieces[i].Art.Dispose();
        }
    }
}
