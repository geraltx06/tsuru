using System;
using System.Collections.Generic;
using System.Drawing;

namespace Tsuru
{
    /// <summary>
    /// Palette, faces and metrics for the dark settings design.
    ///
    /// Sizes are in pixels and fonts are built with
    /// <see cref="GraphicsUnit.Pixel"/>, because the whole window is laid out
    /// on a fixed pixel grid transcribed from the design frame - a point size
    /// would drift against those coordinates on any display that is not at 96
    /// dpi.
    /// </summary>
    internal static class Theme
    {
        // ---------------------------------------------------------- palette
        public static readonly Color Canvas = Color.FromArgb(0x0D, 0x0D, 0x0D);
        public static readonly Color Paper = Color.FromArgb(0xFF, 0xFF, 0xFF);

        /// <summary>Text on a white face.</summary>
        public static readonly Color Ink = Color.FromArgb(0x0D, 0x0D, 0x0D);

        /// <summary>Section headings and anything secondary.</summary>
        public static readonly Color Dim = Color.FromArgb(0x6A, 0x6A, 0x6A);

        /// <summary>Unfilled slider segments and the off state of a switch.</summary>
        public static readonly Color Track = Color.FromArgb(0x3A, 0x3A, 0x3A);

        /// <summary>Hairline between sections.</summary>
        public static readonly Color Rule = Color.FromArgb(0x23, 0x23, 0x23);

        public static readonly Color Mint = Color.FromArgb(0xAF, 0xFF, 0xD1);
        public static readonly Color Peach = Color.FromArgb(0xFF, 0xC5, 0xAF);
        public static readonly Color Violet = Color.FromArgb(0x7E, 0x79, 0xFF);

        // ----------------------------------------------------------- metrics
        /// <summary>Side gutter, and the width the content is laid out inside.</summary>
        public const int Gutter = 28;

        /// <summary>Centre-to-centre spacing of the slider segments.</summary>
        public const int SegmentPitch = 8;

        /// <summary>Painted width of one segment; the rest of the pitch is the gap.</summary>
        public const int SegmentWidth = 5;

        /// <summary>Height of a slider bar.</summary>
        public const int BarHeight = 32;

        /// <summary>Offset of the hard shadow under a button.</summary>
        public const int ShadowOffset = 5;

        // ------------------------------------------------------------- faces
        private static readonly Dictionary<string, Font> Cache = new Dictionary<string, Font>();

        /// <summary>The display serif the wordmark and the page title are set in.</summary>
        public static Font Display(float px)
        {
            return Resolve("display", px, FontStyle.Regular,
                FontLoader.FirstAvailable("Monograph", "Georgia", "Times New Roman"), "Georgia");
        }

        /// <summary>Body face - row labels, values, button captions.</summary>
        public static Font Sans(float px)
        {
            return Resolve("sans", px, FontStyle.Regular,
                FontLoader.FirstAvailable("Instrument Sans", "Inter", "Segoe UI"), "Segoe UI");
        }

        /// <summary>The same face a step heavier, for anything that has to hold its own.</summary>
        public static Font SansSemi(float px)
        {
            return Resolve("semi", px, FontStyle.Regular,
                FontLoader.FirstAvailable("Instrument Sans SemiBold", "Instrument Sans",
                                          "Segoe UI Semibold", "Segoe UI"), "Segoe UI");
        }

        /// <summary>
        /// Fonts are cached and never handed out for disposal: the window builds
        /// dozens of controls that want the same handful of sizes, and they live
        /// exactly as long as the process does.
        /// </summary>
        private static Font Resolve(string slot, float px, FontStyle style, string family, string fallback)
        {
            string key = slot + ":" + px.ToString("0.##") + ":" + (int)style;

            Font found;
            if (Cache.TryGetValue(key, out found)) return found;

            Font built;
            // Built from the family rather than the name, so a face bundled in
            // fonts\ - which is not installed and cannot be found by name -
            // still resolves.
            using (Font probe = FontLoader.Get(family, 12f, style, fallback))
                built = new Font(probe.FontFamily, px, probe.Style, GraphicsUnit.Pixel);

            Cache[key] = built;
            return built;
        }
    }
}
