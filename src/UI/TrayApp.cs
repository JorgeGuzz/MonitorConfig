using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorConfig
{
    // Composition root and tray shell: builds the services, owns the tray icon and the windows, and offers the
    // app-level actions (dialogs, input changes with confirmation, exit) to the windows.
    partial class TrayApp : ApplicationContext
    {
        public readonly Settings Settings;
        public readonly MonitorService Monitors;
        public readonly SaveScheduler Saver;
        public readonly InputWatcher Inputs;
        public readonly ProfileService Profiles;
        public readonly InputLabels Labels;
        readonly UiDispatcher dispatcher = new UiDispatcher();
        readonly CommandServer commands;

        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly SynchronizationContext ui;
        readonly System.Windows.Forms.Timer redetect = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer trimTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer { Interval = 15000 };
        string balloonUrl;   // what a click on the current balloon opens
        MainForm main;
        OsdForm osd;
        Flyout flyout;
        bool dialogOpen;
        bool builtDark = Theme.AppDark;   // the theme the open windows were built with
        Font boldMenuFont;
        public bool Exiting { get; private set; }

        public TrayApp(Settings settings, bool startHidden)
        {
            Settings = settings;
            Func<DateTime> clock = () => DateTime.Now;
            Monitors = new MonitorService(settings, new Dxva2Bus(), DdcQueue.Threaded(), dispatcher.Post, clock);
            Labels = new InputLabels(settings);
            Saver = new SaveScheduler(Monitors, clock);
            Inputs = new InputWatcher(Monitors, Saver, Labels, clock);
            Profiles = new ProfileService(Monitors);
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            Profiles.Indicator += delegate(DdcMonitor m, string title, int percent)
            {
                if (!Settings.ShowOsd) return;
                if (osd == null || osd.IsDisposed) osd = new OsdForm();
                osd.ShowValue(m, title, percent);
            };
            Inputs.Notify += delegate(string text) { balloonUrl = null; tray.ShowBalloonTip(6000, Strings.AppName, text, ToolTipIcon.Warning); };
            Monitors.ValueChanged += delegate(DdcMonitor m, byte code) { if (code == Vcp.Brightness && m == Monitors.Active) UpdateTrayText(); };
            Monitors.MonitorsChanged += UpdateTrayText;
            Monitors.MonitorsChanged += ScheduleTrim;

            tray = new NotifyIcon { Icon = AppIcon(true), Text = Strings.AppName, ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
            tray.BalloonTipClicked += delegate { if (balloonUrl != null) OpenUrl(balloonUrl); };
            menu.Opening += BuildMenu;
            menu.Closed += delegate { ScheduleTrim(); };
            trimTimer.Tick += delegate { TrimMemory(); };
            redetect.Tick += delegate { redetect.Stop(); Monitors.Detect(false); };
            updateTimer.Tick += delegate
            {
                updateTimer.Stop();
                if (Settings.CheckUpdates && Settings.LastUpdateCheck.Date < DateTime.Today) CheckForUpdates(null);
            };
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

            commands = new CommandServer(CommandServer.DefaultPipe, dispatcher.Post, a => CommandRunner.Run(a, Monitors, Profiles, Labels, Settings));
            Startup.Repair();
            if (settings.LastError != null) Monitors.Status(Strings.SettingsReadError(settings.LastError), true);
            Monitors.Detect(false);
            if (!startHidden) ShowMain();
            updateTimer.Start();   // a little after startup, so it never competes with the first DDC reads
        }

        // ---------------------------------------------------------------- icon

        static Icon smallIcon, largeIcon;

        // The icon embedded in the exe (Win32 resource): small for the tray, large for windows.
        public static Icon AppIcon(bool small)
        {
            if (largeIcon == null)
            {
                var l = new IntPtr[1];
                var s = new IntPtr[1];
                try
                {
                    if (Native.ExtractIconEx(Application.ExecutablePath, 0, l, s, 1) > 0)
                    {
                        if (l[0] != IntPtr.Zero) largeIcon = Icon.FromHandle(l[0]);
                        if (s[0] != IntPtr.Zero) smallIcon = Icon.FromHandle(s[0]);
                    }
                }
                catch (Exception ex) { Crash.Log(ex); }
                if (largeIcon == null) largeIcon = SystemIcons.Application;
                if (smallIcon == null) smallIcon = largeIcon;
            }
            return small ? smallIcon : largeIcon;
        }

        // ---------------------------------------------------------------- memory

        // A few seconds after the last window/panel/menu closes: collect garbage and give unused pages back to Windows.
        public void ScheduleTrim()
        {
            trimTimer.Stop();
            trimTimer.Start();
        }

        void TrimMemory()
        {
            trimTimer.Stop();
            if (Exiting || (main != null && main.Visible) || (flyout != null && flyout.Visible) || menu.Visible) return;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using (var p = System.Diagnostics.Process.GetCurrentProcess())
                Native.SetProcessWorkingSetSize(p.Handle, new IntPtr(-1), new IntPtr(-1));
        }

        public void WindowClosed(MainForm form)
        {
            if (main == form) main = null;
            ScheduleTrim();
        }

        // ---------------------------------------------------------------- windows and dialogs

        public void ShowMain()
        {
            if (Exiting) return;
            if (main == null || main.IsDisposed) main = new MainForm(this);
            if (!main.Visible) main.Show();
            if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
            main.Activate();
            Native.SetForegroundWindow(main.Handle);
        }

        void ToggleFlyout()
        {
            if (Exiting || dialogOpen) return;
            if (flyout == null || flyout.IsDisposed) flyout = new Flyout(this);
            // A click on the icon first deactivates (and hides) the open flyout: treat it as "close".
            if (flyout.Visible || (DateTime.Now - flyout.HiddenAt).TotalMilliseconds < 300) { flyout.Hide(); return; }
            flyout.ShowNearTray();
        }

        public void ShowSettings(IWin32Window owner)
        {
            if (dialogOpen) return;
            dialogOpen = true;
            try
            {
                using (var f = new SettingsForm(Settings))
                {
                    if (owner == null) f.StartPosition = FormStartPosition.CenterScreen;
                    if (f.ShowDialog(owner) == DialogResult.OK) SaveSettings();
                }
                ReloadLook();
            }
            finally { dialogOpen = false; }
        }

        // setting: a language code, or empty for automatic (the main window's language button).
        public void SetLanguage(string setting)
        {
            if (Settings.Language == setting) return;
            Settings.Language = setting;
            SaveSettings();
            ReloadLook();
        }

        // The sun button: light <-> dark. Until the first click the windows follow the Windows app mode.
        public void ToggleTheme()
        {
            Settings.WindowTheme = Theme.AppDark ? "light" : "dark";
            SaveSettings();
            ReloadLook();
        }

        // Language and window theme from the settings. Deferred rebuild: the request may come from the main window
        // itself (its menu or language button).
        void ReloadLook()
        {
            string before = Lang.Current;
            Lang.Load(Settings.Language);
            Theme.WindowSetting = Settings.WindowTheme;
            if (Lang.Current != before || Theme.AppDark != builtDark) ui.Post(delegate { RebuildWindows(); }, null);
        }

        public Profile EditProfile(IWin32Window owner, DdcMonitor m, Profile p, Profile original)
        {
            dialogOpen = true;
            try
            {
                Func<Profile, string> validate = candidate =>
                    Settings.Profiles.Any(x => x != original && x.Monitor == candidate.Monitor && x.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase))
                        ? Strings.ProfileExists(candidate.Name) : null;
                using (var f = new ProfileForm(m, p, validate))
                    return f.ShowDialog(owner) == DialogResult.OK ? f.Result : null;
            }
            finally { dialogOpen = false; }
        }

        public bool EditInputNames(IWin32Window owner, DdcMonitor m)
        {
            if (dialogOpen) return false;
            dialogOpen = true;
            try
            {
                using (var f = new InputNamesForm(Settings, m))
                {
                    if (owner == null) f.StartPosition = FormStartPosition.CenterScreen;
                    if (f.ShowDialog(owner) != DialogResult.OK) return false;
                    SaveSettings();
                    if (main != null && !main.IsDisposed && owner != main) main.InputNamesChanged();
                    return true;
                }
            }
            finally { dialogOpen = false; }
        }

        // Windows built in the previous language or theme are rebuilt (the tray menu and panel are rebuilt when opened).
        void RebuildWindows()
        {
            builtDark = Theme.AppDark;
            UpdateTrayText();
            if (main != null && !main.IsDisposed && main.Visible)
            {
                main.HideToTray();
                ShowMain();
            }
        }

        public void SaveSettings()
        {
            if (!Settings.Save()) Monitors.Status(Strings.SettingsSaveError(Settings.LastError), true);
        }

        public void ChangeInput(DdcMonitor m, byte value, IWin32Window owner)
        {
            var cur = m.CurrentInput();
            // Already there as far as we know: resend without asking (the remembered value can be stale).
            if (cur == value) { Inputs.Change(m, value); return; }
            if (Settings.ConfirmInput)
            {
                string text = Strings.ChangeInputQuestion(m.Model, Labels.Text(m, value), cur.HasValue ? Labels.Text(m, cur.Value) : null);
                if (MessageBox.Show(owner, text, Strings.ChangeInputTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            Inputs.Change(m, value);
        }

        // ---------------------------------------------------------------- tray menu

        void UpdateTrayText()
        {
            var m = Monitors.Active;
            string text = Strings.AppName;
            VcpValue v;
            if (m != null)
            {
                text = m.Model;
                if (m.Values.TryGetValue(Vcp.Brightness, out v)) text += " · " + Strings.BrightnessTooltip(v.Percent);
            }
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        // Names come from the user or the monitor: '&' must not become a menu mnemonic.
        static string Esc(string text) { return text.Replace("&", "&&"); }

        Font BoldMenuFont { get { return boldMenuFont ?? (boldMenuFont = new Font(menu.Font, FontStyle.Bold)); } }

        void BuildMenu(object sender, CancelEventArgs e)
        {
            foreach (var old in menu.Items.Cast<ToolStripItem>().ToList()) old.Dispose();   // instead of leaving them to the GC
            menu.Items.Clear();
            var responsive = Monitors.All.Where(m => m.Responsive).ToList();
            if (responsive.Count == 0)
                menu.Items.Add(new ToolStripMenuItem(Monitors.Detecting ? Strings.Searching : Strings.NoMonitorAnswers) { Enabled = false });
            else if (responsive.Count == 1)
            {
                menu.Items.Add(new ToolStripMenuItem(Esc(responsive[0].Model)) { Enabled = false });
                AddMonitorItems(menu.Items, responsive[0]);
            }
            else
                foreach (var m in responsive)
                {
                    var sub = new ToolStripMenuItem(Esc(m.Model));
                    if (m == Monitors.Active) sub.Font = BoldMenuFont;
                    AddMonitorItems(sub.DropDownItems, m);
                    menu.Items.Add(sub);
                }
            foreach (var m in responsive) Monitors.RefreshQuick(m);   // fresh values for the next time

            var profiles = Settings.Profiles;
            if (profiles.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                bool several = profiles.Select(p => p.Monitor).Distinct().Count() > 1;
                foreach (var p in profiles)
                {
                    var profile = p;
                    var target = Profiles.MonitorFor(p);
                    string text = Esc(Strings.ProfileMenu(p.Name, several && target != null ? target.Model : null));
                    menu.Items.Add(new ToolStripMenuItem(text, null, delegate { Profiles.Apply(profile); }) { Enabled = target != null });
                }
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(Strings.OpenApp, null, delegate { ShowMain(); }) { Font = BoldMenuFont });
            menu.Items.Add(new ToolStripMenuItem(Strings.MenuSettingsShort, null, delegate { ShowSettings(main != null && main.Visible ? main : null); }) { Enabled = !dialogOpen });
            menu.Items.Add(new ToolStripMenuItem(Strings.RedetectMonitors, null, delegate { Monitors.Detect(true); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(Strings.Exit, null, delegate { ExitApp(); }));
            DarkMode.StyleMenu(menu, Theme.Dark);   // like the tray panel: Windows mode, not app mode
            e.Cancel = false;
        }

        void AddMonitorItems(ToolStripItemCollection items, DdcMonitor m)
        {
            if (m.Supports(Vcp.Brightness)) items.Add(LevelMenu(m, Vcp.Brightness, Strings.Brightness));
            if (m.Supports(Vcp.Volume))
            {
                var volume = LevelMenu(m, Vcp.Volume, Strings.Volume);
                if (m.Supports(Vcp.Mute))
                {
                    VcpValue mute;
                    bool muted = m.Values.TryGetValue(Vcp.Mute, out mute) && mute.Current == Vcp.MuteOn;
                    volume.DropDownItems.Add(new ToolStripSeparator());
                    volume.DropDownItems.Add(new ToolStripMenuItem(Strings.MuteMenu, null, delegate { Monitors.Set(m, Vcp.Mute, muted ? Vcp.MuteOff : Vcp.MuteOn); }) { Checked = muted });
                    if (muted) volume.Text += " · " + Strings.MutedSuffix;
                }
                items.Add(volume);
            }
            if (m.Supports(Vcp.Input))
            {
                var cur = m.CurrentInput();
                var input = new ToolStripMenuItem(cur.HasValue ? Strings.InputMenu(Esc(Labels.Text(m, cur.Value))) : Strings.Input);
                foreach (var o in m.InputOptions)
                {
                    byte value = o;
                    input.DropDownItems.Add(new ToolStripMenuItem(Esc(Labels.Text(m, o)), null, delegate { ChangeInput(m, value, null); }) { Checked = cur == o });
                }
                items.Add(input);
            }
        }

        // "Brightness (45 %)" with 0..100 % steps.
        ToolStripMenuItem LevelMenu(DdcMonitor m, byte code, string name)
        {
            VcpValue v;
            bool have = m.Values.TryGetValue(code, out v);
            var item = new ToolStripMenuItem(have ? name + " (" + v.Percent + " %)" : name);
            foreach (var pct in new[] { 0, 10, 25, 50, 75, 100 })
            {
                uint target = Monitors.ValueForPercent(m, code, pct);
                item.DropDownItems.Add(new ToolStripMenuItem(pct + " %", null, delegate { Monitors.Set(m, code, target); }) { Checked = have && v.Current == target });
            }
            return item;
        }

        // ---------------------------------------------------------------- system events

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Monitors.Ui(delegate { redetect.Stop(); redetect.Interval = 2000; redetect.Start(); });
        }

        // Windows switched between light and dark mode (the main window follows it when set to automatic).
        void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
                Monitors.Ui(delegate { if (!Exiting && Theme.AppDark != builtDark) RebuildWindows(); });
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                Monitors.Ui(delegate { redetect.Stop(); redetect.Interval = 4000; redetect.Start(); });
        }

        // ---------------------------------------------------------------- updates

        // owner == null: the daily automatic check, silent unless there is a version not announced yet.
        // Otherwise the menu item, which always answers.
        public void CheckForUpdates(IWin32Window owner)
        {
            UpdateChecker.CheckAsync(dispatcher.Post, delegate(UpdateChecker.Release latest, Exception error)
            {
                if (Exiting) return;
                if (owner == null)
                {
                    if (error != null) return;   // offline or GitHub unreachable: try again on the next start
                    Settings.LastUpdateCheck = DateTime.Today;
                    if (UpdateChecker.IsNewer(latest) && Settings.UpdateNotified != latest.Tag)
                    {
                        Settings.UpdateNotified = latest.Tag;
                        balloonUrl = latest.Url;
                        tray.ShowBalloonTip(10000, Strings.AppName, Strings.UpdateAvailable(latest.Version.ToString()), ToolTipIcon.Info);
                    }
                    SaveSettings();
                    return;
                }
                var form = owner as Form;
                if (form != null && form.IsDisposed) owner = null;
                if (error != null)
                    MessageBox.Show(owner, Strings.UpdateCheckFailed(error.Message), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else if (!UpdateChecker.IsNewer(latest))
                    MessageBox.Show(owner, Strings.UpToDate(UpdateChecker.Current.ToString()), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                else if (MessageBox.Show(owner, Strings.UpdateAsk(latest.Version.ToString(), UpdateChecker.Current.ToString()), Strings.AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    OpenUrl(latest.Url);
            });
        }

        static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { Crash.Log(ex); }
        }

        // ---------------------------------------------------------------- exit

        // Deferred so it never runs inside a handler of the window it disposes.
        public void ExitApp()
        {
            if (Exiting) return;
            Exiting = true;
            ui.Post(delegate { Shutdown(); }, null);
        }

        void Shutdown()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            redetect.Stop();
            trimTimer.Stop();
            updateTimer.Stop();
            Settings.Save();
            if (main != null && !main.IsDisposed) main.Dispose();
            if (flyout != null && !flyout.IsDisposed) flyout.Dispose();
            if (osd != null && !osd.IsDisposed) osd.Dispose();
            tray.Visible = false;
            tray.Dispose();
            commands.Dispose();
            Saver.Dispose();      // first: pending saves are queued before the monitors close
            Inputs.Dispose();
            Monitors.Dispose();   // closes the monitors and finishes the DDC queue
            dispatcher.Dispose();
            ExitThread();
        }
    }
}
