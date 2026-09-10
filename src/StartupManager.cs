using System;
using Microsoft.Win32;

namespace Tsuru
{
    /// <summary>Per-user "launch at sign-in" entry. No elevation required.</summary>
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Tsuru";

        /// <summary>
        /// Appended to the run-at-sign-in entry so the app can tell a boot from
        /// the user launching it. Owned here because this is what writes it.
        /// </summary>
        public const string AutostartSwitch = "--autostart";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue(ValueName);
                    return value != null && value.ToString().Length > 0;
                }
            }
            catch (Exception ex)
            {
                Log.Write("StartupManager.IsEnabled failed: " + ex.Message);
                return false;
            }
        }

        private static string EntryValue()
        {
            return "\"" + System.Windows.Forms.Application.ExecutablePath + "\" " + AutostartSwitch;
        }

        /// <summary>
        /// Rewrites a startup entry that predates the --autostart switch, so an
        /// existing install stops opening the welcome screen at every sign-in.
        /// </summary>
        public static void NormaliseEntry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    object value = key.GetValue(ValueName);
                    if (value == null) return;

                    string current = value.ToString();
                    if (current.IndexOf(AutostartSwitch, StringComparison.OrdinalIgnoreCase) >= 0)
                        return;

                    key.SetValue(ValueName, EntryValue());
                    Log.Write("Updated the startup entry to carry " + AutostartSwitch + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Write("StartupManager.NormaliseEntry failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Replaces a run-at-sign-in entry left by an earlier name of the app.
        /// The old entry points at an executable that no longer exists, so it is
        /// removed and, if it was set, re-registered against this one.
        /// </summary>
        public static void AdoptLegacyEntry(string legacyValueName)
        {
            if (legacyValueName == ValueName) return;

            try
            {
                bool wasEnabled;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (key.GetValue(legacyValueName) == null) return;

                    wasEnabled = true;
                    key.DeleteValue(legacyValueName, false);
                }

                if (wasEnabled)
                {
                    SetEnabled(true);
                    Log.Write("Moved the startup entry from " + legacyValueName + " to " + ValueName + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Write("Startup entry migration failed: " + ex.Message);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enabled)
                        key.SetValue(ValueName, EntryValue());
                    else if (key.GetValue(ValueName) != null)
                        key.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex)
            {
                Log.Write("StartupManager.SetEnabled failed: " + ex.Message);
            }
        }
    }
}
