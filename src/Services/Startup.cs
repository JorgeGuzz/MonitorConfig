using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorConfig
{
    // "Start with Windows" through HKCU\...\Run (no admin needed). Follows the exe if it is moved.
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "MonitorConfig";

        static string Command { get { return "\"" + Application.ExecutablePath + "\" --tray"; } }

        static string Current
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k == null ? null : k.GetValue(ValueName) as string; }
                catch (Exception) { return null; }
            }
        }

        public static bool IsEnabled { get { return Current != null; } }

        public static bool Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(ValueName, Command);
                    else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        // The exe was moved: point the entry at this copy, but only when the registered one no longer exists
        // (otherwise running any other copy of the program would take over the autostart).
        public static void Repair()
        {
            string cur = Current;
            if (cur == null || cur.Equals(Command, StringComparison.OrdinalIgnoreCase)) return;
            string registered = ExeOf(cur);
            if (registered == null || !File.Exists(registered)) Set(true);
        }

        public static string ExeOf(string command)
        {
            command = command.Trim();
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command.Substring(1, end - 1) : null;
            }
            int space = command.IndexOf(' ');
            return space > 0 ? command.Substring(0, space) : command;
        }
    }
}
