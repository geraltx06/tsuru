using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Tsuru
{
    internal static class Program
    {
        private const string MutexName = "Tsuru.SingleInstance.9F2A1C";


        [STAThread]
        private static int Main(string[] args)
        {
            // Build-time helper: the executable draws its own icon file so the
            // repository carries no binary art. Not a user-facing switch.
            if (args.Length == 2 && args[0] == "--export-icon")
            {
                try
                {
                    IconFactory.SaveIco(args[1], new int[] { 16, 20, 24, 32, 48, 64, 128, 256 });
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            bool isFirstInstance;
            using (Mutex mutex = new Mutex(true, MutexName, out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    // Already running: ask that instance to show its settings and
                    // leave immediately, so re-launching is useful rather than a
                    // dialog the user has to dismiss.
                    if (!SignalRunningInstance())
                    {
                        MessageBox.Show(
                            "Tsuru is already running. Look for it in the notification area.",
                            "Tsuru", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Before anything reads settings, so an upgrade from the app's
                // former name keeps the user's configuration.
                Migration.Run();

                // Created here rather than inside TrayContext so it exists from
                // the moment the mutex is held. Otherwise a second launch during
                // start-up finds no event and its request is lost silently.
                EventWaitHandle activation = OpenSignal(TrayContext.ActivationEventName);
                EventWaitHandle quit = OpenSignal(TrayContext.QuitEventName);

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("Unhandled UI exception: " + e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Write("Unhandled exception: " + e.ExceptionObject);
                };

                try
                {
                    Application.Run(new TrayContext(activation, quit, IsAutostart(args)));
                }
                catch (Exception ex)
                {
                    Log.Write("Fatal: " + ex);
                    MessageBox.Show("Tsuru could not start:" + Environment.NewLine + ex.Message,
                        "Tsuru", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                GC.KeepAlive(mutex);
            }

            return 0;
        }

        /// <summary>
        /// True when Windows started us at sign-in rather than the user running
        /// the app. The run-at-sign-in entry carries this switch so a boot does
        /// not open the welcome screen every time.
        /// </summary>
        private static bool IsAutostart(string[] args)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, StartupManager.AutostartSwitch, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Creates one of the named events this instance listens on. Never
        /// throws: losing a signal channel should not stop the app running.
        /// </summary>
        private static EventWaitHandle OpenSignal(string name)
        {
            try
            {
                bool created;
                return new EventWaitHandle(false, EventResetMode.AutoReset, name, out created);
            }
            catch (Exception ex)
            {
                Log.Write("Signal '" + name + "' unavailable: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Asks the instance that already holds the mutex to show its settings.
        /// It may still be starting up, so this retries briefly before giving up.
        /// </summary>
        private static bool SignalRunningInstance()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    EventWaitHandle activation;
                    if (EventWaitHandle.TryOpenExisting(TrayContext.ActivationEventName, out activation))
                    {
                        using (activation) activation.Set();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Could not signal the running instance: " + ex.Message);
                    return false;
                }
                Thread.Sleep(100);
            }
            return false;
        }
    }
}
