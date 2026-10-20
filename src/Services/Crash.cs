using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Last-resort error handling: unexpected exceptions go to MonitorConfig.log (next to the settings) instead of
    // .NET's crash dialog, and the tray app keeps running when the error happened in the UI.
    static class Crash
    {
        const long MaxLogBytes = 256 * 1024;
        public static string Folder = AppDomain.CurrentDomain.BaseDirectory;
        static bool shown;

        public static string LogPath { get { return Path.Combine(Folder, "MonitorConfig.log"); } }

        // Must run before the first control is created.
        public static void Install()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                var info = new FileInfo(LogPath);
                bool append = !info.Exists || info.Length < MaxLogBytes;   // start over instead of growing forever
                using (var w = new StreamWriter(LogPath, append, new UTF8Encoding(false)))
                    w.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine);
            }
            catch (Exception) { }   // logging must never throw
        }

        static void Report(Exception ex)
        {
            Log(ex);
            if (shown) return;   // once per session: a repeating error must not flood the screen
            shown = true;
            MessageBox.Show(Strings.CrashMessage(LogPath, ex.Message), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
