using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Tsuru
{
    /// <summary>
    /// Maps a screen point to the executable name of the window under it.
    ///
    /// This runs inside the low-level mouse hook, which must return promptly or
    /// Windows silently evicts the hook, so results are cached per top-level
    /// window and only re-resolved after <see cref="TtlMs"/>.
    /// </summary>
    internal static class ProcessResolver
    {
        private const int TtlMs = 3000;
        private const int MaxEntries = 256;

        private class Entry
        {
            public string Name;
            public long Stamp;
        }

        private static readonly Dictionary<IntPtr, Entry> Cache = new Dictionary<IntPtr, Entry>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        /// <summary>Executable name (e.g. "chrome.exe") owning the window at <paramref name="pt"/>, or "".</summary>
        public static string ExecutableAt(Native.POINT pt)
        {
            IntPtr hwnd = Native.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;

            IntPtr root = Native.GetAncestor(hwnd, Native.GA_ROOT);
            if (root != IntPtr.Zero) hwnd = root;

            long now = Clock.ElapsedMilliseconds;
            Entry entry;
            if (Cache.TryGetValue(hwnd, out entry) && now - entry.Stamp < TtlMs)
                return entry.Name;

            string name = Resolve(hwnd);
            if (Cache.Count > MaxEntries) Cache.Clear();
            Cache[hwnd] = new Entry { Name = name, Stamp = now };
            return name;
        }

        private static string Resolve(IntPtr hwnd)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return string.Empty;

                handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (handle == IntPtr.Zero) return string.Empty;

                int capacity = 512;
                StringBuilder sb = new StringBuilder(capacity);
                if (!Native.QueryFullProcessImageName(handle, 0, sb, ref capacity))
                    return string.Empty;

                return Path.GetFileName(sb.ToString());
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                if (handle != IntPtr.Zero) Native.CloseHandle(handle);
            }
        }
    }
}
