using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tsuru
{
    /// <summary>
    /// Turns discrete wheel notches into a stream of small synthetic wheel
    /// events played out over time.
    ///
    /// Two threads are involved:
    ///   * the hook thread owns a WH_MOUSE_LL hook and its own message pump.
    ///     It swallows real wheel events and queues them as animation pulses.
    ///   * the animation thread drains the queue, sampling every active pulse
    ///     against an easing curve and injecting the difference via SendInput.
    ///
    /// Injected events carry <see cref="Signature"/> in dwExtraInfo so the hook
    /// can recognise its own output and let it pass straight through.
    /// </summary>
    public sealed class ScrollEngine : IDisposable
    {
        private const int WheelDelta = 120;
        private static readonly IntPtr Signature = new IntPtr(0x5343524C); // 'SCRL'

        /// <summary>One wheel notch in flight.</summary>
        private sealed class Pulse
        {
            public double Total;      // total distance in wheel units
            public double Emitted;    // how much has already been injected
            public double Start;      // clock reading when the notch arrived
            public bool Horizontal;
        }

        private readonly object _sync = new object();
        private readonly List<Pulse> _pulses = new List<Pulse>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        private volatile Settings _settings;
        private volatile bool _running;

        private Thread _hookThread;
        private Thread _animThread;
        private uint _hookThreadId;
        private IntPtr _hook;
        private Native.HookProc _hookProc;   // must outlive the hook; do not inline
        private ManualResetEvent _hookReady;
        private volatile bool _hookInstalled;

        private double _lastScrollMs = double.NegativeInfinity;
        private double _carryX, _carryY;

        // Cached normaliser so pulse(1) == 1 for the configured scale.
        private double _normalizeScale = double.NaN;
        private double _normalizeValue = 1;

        /// <summary>Total notches smoothed this session - surfaced in the About box.</summary>
        public long HandledCount;

        public ScrollEngine(Settings settings)
        {
            _settings = settings;
        }

        public Settings Settings
        {
            get { return _settings; }
            set { _settings = value; }
        }

        /// <summary>True once the low-level hook is actually installed.</summary>
        public bool IsHookInstalled
        {
            get { return _hookInstalled; }
        }

        // ------------------------------------------------------------- start
        public bool Start()
        {
            if (_running) return _hookInstalled;
            _running = true;

            _hookReady = new ManualResetEvent(false);

            _hookThread = new Thread(HookLoop);
            _hookThread.IsBackground = true;
            _hookThread.Name = "Tsuru.Hook";
            // The hook callback runs on this thread; give it headroom so input
            // never queues behind ordinary work.
            _hookThread.Priority = ThreadPriority.Highest;
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();

            _animThread = new Thread(AnimationLoop);
            _animThread.IsBackground = true;
            _animThread.Name = "Tsuru.Animate";
            _animThread.Priority = ThreadPriority.AboveNormal;
            _animThread.Start();

            _hookReady.WaitOne(3000);
            return _hookInstalled;
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _wake.Set();

            uint tid = _hookThreadId;
            if (tid != 0)
                Native.PostThreadMessage(tid, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            if (_animThread != null) _animThread.Join(1000);
            if (_hookThread != null) _hookThread.Join(1000);

            lock (_sync) _pulses.Clear();
            _carryX = _carryY = 0;
            _hookInstalled = false;
        }

        public void Dispose()
        {
            Stop();
            _wake.Close();
            if (_hookReady != null) _hookReady.Close();
        }

        // -------------------------------------------------------- hook thread
        private void HookLoop()
        {
            _hookThreadId = Native.GetCurrentThreadId();
            _hookProc = HookCallback;

            IntPtr module = Native.GetModuleHandle(null);
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _hookProc, module, 0);

            if (_hook == IntPtr.Zero)
            {
                Log.Write("SetWindowsHookEx failed, error " + Marshal.GetLastWin32Error());
                _hookInstalled = false;
                _hookReady.Set();
                return;
            }

            _hookInstalled = true;
            _hookReady.Set();

            try
            {
                Native.MSG msg;
                while (_running && Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Hook message loop failed: " + ex);
            }
            finally
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
                _hookInstalled = false;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Anything other than a real wheel event we want to smooth falls
            // through to CallNextHookEx untouched.
            if (nCode < 0) return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

            try
            {
                int message = wParam.ToInt32();
                if (message != Native.WM_MOUSEWHEEL && message != Native.WM_MOUSEHWHEEL)
                    return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                Native.MSLLHOOKSTRUCT data =
                    (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));

                // Our own injected frames.
                if (data.dwExtraInfo == Signature)
                    return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                Settings s = _settings;
                if (s == null || !s.Enabled)
                    return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                bool horizontal = message == Native.WM_MOUSEHWHEEL;
                if (horizontal ? !s.SmoothHorizontal : !s.SmoothVertical)
                    return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                // Another tool is synthesising scrolls; smoothing them would
                // fight it and can feed back into this hook.
                if (s.IgnoreInjected && (data.flags & Native.LLMHF_INJECTED) != 0)
                    return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                int delta = unchecked((short)((data.mouseData >> 16) & 0xFFFF));
                if (delta == 0)
                    return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                // Precision touchpads report fine-grained deltas and are already
                // smooth; re-animating them makes them feel laggy.
                if (s.IgnorePrecisionDevices && (delta % WheelDelta) != 0)
                    return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                if (IsExcluded(s, data.pt))
                    return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                Enqueue(s, delta, horizontal);
                HandledCount++;

                // Swallow the original notch - we deliver it ourselves.
                return new IntPtr(1);
            }
            catch (Exception ex)
            {
                Log.Write("Hook callback failed: " + ex.Message);
                return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }
        }

        private static bool IsExcluded(Settings s, Native.POINT pt)
        {
            if (s.Excluded.Count == 0) return false;
            string exe = ProcessResolver.ExecutableAt(pt);
            if (exe.Length == 0) return false;
            for (int i = 0; i < s.Excluded.Count; i++)
            {
                if (string.Equals(s.Excluded[i], exe, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void Enqueue(Settings s, int rawDelta, bool horizontal)
        {
            double now = _clock.Elapsed.TotalMilliseconds;

            // A notch travels StepSize wheel units; 120 reproduces the distance
            // Windows would have scrolled on its own.
            double amount = (rawDelta / (double)WheelDelta) * s.StepSize;

            if (horizontal ? s.InvertHorizontal : s.InvertVertical)
                amount = -amount;

            // Notches arriving in quick succession compound, so a fast flick
            // travels further than the same number of slow notches.
            if (s.AccelDelta > 0)
            {
                double sinceLast = now - _lastScrollMs;
                if (sinceLast < s.AccelDelta)
                {
                    double factor = (1.0 + (50.0 / Math.Max(sinceLast, 1.0))) / 2.0;
                    if (factor > 1.0) amount *= Math.Min(factor, Math.Max(1.0, s.AccelMax));
                }
            }
            _lastScrollMs = now;

            Pulse p = new Pulse();
            p.Total = amount;
            p.Emitted = 0;
            p.Start = now;
            p.Horizontal = horizontal;

            lock (_sync)
            {
                // Guard against unbounded growth if a frame ever stalls.
                if (_pulses.Count > 64) _pulses.RemoveAt(0);
                _pulses.Add(p);
            }

            _wake.Set();
        }

        // --------------------------------------------------- animation thread
        private void AnimationLoop()
        {
            Native.timeBeginPeriod(1);
            try
            {
                while (_running)
                {
                    bool active;
                    lock (_sync) active = _pulses.Count > 0;

                    if (!active)
                    {
                        // Nothing in flight: drop sub-unit remainders so a new
                        // gesture starts clean, then sleep until woken.
                        _carryX = _carryY = 0;
                        _wake.WaitOne(250);
                        continue;
                    }

                    try
                    {
                        Step();
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Animation step failed: " + ex.Message);
                        lock (_sync) _pulses.Clear();
                    }

                    Settings s = _settings;
                    int fps = s == null ? 120 : s.FrameRate;
                    if (fps < 30) fps = 30;
                    if (fps > 500) fps = 500;
                    Thread.Sleep(Math.Max(1, (int)Math.Round(1000.0 / fps)));
                }
            }
            finally
            {
                Native.timeEndPeriod(1);
            }
        }

        private void Step()
        {
            Settings s = _settings;
            if (s == null) return;

            double now = _clock.Elapsed.TotalMilliseconds;
            double duration = Math.Max(1.0, s.AnimationTime);
            double dx = 0, dy = 0;

            lock (_sync)
            {
                for (int i = _pulses.Count - 1; i >= 0; i--)
                {
                    Pulse p = _pulses[i];
                    double elapsed = now - p.Start;
                    bool finished = elapsed >= duration;

                    double position = finished ? 1.0 : elapsed / duration;
                    if (s.PulseAlgorithm) position = Curve(position, s.PulseScale);

                    // Emit the difference between where the notch should be and
                    // where it has already been taken.
                    double move = p.Total * position - p.Emitted;
                    p.Emitted += move;

                    if (p.Horizontal) dx += move; else dy += move;

                    if (finished) _pulses.RemoveAt(i);
                }
            }

            // Wheel events are integral, so fractions are carried to the next frame.
            _carryY += dy;
            _carryX += dx;

            int stepY = (int)_carryY;   // truncates toward zero on both signs
            int stepX = (int)_carryX;
            _carryY -= stepY;
            _carryX -= stepX;

            if (stepY != 0) Inject(stepY, false);
            if (stepX != 0) Inject(stepX, true);
        }

        private static void Inject(int delta, bool horizontal)
        {
            Native.INPUT[] input = new Native.INPUT[1];
            input[0].type = Native.INPUT_MOUSE;
            input[0].mi.dwFlags = horizontal ? Native.MOUSEEVENTF_HWHEEL : Native.MOUSEEVENTF_WHEEL;
            input[0].mi.mouseData = delta;
            input[0].mi.dwExtraInfo = Signature;

            Native.SendInput(1, input, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        // ------------------------------------------------------------- easing
        /// <summary>
        /// The smoothscroll pulse curve: a quick ramp that decays into a long
        /// tail, normalised so the notch lands exactly on its target.
        /// </summary>
        private double Curve(double x, double scale)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;

            if (scale <= 0) scale = 1;
            if (_normalizeScale != scale)
            {
                double at1 = Raw(1.0, scale);
                _normalizeValue = at1 == 0 ? 1 : 1.0 / at1;
                _normalizeScale = scale;
            }

            return Raw(x, scale) * _normalizeValue;
        }

        private static double Raw(double x, double scale)
        {
            x *= scale;
            if (x < 1.0)
                return x - (1.0 - Math.Exp(-x));

            double start = Math.Exp(-1.0);
            x -= 1.0;
            return start + (1.0 - Math.Exp(-x)) * (1.0 - start);
        }
    }
}
