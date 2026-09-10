using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace Tsuru
{
    /// <summary>
    /// Resolves display fonts with a graceful ladder, because the machine this
    /// runs on is not necessarily the machine it was designed on:
    ///
    ///   1. a font file bundled in <c>fonts\</c> beside the executable
    ///   2. the same family installed system-wide
    ///   3. a sensible system fallback
    ///
    /// GDI+ silently substitutes a default face for an unknown family rather
    /// than failing, so a requested family is only trusted when the resolved
    /// font reports the name that was asked for.
    /// </summary>
    internal static class FontLoader
    {
        // Must outlive every Font created from it, hence static.
        private static readonly PrivateFontCollection Bundled = new PrivateFontCollection();
        private static readonly Dictionary<string, FontFamily> BundledFamilies =
            new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);

        static FontLoader()
        {
            try
            {
                string dir = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "fonts");
                if (!Directory.Exists(dir)) return;

                foreach (string file in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".ttf" && ext != ".otf") continue;
                    try { Bundled.AddFontFile(file); }
                    catch (Exception ex) { Log.Write("Could not load font " + file + ": " + ex.Message); }
                }

                foreach (FontFamily family in Bundled.Families)
                    BundledFamilies[family.Name] = family;
            }
            catch (Exception ex)
            {
                Log.Write("Bundled font scan failed: " + ex.Message);
            }
        }

        /// <summary>True when <paramref name="family"/> can actually be rendered.</summary>
        public static bool IsAvailable(string family)
        {
            if (BundledFamilies.ContainsKey(family)) return true;
            using (Font probe = new Font(family, 12f))
                return string.Equals(probe.Name, family, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The first of <paramref name="families"/> that is actually present.
        /// Lets the design name its preferred face while still rendering
        /// sensibly on a machine that has none of them.
        /// </summary>
        public static string FirstAvailable(params string[] families)
        {
            foreach (string family in families)
            {
                try { if (IsAvailable(family)) return family; }
                catch { }
            }
            return families.Length > 0 ? families[families.Length - 1] : "Segoe UI";
        }

        /// <summary>
        /// The requested family at the given size, or <paramref name="fallback"/>
        /// if it is not present. Never throws and never returns null.
        /// </summary>
        public static Font Get(string family, float size, FontStyle style, string fallback)
        {
            try
            {
                FontFamily bundled;
                if (BundledFamilies.TryGetValue(family, out bundled))
                    return new Font(bundled, size, SupportedStyle(bundled, style));

                Font candidate = new Font(family, size, style);
                if (string.Equals(candidate.Name, family, StringComparison.OrdinalIgnoreCase))
                    return candidate;
                candidate.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("Font '" + family + "' unavailable: " + ex.Message);
            }

            try { return new Font(fallback, size, style); }
            catch { return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style); }
        }

        /// <summary>
        /// A bundled face may only ship one weight; asking for a style it does
        /// not have throws, so fall back to whatever it does support.
        /// </summary>
        private static FontStyle SupportedStyle(FontFamily family, FontStyle wanted)
        {
            if (family.IsStyleAvailable(wanted)) return wanted;
            if (family.IsStyleAvailable(FontStyle.Regular)) return FontStyle.Regular;
            if (family.IsStyleAvailable(FontStyle.Bold)) return FontStyle.Bold;
            return wanted;
        }
    }
}
