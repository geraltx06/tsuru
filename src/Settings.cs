using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tsuru
{
    /// <summary>
    /// User configuration. Persisted as a plain key=value file so it can be
    /// hand-edited or diffed without any serializer dependency.
    /// </summary>
    public class Settings
    {
        // --- master ---
        public bool Enabled = true;

        // --- feel: these mirror the smoothscroll.net option names ---
        /// <summary>Wheel units travelled per physical notch. 120 == the distance Windows scrolls natively.</summary>
        public double StepSize = 120;
        /// <summary>Duration in ms over which one notch is played out.</summary>
        public double AnimationTime = 400;
        /// <summary>Higher == the scroll settles more sharply at the end.</summary>
        public double PulseScale = 8;
        /// <summary>Use the exponential pulse curve. When false, motion is linear.</summary>
        public bool PulseAlgorithm = true;

        // --- acceleration: consecutive notches inside AccelDelta ms compound ---
        /// <summary>Window in ms within which successive notches accelerate. 0 disables.</summary>
        public double AccelDelta = 50;
        /// <summary>Upper bound on the acceleration multiplier.</summary>
        public double AccelMax = 3.0;

        // --- delivery ---
        /// <summary>How many times per second synthetic wheel events are emitted.</summary>
        public int FrameRate = 120;

        // --- axes ---
        public bool SmoothVertical = true;
        public bool SmoothHorizontal = true;
        public bool InvertVertical = false;
        public bool InvertHorizontal = false;

        // --- compatibility ---
        /// <summary>Pass through events injected by other software (prevents feedback loops).</summary>
        public bool IgnoreInjected = true;
        /// <summary>Pass through deltas that are not multiples of 120 - precision touchpads are already smooth.</summary>
        public bool IgnorePrecisionDevices = true;
        /// <summary>Executable names (e.g. "game.exe") that keep native scrolling.</summary>
        public List<string> Excluded = new List<string>();

        // --- shell ---
        public bool StartWithWindows = false;

        /// <summary>
        /// Set once the user has actually switched smoothing on, by any route.
        /// Until then the welcome screen keeps offering to do it, so an app
        /// dismissed mid-onboarding does not sit paused and unexplained.
        /// </summary>
        public bool OnboardingComplete = false;

        public static string ConfigDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Tsuru");
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, "settings.ini"); }
        }

        public Settings Clone()
        {
            Settings copy = (Settings)MemberwiseClone();
            copy.Excluded = new List<string>(Excluded);
            return copy;
        }

        // -------------------------------------------------------------- load
        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(ConfigPath)) return s;
                foreach (string raw in File.ReadAllLines(ConfigPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    s.Apply(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
                }
            }
            catch (Exception ex)
            {
                Log.Write("Settings.Load failed: " + ex.Message);
            }
            return s;
        }

        private void Apply(string key, string val)
        {
            switch (key)
            {
                case "Enabled": Enabled = ParseBool(val, Enabled); break;
                case "StepSize": StepSize = ParseNum(val, StepSize); break;
                case "AnimationTime": AnimationTime = ParseNum(val, AnimationTime); break;
                case "PulseScale": PulseScale = ParseNum(val, PulseScale); break;
                case "PulseAlgorithm": PulseAlgorithm = ParseBool(val, PulseAlgorithm); break;
                case "AccelDelta": AccelDelta = ParseNum(val, AccelDelta); break;
                case "AccelMax": AccelMax = ParseNum(val, AccelMax); break;
                case "FrameRate": FrameRate = (int)ParseNum(val, FrameRate); break;
                case "SmoothVertical": SmoothVertical = ParseBool(val, SmoothVertical); break;
                case "SmoothHorizontal": SmoothHorizontal = ParseBool(val, SmoothHorizontal); break;
                case "InvertVertical": InvertVertical = ParseBool(val, InvertVertical); break;
                case "InvertHorizontal": InvertHorizontal = ParseBool(val, InvertHorizontal); break;
                case "IgnoreInjected": IgnoreInjected = ParseBool(val, IgnoreInjected); break;
                case "IgnorePrecisionDevices": IgnorePrecisionDevices = ParseBool(val, IgnorePrecisionDevices); break;
                case "StartWithWindows": StartWithWindows = ParseBool(val, StartWithWindows); break;
                case "OnboardingComplete": OnboardingComplete = ParseBool(val, OnboardingComplete); break;
                case "Excluded":
                    Excluded.Clear();
                    foreach (string part in val.Split(';'))
                    {
                        string p = part.Trim();
                        if (p.Length > 0) Excluded.Add(p);
                    }
                    break;
            }
        }

        private static bool ParseBool(string v, bool fallback)
        {
            bool b;
            return bool.TryParse(v, out b) ? b : fallback;
        }

        private static double ParseNum(string v, double fallback)
        {
            double d;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : fallback;
        }

        private static string Num(double d)
        {
            return d.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // -------------------------------------------------------------- save
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Tsuru settings - edit while the app is closed, or use the tray UI.");
                sb.AppendLine("Enabled=" + Enabled);
                sb.AppendLine();
                sb.AppendLine("# Feel");
                sb.AppendLine("StepSize=" + Num(StepSize));
                sb.AppendLine("AnimationTime=" + Num(AnimationTime));
                sb.AppendLine("PulseScale=" + Num(PulseScale));
                sb.AppendLine("PulseAlgorithm=" + PulseAlgorithm);
                sb.AppendLine("AccelDelta=" + Num(AccelDelta));
                sb.AppendLine("AccelMax=" + Num(AccelMax));
                sb.AppendLine("FrameRate=" + FrameRate);
                sb.AppendLine();
                sb.AppendLine("# Axes");
                sb.AppendLine("SmoothVertical=" + SmoothVertical);
                sb.AppendLine("SmoothHorizontal=" + SmoothHorizontal);
                sb.AppendLine("InvertVertical=" + InvertVertical);
                sb.AppendLine("InvertHorizontal=" + InvertHorizontal);
                sb.AppendLine();
                sb.AppendLine("# Compatibility");
                sb.AppendLine("IgnoreInjected=" + IgnoreInjected);
                sb.AppendLine("IgnorePrecisionDevices=" + IgnorePrecisionDevices);
                sb.AppendLine("Excluded=" + string.Join(";", Excluded.ToArray()));
                sb.AppendLine();
                sb.AppendLine("StartWithWindows=" + StartWithWindows);
                sb.AppendLine("OnboardingComplete=" + OnboardingComplete);
                File.WriteAllText(ConfigPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Write("Settings.Save failed: " + ex.Message);
            }
        }
    }
}
