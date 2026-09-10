using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tsuru
{
    /// <summary>
    /// The application itself: a tray icon, its menu, and the lifetime of the
    /// scroll engine. There is no main window - the settings window is created
    /// on demand.
    /// </summary>
    public class TrayContext : ApplicationContext
    {
        /// <summary>
        /// Signalled by a second launch of the executable. Rather than nagging
        /// with a dialog, running it again brings up Settings on the instance
        /// that is already here.
        /// </summary>
        public const string ActivationEventName = @"Local\Tsuru.ShowSettings.9F2A1C";

        /// <summary>
        /// Signalled by the installer or uninstaller to ask for a clean exit.
        /// Terminating the process instead would skip tray-icon removal and
        /// leave a phantom icon behind until the user hovers over it.
        /// </summary>
        public const string QuitEventName = @"Local\Tsuru.Quit.9F2A1C";

        private readonly Settings _settings;
        private readonly ScrollEngine _engine;

        private readonly EventWaitHandle _activation;
        private readonly EventWaitHandle _quit;
        private Thread _activationThread;
        private volatile bool _listening;

        private NotifyIcon _tray;
        private Icon _iconOn;
        private Icon _iconOff;
        private ToolStripMenuItem _enabledItem;
        private ToolStripMenuItem _startupItem;
        private SettingsForm _settingsForm;
        private WelcomeForm _welcomeForm;

        /// <summary>Marshals SystemEvents callbacks, which arrive on their own thread, onto the UI thread.</summary>
        private Control _marshal;
        private bool _lightTheme;

        public TrayContext(EventWaitHandle activation, EventWaitHandle quit, bool autostart)
        {
            _activation = activation;
            _quit = quit;
            _settings = Settings.Load();

            // The registry is the source of truth for the startup entry; the
            // config file only mirrors it for display.
            _settings.StartWithWindows = StartupManager.IsEnabled();

            // Until onboarding has been completed, smoothing waits for the
            // welcome screen to hand over so Start has something to switch on.
            // Keyed on the flag rather than on the config file existing, so a
            // welcome dismissed without starting is offered again next launch.
            bool needsWelcome = !_settings.OnboardingComplete;
            if (needsWelcome) _settings.Enabled = false;

            _engine = new ScrollEngine(_settings);

            // Materialise the file on first run so it is discoverable and
            // hand-editable rather than appearing only after a change.
            _settings.Save();

            BuildTray();

            bool ok = _engine.Start();
            UpdateTrayState();

            if (!ok)
            {
                _tray.ShowBalloonTip(8000, "Tsuru",
                    "Could not install the mouse hook. Smoothing is inactive - see " + Log.Path,
                    ToolTipIcon.Error);
            }
            else if (!autostart)
            {
                // The welcome screen is the app's front door on every manual
                // launch. Signing in is not a manual launch, so a boot does not
                // put a window in the user's face.
                ShowWelcome();
            }
        }

        // --------------------------------------------------------------- tray
        private void BuildTray()
        {
            _marshal = new Control();
            _marshal.CreateControl();

            _lightTheme = IconFactory.SystemUsesLightTheme();
            RebuildIcons();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            StartActivationListener();

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            _enabledItem = new ToolStripMenuItem("Smooth scrolling");
            _enabledItem.CheckOnClick = true;
            _enabledItem.Click += delegate { SetEnabled(_enabledItem.Checked); };
            menu.Items.Add(_enabledItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem settings = new ToolStripMenuItem("Settings...");
            settings.Click += delegate { ShowSettings(); };
            menu.Items.Add(settings);

            _startupItem = new ToolStripMenuItem("Start with Windows");
            _startupItem.CheckOnClick = true;
            _startupItem.Click += delegate
            {
                _settings.StartWithWindows = _startupItem.Checked;
                StartupManager.SetEnabled(_startupItem.Checked);
                _settings.Save();
            };
            menu.Items.Add(_startupItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem welcome = new ToolStripMenuItem("Welcome screen");
            welcome.Click += delegate { ShowWelcome(); };
            menu.Items.Add(welcome);

            ToolStripMenuItem about = new ToolStripMenuItem("About");
            about.Click += delegate { ShowAbout(); };
            menu.Items.Add(about);

            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { ExitApp(); };
            menu.Items.Add(exit);

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowSettings(); };
        }

        /// <summary>
        /// Waits for another launch of the executable to ask for the settings
        /// window. A short poll rather than a blocking wait so shutdown is clean.
        /// </summary>
        private void StartActivationListener()
        {
            if (_activation == null && _quit == null) return;

            // Index 0 asks for Settings, index 1 asks the app to shut down.
            List<WaitHandle> signals = new List<WaitHandle>();
            int activationIndex = -1, quitIndex = -1;
            if (_activation != null) { activationIndex = signals.Count; signals.Add(_activation); }
            if (_quit != null) { quitIndex = signals.Count; signals.Add(_quit); }
            WaitHandle[] handles = signals.ToArray();

            _listening = true;
            _activationThread = new Thread(delegate()
            {
                while (_listening)
                {
                    try
                    {
                        int index = WaitHandle.WaitAny(handles, 400);
                        if (index == WaitHandle.WaitTimeout) continue;
                        if (!_listening) break;
                        if (_marshal == null || !_marshal.IsHandleCreated) continue;

                        if (index == quitIndex)
                            _marshal.BeginInvoke((MethodInvoker)delegate { ExitApp(); });
                        else if (index == activationIndex)
                            _marshal.BeginInvoke((MethodInvoker)delegate { ShowWelcome(); });
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Activation wait failed: " + ex.Message);
                        return;
                    }
                }
            });
            _activationThread.IsBackground = true;
            _activationThread.Name = "Tsuru.Activation";
            _activationThread.Start();
        }

        /// <summary>
        /// Windows does not recolour notification icons, so the glyph is redrawn
        /// whenever the user flips between the light and dark taskbar.
        /// </summary>
        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.VisualStyle) return;

            if (_marshal == null || !_marshal.IsHandleCreated) return;

            _marshal.BeginInvoke((MethodInvoker)delegate
            {
                bool light = IconFactory.SystemUsesLightTheme();
                if (light == _lightTheme) return;
                _lightTheme = light;
                RebuildIcons();
                UpdateTrayState();
            });
        }

        private void RebuildIcons()
        {
            Icon oldOn = _iconOn;
            Icon oldOff = _iconOff;

            _iconOn = IconFactory.Create(true, 32);
            _iconOff = IconFactory.Create(false, 32);

            if (_tray != null) _tray.Icon = _settings.Enabled ? _iconOn : _iconOff;

            if (oldOn != null) oldOn.Dispose();
            if (oldOff != null) oldOff.Dispose();
        }

        private void UpdateTrayState()
        {
            bool on = _settings.Enabled;
            _tray.Icon = on ? _iconOn : _iconOff;
            _tray.Text = on ? "Tsuru - active" : "Tsuru - paused";
            _enabledItem.Checked = on;
            _startupItem.Checked = _settings.StartWithWindows;
        }

        private void SetEnabled(bool enabled)
        {
            _settings.Enabled = enabled;

            // Switching smoothing on is the act that completes onboarding,
            // whether it came from the welcome screen, the menu or Settings.
            if (enabled) _settings.OnboardingComplete = true;

            _engine.Settings = _settings;
            _settings.Save();
            UpdateTrayState();

            // Keep an open settings window from showing a stale master switch.
            if (_settingsForm != null && !_settingsForm.IsDisposed)
                _settingsForm.SyncEnabled(enabled);
        }

        // ------------------------------------------------------------ windows
        /// <summary>
        /// First-run hand-off: Start switches smoothing on and moves the user
        /// straight to Settings. Dismissing without starting leaves it paused,
        /// so say where the switch lives rather than leaving a dead-looking app.
        /// </summary>
        private void ShowWelcome()
        {
            if (_welcomeForm != null && !_welcomeForm.IsDisposed)
            {
                if (_welcomeForm.WindowState == FormWindowState.Minimized)
                    _welcomeForm.WindowState = FormWindowState.Normal;
                _welcomeForm.Reflect(_settings.Enabled);
                _welcomeForm.Activate();
                return;
            }

            _welcomeForm = new WelcomeForm(
                _settings.Enabled,
                delegate
                {
                    SetEnabled(true);
                    ShowSettings();
                },
                delegate { ShowSettings(); });

            _welcomeForm.FormClosed += delegate
            {
                bool started = _welcomeForm.Started;
                _welcomeForm = null;

                // Only nag when they backed out of onboarding, not when they
                // reopened the screen later out of curiosity.
                if (started || _settings.OnboardingComplete) return;
                _tray.ShowBalloonTip(7000, "Tsuru is paused",
                    "Turn smoothing on from the tray icon whenever you are ready.",
                    ToolTipIcon.Info);
            };

            _welcomeForm.Show();
            _welcomeForm.Activate();
        }

        private void ShowSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                if (_settingsForm.WindowState == FormWindowState.Minimized)
                    _settingsForm.WindowState = FormWindowState.Normal;
                _settingsForm.Activate();
                return;
            }

            // Live-applied edits arrive here as a fresh Settings instance, which
            // the engine picks up on its next frame without locking.
            _settingsForm = new SettingsForm(_settings, delegate(Settings updated)
            {
                CopyFeelInto(updated, _settings);
                _engine.Settings = _settings;
                UpdateTrayState();
            });

            _settingsForm.FormClosed += delegate { _settingsForm = null; };
            _settingsForm.Show();
            _settingsForm.Activate();
        }

        /// <summary>Copies everything the settings window can edit, master switch included.</summary>
        private static void CopyFeelInto(Settings from, Settings into)
        {
            into.Enabled = from.Enabled;
            into.StepSize = from.StepSize;
            into.AnimationTime = from.AnimationTime;
            into.PulseScale = from.PulseScale;
            into.PulseAlgorithm = from.PulseAlgorithm;
            into.AccelDelta = from.AccelDelta;
            into.AccelMax = from.AccelMax;
            into.FrameRate = from.FrameRate;
            into.SmoothVertical = from.SmoothVertical;
            into.SmoothHorizontal = from.SmoothHorizontal;
            into.InvertVertical = from.InvertVertical;
            into.InvertHorizontal = from.InvertHorizontal;
            into.IgnoreInjected = from.IgnoreInjected;
            into.IgnorePrecisionDevices = from.IgnorePrecisionDevices;
            into.StartWithWindows = from.StartWithWindows;
            into.Excluded = new System.Collections.Generic.List<string>(from.Excluded);
        }

        private void ShowAbout()
        {
            string text =
                "Tsuru" + Environment.NewLine +
                "Smooth mouse wheel scrolling for Windows." + Environment.NewLine + Environment.NewLine +
                "Hook: " + (_engine.IsHookInstalled ? "installed" : "NOT installed") + Environment.NewLine +
                "Notches smoothed this session: " + _engine.HandledCount + Environment.NewLine + Environment.NewLine +
                "Settings file:" + Environment.NewLine + Settings.ConfigPath + Environment.NewLine + Environment.NewLine +
                "Note: to smooth scrolling inside apps that run as administrator, " +
                "Tsuru has to be running as administrator too.";

            MessageBox.Show(text, "About Tsuru", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExitApp()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            _listening = false;
            if (_activation != null || _quit != null)
            {
                // Nudge the waiter so shutdown does not sit out the poll interval.
                if (_activation != null) _activation.Set();
                if (_activationThread != null) _activationThread.Join(800);
                if (_activation != null) _activation.Close();
                if (_quit != null) _quit.Close();
            }

            if (_welcomeForm != null && !_welcomeForm.IsDisposed) _welcomeForm.Close();
            if (_settingsForm != null && !_settingsForm.IsDisposed) _settingsForm.Close();

            _settings.Save();
            _engine.Dispose();

            _tray.Visible = false;
            _tray.Dispose();
            if (_iconOn != null) _iconOn.Dispose();
            if (_iconOff != null) _iconOff.Dispose();
            if (_marshal != null) _marshal.Dispose();

            ExitThread();
        }
    }
}
