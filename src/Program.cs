using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("MonitorConfig")]
[assembly: AssemblyDescription("DDC/CI monitor control")]
[assembly: AssemblyProduct("MonitorConfig")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace MonitorConfig
{
    static class Program
    {
        const string MutexName = @"Local\MonitorConfig.Instance";

        [STAThread]
        static int Main(string[] args)
        {
            if (CommandRunner.IsCommand(args)) return CommandLine.Run(args, MutexName);
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MonitorConfig.Show"))
            using (var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MonitorConfig.Exit"))
            {
                bool exit = args.Contains("--exit");
                if (!created)
                {
                    // Already running: bring its window up, or ask it to quit (--exit, used by build.cmd).
                    (exit ? exitEvent : showEvent).Set();
                    if (exit) WaitUntilClosed(mutex);
                    return 0;
                }
                if (exit) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Crash.Install();

                string iniPath = null;
                bool hidden = args.Contains("--tray");
#if DEVTOOLS
                iniPath = DevTools.Arg(args, "--ini");
                hidden |= DevTools.RunsHidden(args);
#endif
                var settings = Settings.Load(iniPath);
                Crash.Folder = Path.GetDirectoryName(settings.Path);   // the log sits next to the settings
                Lang.Load(settings.Language);
                Theme.WindowSetting = settings.WindowTheme;
                var app = new TrayApp(settings, hidden || settings.StartMinimized);
                ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { app.Monitors.Ui(app.ShowMain); }, null, -1, false);
                ThreadPool.RegisterWaitForSingleObject(exitEvent, delegate { app.Monitors.Ui(app.ExitApp); }, null, -1, true);
#if DEVTOOLS
                DevTools.Start(app, args);
#endif
                Application.Run(app);
                mutex.ReleaseMutex();
            }
            return 0;
        }

        // The running copy holds the mutex until it has shut down (max. 5 s).
        static void WaitUntilClosed(Mutex mutex)
        {
            try
            {
                if (mutex.WaitOne(5000)) mutex.ReleaseMutex();
            }
            catch (AbandonedMutexException) { mutex.ReleaseMutex(); }
        }
    }
}
