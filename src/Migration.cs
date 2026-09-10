using System;
using System.IO;

namespace Tsuru
{
    /// <summary>
    /// Carries settings forward from the app's former name.
    ///
    /// The rename moved the config folder and the run-at-sign-in entry, so
    /// without this an existing install would silently come back with defaults
    /// and a stale startup entry pointing at an executable that no longer
    /// exists. Runs once at start-up and is a no-op afterwards.
    /// </summary>
    internal static class Migration
    {
        private const string LegacyFolder = "ScrollSmooth";
        private const string LegacyRunValue = "ScrollSmooth";

        public static void Run()
        {
            MoveSettings();
            StartupManager.AdoptLegacyEntry(LegacyRunValue);
            StartupManager.NormaliseEntry();
        }

        private static void MoveSettings()
        {
            try
            {
                string legacy = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    LegacyFolder);

                if (!Directory.Exists(legacy)) return;

                // Anything already written under the new name wins; this only
                // fills a gap, so re-running can never clobber current settings.
                if (File.Exists(Settings.ConfigPath)) return;

                string legacyConfig = Path.Combine(legacy, "settings.ini");
                if (!File.Exists(legacyConfig)) return;

                Directory.CreateDirectory(Settings.ConfigDirectory);
                File.Copy(legacyConfig, Settings.ConfigPath, false);

                // The old folder is left in place rather than deleted: it costs
                // nothing, and removing a user's data on an upgrade is a poor
                // trade for tidiness.
                Log.Write("Adopted settings from the previous " + LegacyFolder + " folder.");
            }
            catch (Exception ex)
            {
                Log.Write("Settings migration failed: " + ex.Message);
            }
        }
    }
}
