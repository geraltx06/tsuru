using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Tsuru
{
    /// <summary>
    /// Live-applying settings window. Every change takes effect immediately so
    /// the feel can be judged by scrolling another window while it is open,
    /// which is why this is shown non-modally.
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly Settings _original;
        private readonly Action<Settings> _apply;
        private Settings _working;

        private readonly ToolTip _tips = new ToolTip();
        private Panel _host;
        private int _y;
        private bool _loading;

        private readonly List<Action> _refreshers = new List<Action>();

        // Absolute layout: Panel.Padding is unreliable once AutoScroll is on,
        // so the gutters are baked into these coordinates instead.
        private const int LabelX = 16;
        private const int LabelW = 150;
        private const int ControlX = 172;
        private const int ControlW = 196;
        private const int ValueX = 376;
        private const int ValueW = 62;
        private const int FullW = 422;
        private const int RowH = 30;
        private const int TopMargin = 14;

        public SettingsForm(Settings current, Action<Settings> apply)
        {
            _original = current.Clone();
            _working = current.Clone();
            _apply = apply;

            BuildForm();
            BuildContent();
            Reload();
        }

        private void BuildForm()
        {
            Text = "Tsuru Settings";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(478, 620);
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowInTaskbar = true;
            Icon = IconFactory.Create(true, 32);

            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.AutoScroll = true;
            Controls.Add(_host);

            _y = TopMargin;

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 52;
            Controls.Add(footer);

            Button close = new Button();
            close.Text = "Close";
            close.Size = new Size(90, 28);
            close.Location = new Point(ClientSize.Width - 106, 12);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate { Close(); };
            footer.Controls.Add(close);

            Button revert = new Button();
            revert.Text = "Revert";
            revert.Size = new Size(90, 28);
            revert.Location = new Point(ClientSize.Width - 202, 12);
            revert.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            revert.Click += delegate
            {
                _working = _original.Clone();
                Push();
                Reload();
            };
            footer.Controls.Add(revert);

            Button defaults = new Button();
            defaults.Text = "Reset to defaults";
            defaults.Size = new Size(126, 28);
            defaults.Location = new Point(16, 12);
            defaults.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            defaults.Click += delegate
            {
                Settings fresh = new Settings();
                // Keep the things that are not part of "feel".
                fresh.Enabled = _working.Enabled;
                fresh.StartWithWindows = _working.StartWithWindows;
                fresh.Excluded = new List<string>(_working.Excluded);
                _working = fresh;
                Push();
                Reload();
            };
            footer.Controls.Add(defaults);

            AcceptButton = close;
        }

        // ------------------------------------------------------------ content
        private void BuildContent()
        {
            MasterSwitch();

            Header("Feel");

            Slider("Step size", 30, 600, 1, "u",
                delegate { return _working.StepSize; },
                delegate(double v) { _working.StepSize = v; },
                "Distance covered by one wheel notch. 120 matches how far Windows scrolls on its own.");

            Slider("Animation time", 50, 1500, 1, "ms",
                delegate { return _working.AnimationTime; },
                delegate(double v) { _working.AnimationTime = v; },
                "How long a single notch takes to play out. Higher is floatier.");

            Slider("Smoothness", 1, 20, 1, "",
                delegate { return _working.PulseScale; },
                delegate(double v) { _working.PulseScale = v; },
                "Shape of the easing curve. Higher settles more sharply at the end.");

            Check("Use pulse easing",
                delegate { return _working.PulseAlgorithm; },
                delegate(bool v) { _working.PulseAlgorithm = v; },
                "Off gives plain linear motion instead of the eased curve.");

            Gap();
            Header("Acceleration");

            Slider("Acceleration window", 0, 200, 1, "ms",
                delegate { return _working.AccelDelta; },
                delegate(double v) { _working.AccelDelta = v; },
                "Notches arriving within this window compound. 0 turns acceleration off.");

            Slider("Maximum boost", 10, 100, 0.1, "x",
                delegate { return _working.AccelMax; },
                delegate(double v) { _working.AccelMax = v; },
                "Upper limit on how much a fast flick is multiplied.");

            Gap();
            Header("Axes");

            Check("Smooth vertical scrolling",
                delegate { return _working.SmoothVertical; },
                delegate(bool v) { _working.SmoothVertical = v; }, null);

            Check("Smooth horizontal scrolling",
                delegate { return _working.SmoothHorizontal; },
                delegate(bool v) { _working.SmoothHorizontal = v; },
                "Applies to tilt wheels and Shift+wheel in some apps.");

            Check("Invert vertical direction",
                delegate { return _working.InvertVertical; },
                delegate(bool v) { _working.InvertVertical = v; },
                "Natural / reversed scrolling.");

            Check("Invert horizontal direction",
                delegate { return _working.InvertHorizontal; },
                delegate(bool v) { _working.InvertHorizontal = v; }, null);

            Gap();
            Header("Compatibility");

            Slider("Update rate", 30, 240, 1, "fps",
                delegate { return _working.FrameRate; },
                delegate(double v) { _working.FrameRate = (int)Math.Round(v); },
                "How often motion is delivered. Match your monitor for the smoothest result.");

            Check("Leave precision touchpads alone",
                delegate { return _working.IgnorePrecisionDevices; },
                delegate(bool v) { _working.IgnorePrecisionDevices = v; },
                "Touchpads already scroll smoothly; re-animating them adds lag.");

            Check("Ignore scrolling from other apps",
                delegate { return _working.IgnoreInjected; },
                delegate(bool v) { _working.IgnoreInjected = v; },
                "Prevents fighting other automation tools that synthesise scroll events.");

            Gap();
            Header("Excluded apps");

            Label note = new Label();
            note.Text = "One executable name per line, e.g. game.exe. These keep native scrolling.";
            note.ForeColor = SystemColors.GrayText;
            note.Location = new Point(LabelX, _y);
            note.Size = new Size(FullW, 32);
            _host.Controls.Add(note);
            _y += 34;

            TextBox excluded = new TextBox();
            excluded.Multiline = true;
            excluded.ScrollBars = ScrollBars.Vertical;
            excluded.Location = new Point(LabelX, _y);
            excluded.Size = new Size(FullW, 96);
            excluded.TextChanged += delegate
            {
                if (_loading) return;
                List<string> list = new List<string>();
                foreach (string line in excluded.Lines)
                {
                    string t = line.Trim();
                    if (t.Length > 0) list.Add(t);
                }
                _working.Excluded = list;
                Push();
            };
            _host.Controls.Add(excluded);
            _refreshers.Add(delegate
            {
                excluded.Lines = _working.Excluded.ToArray();
            });
            _y += 92;

            Gap();
            Header("Startup");

            Check("Start Tsuru when I sign in",
                delegate { return _working.StartWithWindows; },
                delegate(bool v)
                {
                    _working.StartWithWindows = v;
                    StartupManager.SetEnabled(v);
                }, null);
        }

        /// <summary>
        /// The master on/off switch, deliberately the first thing in the panel
        /// and styled as a card rather than another checkbox: everything below
        /// it is inert while this is off.
        /// </summary>
        private void MasterSwitch()
        {
            Panel card = new Panel();
            card.Location = new Point(LabelX, _y);
            card.Size = new Size(FullW, 56);
            card.BackColor = SystemColors.ControlLightLight;
            card.BorderStyle = BorderStyle.FixedSingle;
            _host.Controls.Add(card);

            Label title = new Label();
            title.Text = "Smooth scrolling";
            title.Font = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold);
            title.Location = new Point(14, 9);
            title.Size = new Size(240, 22);
            card.Controls.Add(title);

            Label state = new Label();
            state.Location = new Point(14, 32);
            state.Size = new Size(260, 18);
            state.ForeColor = SystemColors.GrayText;
            card.Controls.Add(state);

            CheckBox toggle = new CheckBox();
            toggle.Appearance = Appearance.Button;
            toggle.TextAlign = ContentAlignment.MiddleCenter;
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.Size = new Size(86, 32);
            toggle.Location = new Point(FullW - 86 - 16, 11);
            toggle.Cursor = Cursors.Hand;
            toggle.BackColor = Color.FromArgb(232, 232, 232);
            toggle.FlatAppearance.CheckedBackColor = Color.FromArgb(0, 120, 215);
            toggle.FlatAppearance.BorderColor = Color.FromArgb(170, 170, 170);
            card.Controls.Add(toggle);

            MethodInvoker paint = delegate
            {
                bool on = toggle.Checked;
                toggle.Text = on ? "On" : "Off";
                toggle.ForeColor = on ? Color.White : Color.FromArgb(70, 70, 70);
                state.Text = on
                    ? "Your mouse wheel is being smoothed."
                    : "Paused - Windows is scrolling normally.";
            };

            toggle.CheckedChanged += delegate
            {
                paint();
                if (_loading) return;
                _working.Enabled = toggle.Checked;
                Push();
            };

            _tips.SetToolTip(toggle, "Turn all smoothing on or off. Same as the tray menu.");

            _refreshers.Add(delegate
            {
                toggle.Checked = _working.Enabled;
                paint();
            });

            _y += card.Height + 16;
        }

        /// <summary>
        /// Reflects a change made outside this window - the tray menu - so the
        /// two never disagree about whether smoothing is on.
        /// </summary>
        public void SyncEnabled(bool enabled)
        {
            if (_working.Enabled == enabled) return;
            _working.Enabled = enabled;
            Reload();
        }

        // ----------------------------------------------------------- builders
        private void Header(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font(Font, FontStyle.Bold);
            l.Location = new Point(LabelX, _y);
            l.Size = new Size(300, 20);
            _host.Controls.Add(l);
            _y += 26;
        }

        private void Gap()
        {
            _y += 10;
        }

        private delegate double Getter();
        private delegate void Setter(double value);
        private delegate bool BoolGetter();
        private delegate void BoolSetter(bool value);

        /// <summary>
        /// Adds a labelled slider. <paramref name="scale"/> converts a tick to a
        /// setting value, letting integer-only TrackBars express fractions.
        /// </summary>
        private void Slider(string text, int min, int max, double scale, string suffix,
                            Getter get, Setter set, string hint)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(LabelX, _y + 4);
            label.Size = new Size(LabelW, 20);
            _host.Controls.Add(label);

            TrackBar bar = new TrackBar();
            bar.Minimum = min;
            bar.Maximum = max;
            bar.TickStyle = TickStyle.None;
            bar.AutoSize = false;
            bar.Location = new Point(ControlX, _y);
            bar.Size = new Size(ControlW, 26);
            _host.Controls.Add(bar);

            Label value = new Label();
            value.Location = new Point(ValueX, _y + 4);
            value.Size = new Size(ValueW, 20);
            value.ForeColor = SystemColors.GrayText;
            _host.Controls.Add(value);

            bar.ValueChanged += delegate
            {
                double v = bar.Value * scale;
                value.Text = Format(v, scale) + (suffix.Length > 0 ? " " + suffix : "");
                if (_loading) return;
                set(v);
                Push();
            };

            if (!string.IsNullOrEmpty(hint))
            {
                _tips.SetToolTip(label, hint);
                _tips.SetToolTip(bar, hint);
            }

            _refreshers.Add(delegate
            {
                int ticks = (int)Math.Round(get() / scale);
                bar.Value = Math.Max(min, Math.Min(max, ticks));
                double v = bar.Value * scale;
                value.Text = Format(v, scale) + (suffix.Length > 0 ? " " + suffix : "");
            });

            _y += RowH + 4;
        }

        private static string Format(double v, double scale)
        {
            return scale < 1
                ? v.ToString("0.0", CultureInfo.InvariantCulture)
                : v.ToString("0", CultureInfo.InvariantCulture);
        }

        private void Check(string text, BoolGetter get, BoolSetter set, string hint)
        {
            CheckBox box = new CheckBox();
            box.Text = text;
            box.Location = new Point(LabelX, _y);
            box.Size = new Size(FullW, 22);
            box.CheckedChanged += delegate
            {
                if (_loading) return;
                set(box.Checked);
                Push();
            };
            _host.Controls.Add(box);

            if (!string.IsNullOrEmpty(hint)) _tips.SetToolTip(box, hint);

            _refreshers.Add(delegate { box.Checked = get(); });
            _y += 26;
        }

        // -------------------------------------------------------------- state
        /// <summary>Repaints every control from <see cref="_working"/> without re-firing handlers.</summary>
        private void Reload()
        {
            _loading = true;
            try
            {
                foreach (Action refresh in _refreshers) refresh();
            }
            finally
            {
                _loading = false;
            }
        }

        private void Push()
        {
            _apply(_working.Clone());
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _working.Save();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tips.Dispose();
                if (Icon != null) Icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
