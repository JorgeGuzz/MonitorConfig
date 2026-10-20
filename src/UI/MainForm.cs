using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Main window: menu, monitor selector, one tab per area (UI\Tabs) and a status bar.
    partial class MainForm : Form
    {
        readonly TrayApp app;
        readonly MonitorService monitors;
        readonly ComboBox monitorBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly ToolStripStatusLabel statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolStripStatusLabel modeLabel = new ToolStripStatusLabel();
        readonly ToolStripDropDownButton languageButton = new ToolStripDropDownButton { ImageScaling = ToolStripItemImageScaling.None };
        readonly List<MonitorTab> pages = new List<MonitorTab>();
        readonly InputTab inputTab;
        Label noDdcLabel;
        bool updating, toTray;

        public MainForm(TrayApp app)
        {
            this.app = app;
            monitors = app.Monitors;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;   // sizes are scaled by hand with Dpi.*
            Font = Fonts.Ui;
            Text = Strings.AppName;
            Icon = TrayApp.AppIcon(false);
            ClientSize = Dpi.Size(640, 620);
            MinimumSize = Dpi.Size(560, 540);
            StartPosition = FormStartPosition.CenterScreen;

            var menu = BuildMenu();
            var top = BuildTopBar();
            inputTab = new InputTab(app);
            pages.AddRange(new MonitorTab[] { new ImageTab(app), inputTab, new ProfilesTab(app), new SystemTab(app), new InfoTab(app), new AdvancedTab(app) });
            foreach (var page in pages) tabs.TabPages.Add(page);
            var status = new StatusStrip { ShowItemToolTips = true };
            status.Items.Add(statusLabel);
            status.Items.Add(modeLabel);
            status.Items.Add(BuildLanguageButton());

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(status);
            Controls.Add(menu);
            MainMenuStrip = menu;
            RestorePosition();
            monitors.MonitorsChanged += OnMonitorsChanged;
            monitors.ValueChanged += OnValueChanged;
            monitors.StatusChanged += OnStatus;
            OnMonitorsChanged();
            OnStatus(monitors.Detecting ? Strings.Searching : Strings.Ready, false);
            Lay.ResumeTree(this);   // everything was built with layout suspended: one layout pass
            PerformLayout();
        }

        protected override void Dispose(bool disposing)
        {
            monitors.MonitorsChanged -= OnMonitorsChanged;
            monitors.ValueChanged -= OnValueChanged;
            monitors.StatusChanged -= OnStatus;
            base.Dispose(disposing);
        }

        DdcMonitor M { get { return monitors.Active; } }

        // ---------------------------------------------------------------- layout

        MenuStrip BuildMenu()
        {
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem(Strings.MenuFile);
            file.DropDownItems.Add(new ToolStripMenuItem(Strings.MenuRedetect, null, delegate { monitors.Detect(true); }) { ShortcutKeys = Keys.F5 });
            file.DropDownItems.Add(new ToolStripMenuItem(Strings.MenuHide, null, delegate { HideToTray(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem(Strings.MenuExit, null, delegate { app.ExitApp(); }));
            var options = new ToolStripMenuItem(Strings.MenuOptions);
            options.DropDownItems.Add(new ToolStripMenuItem(Strings.MenuSettings, null, delegate { app.ShowSettings(this); }));
            var help = new ToolStripMenuItem(Strings.MenuHelp);
            help.DropDownItems.Add(new ToolStripMenuItem(Strings.MenuAbout, null, delegate { ShowAbout(); }));
            menu.Items.Add(file);
            menu.Items.Add(options);
            menu.Items.Add(help);
            return menu;
        }

        Control BuildTopBar()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = Dpi.Pad(6, 6, 6, 2) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(Lay.Label(Strings.MonitorLabel), 0, 0);
            t.Controls.Add(monitorBox, 1, 0);
            t.Controls.Add(Lay.Button(Strings.Redetect, delegate { monitors.Detect(true); }), 2, 0);
            noDdcLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, ForeColor = Color.Firebrick, Visible = false, Text = Strings.NoDdcHelp };
            t.Controls.Add(noDdcLabel, 0, 1);
            t.SetColumnSpan(noDdcLabel, 3);
            // The wheel must not switch the monitor by accident (it is the first focused control): only with the list open.
            monitorBox.MouseWheel += delegate(object sender, MouseEventArgs e)
            {
                if (!monitorBox.DroppedDown) ((HandledMouseEventArgs)e).Handled = true;
            };
            monitorBox.SelectedIndexChanged += delegate
            {
                if (updating || monitorBox.SelectedIndex < 0 || monitorBox.SelectedIndex >= monitors.All.Count) return;
                monitors.SetActive(monitors.All[monitorBox.SelectedIndex]);
            };
            return t;
        }

        // Bottom right: the flag and name of the language in use; the list applies the choice at once.
        ToolStripItem BuildLanguageButton()
        {
            int flag = Dpi.S(12);
            languageButton.Image = Flags.Get(Lang.Current, flag);
            languageButton.Text = Lang.NameOf(Lang.Current);
            languageButton.ToolTipText = Strings.SettingsLanguage;
            foreach (var choice in LanguageChoice.All())
            {
                var c = choice;
                languageButton.DropDownItems.Add(new ToolStripMenuItem(c.Text, Flags.Get(c.Flag, flag), delegate { app.SetLanguage(c.Code); })
                    { Tag = c.Code, ImageScaling = ToolStripItemImageScaling.None });
            }
            // The setting can also change from the settings dialog without rebuilding this window (same language).
            languageButton.DropDownOpening += delegate
            {
                foreach (ToolStripMenuItem item in languageButton.DropDownItems) item.Checked = (string)item.Tag == app.Settings.Language;
            };
            return languageButton;
        }

        // ---------------------------------------------------------------- state -> UI

        void OnMonitorsChanged()
        {
            Lay.SuspendTree(this);
            try { FillMonitors(); }
            finally { Lay.ResumeTree(this); PerformLayout(); }
        }

        void FillMonitors()
        {
            var m = M;
            if (Visible && m != null && m.Responsive && (DateTime.Now - m.LastRead).TotalSeconds > 5) monitors.ReadMain(m);
            updating = true;
            monitorBox.Items.Clear();
            foreach (var x in monitors.All) monitorBox.Items.Add(x.DisplayName);
            monitorBox.SelectedIndex = monitors.All.IndexOf(m);
            updating = false;
            noDdcLabel.Visible = m != null && !m.Responsive && !monitors.Detecting;
            foreach (var page in pages) page.Bind(m);
            if (m != null)
                foreach (var code in m.Values.Keys.ToList()) OnValueChanged(m, code);   // what is already known
            UpdateMode();
        }

        void OnValueChanged(DdcMonitor m, byte code)
        {
            if (m != M) return;
            foreach (var page in pages) page.OnValue(m, code);
            if (code == Vcp.VFreq) UpdateMode();
        }

        void UpdateMode()
        {
            var m = M;
            VcpValue vf;
            modeLabel.Text = m == null ? "" : m.Values.TryGetValue(Vcp.VFreq, out vf) ? m.Model + " · " + Vcp.Format(Vcp.VFreq, vf) : m.Model;
        }

        void OnStatus(string text, bool error)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
            if (!error && M != null) noDdcLabel.Visible = !M.Responsive && !monitors.Detecting;
        }

        public void InputNamesChanged() { inputTab.NamesChanged(); }

        // ---------------------------------------------------------------- window

        void ShowAbout()
        {
            MessageBox.Show(this, Strings.AboutText(Application.ProductVersion, app.Settings.Path), Strings.MenuAboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void RestorePosition()
        {
            var s = app.Settings;
            if (s.WindowX == int.MinValue || s.WindowY == int.MinValue) return;
            var r = new Rectangle(s.WindowX, s.WindowY, Width, Height);
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y, Math.Max(1, r.Width - 80), 40))))
            {
                StartPosition = FormStartPosition.Manual;
                Location = r.Location;
            }
        }

        void SavePosition()
        {
            if (WindowState != FormWindowState.Normal || !Visible) return;
            app.Settings.WindowX = Location.X;
            app.Settings.WindowY = Location.Y;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            tabs.Focus();   // not the monitor list: arrow keys would change the monitor
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            var m = M;
            if (m != null && m.Responsive && !monitors.Detecting && (DateTime.Now - m.LastRead).TotalSeconds > 5) monitors.ReadMain(m);
        }

        // Closing to the tray disposes the window (rebuilt when reopened): about 10 MB and 60 GDI/USER objects less.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SavePosition();
            base.OnFormClosing(e);
            bool stay = toTray || (e.CloseReason == CloseReason.UserClosing && app.Settings.CloseToTray);
            if (!e.Cancel && !stay && !app.Exiting) app.ExitApp();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            app.Settings.Save();
            app.WindowClosed(this);
        }

        public void HideToTray()
        {
            toTray = true;
            Close();
        }
    }
}
