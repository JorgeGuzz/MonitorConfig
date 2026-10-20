using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MonitorConfig
{
    abstract class DialogBase : Form
    {
        protected readonly TableLayoutPanel Body;
        protected readonly Button OkButton, CancelBtn;

        protected DialogBase(string title)
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;   // sizes are scaled by hand with Dpi.*
            Font = Fonts.Ui;
            Text = title;
            Icon = TrayApp.AppIcon(false);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Dpi.Pad(8);

            var outer = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            Body = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = Dpi.Pad(0, 10, 0, 0) };
            CancelBtn = new Button { Text = Strings.Cancel, DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = Dpi.Size(88, 0) };
            OkButton = new Button { Text = Strings.Ok, AutoSize = true, MinimumSize = Dpi.Size(88, 0) };
            OkButton.Click += delegate { if (Commit()) { DialogResult = DialogResult.OK; Close(); } };
            buttons.Controls.Add(CancelBtn);
            buttons.Controls.Add(OkButton);
            outer.Controls.Add(Body, 0, 0);
            outer.Controls.Add(buttons, 0, 1);
            Controls.Add(outer);
            AcceptButton = OkButton;
            CancelButton = CancelBtn;
        }

        protected void Finish()
        {
            ResumeLayout(false);
            PerformLayout();
        }

        protected abstract bool Commit();

        protected void Header(string text)
        {
            var l = new Label { Text = text, AutoSize = true, UseMnemonic = false, Font = Fonts.Bold, Margin = Dpi.Pad(3, Body.RowCount == 0 ? 3 : 14, 3, 4) };
            Wide(l);
        }

        protected T Wide<T>(T c) where T : Control
        {
            int row = Lay.Row(Body);
            Body.Controls.Add(c, 0, row);
            Body.SetColumnSpan(c, 2);
            return c;
        }

        protected T Field<T>(string label, T c) where T : Control
        {
            int row = Lay.Row(Body);
            var l = Lay.Label(label);
            l.Margin = Dpi.Pad(20, 6, 10, 3);
            Body.Controls.Add(l, 0, row);
            Body.Controls.Add(c, 1, row);
            return c;
        }

        protected CheckBox Check(string text, bool value)
        {
            return Wide(new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = Dpi.Pad(20, 3, 3, 3) });
        }

        protected Label Hint(string text)
        {
            return Wide(new Label { Text = text, AutoSize = true, UseMnemonic = false, ForeColor = SystemColors.GrayText, MaximumSize = Dpi.Size(420, 0), Margin = Dpi.Pad(20, 2, 3, 3) });
        }

        protected void Tell(string text)
        {
            MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    class SettingsForm : DialogBase
    {
        readonly Settings s;
        readonly CheckBox startup, startMin, closeTray, osd, confirmInput, keepInput;
        readonly ComboBox language;

        public SettingsForm(Settings settings) : base(Strings.SettingsTitle)
        {
            s = settings;
            Header(Strings.SettingsLanguage);
            language = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Dpi.S(17),
                Width = Dpi.S(300), Margin = Dpi.Pad(20, 3, 3, 3)
            };
            language.DrawItem += LanguageChoice.Draw;
            foreach (var choice in LanguageChoice.All()) language.Items.Add(choice);
            language.SelectedIndex = Math.Max(0, Array.IndexOf(Lang.Order, s.Language) + 1);
            Wide(language);

            Header(Strings.SettingsStart);
            startup = Check(Strings.SettingsAutostart, Startup.IsEnabled);
            startMin = Check(Strings.SettingsStartMinimized, s.StartMinimized);
            closeTray = Check(Strings.SettingsCloseToTray, s.CloseToTray);

            Header(Strings.Profiles);
            osd = Check(Strings.SettingsProfileOsd, s.ShowOsd);

            Header(Strings.SettingsSafety);
            confirmInput = Check(Strings.SettingsConfirmInput, s.ConfirmInput);
            keepInput = Check(Strings.SettingsKeepInput, s.KeepInput);
            Hint(Strings.SettingsKeepInputHint(InputWatcher.Retries));

            Header(Strings.SettingsFile);
            var link = new LinkLabel { Text = s.Path, AutoSize = true, UseMnemonic = false, MaximumSize = Dpi.Size(420, 0), Margin = Dpi.Pad(20, 2, 3, 3) };
            link.LinkClicked += delegate
            {
                try { Process.Start("explorer.exe", "/select,\"" + s.Path + "\""); }
                catch (Exception ex) { Crash.Log(ex); }
            };
            Wide(link);
            Finish();
        }

        protected override bool Commit()
        {
            if (startup.Checked != Startup.IsEnabled && !Startup.Set(startup.Checked)) Tell(Strings.AutostartFailed);
            s.StartMinimized = startMin.Checked;
            s.CloseToTray = closeTray.Checked;
            s.ShowOsd = osd.Checked;
            s.ConfirmInput = confirmInput.Checked;
            s.KeepInput = keepInput.Checked;
            s.Language = ((LanguageChoice)language.SelectedItem).Code;
            return true;
        }
    }

    // An entry of the language selectors (settings dialog and main window button).
    class LanguageChoice
    {
        public string Code, Text, Flag;
        public override string ToString() { return Text; }

        // Automatic first (with the flag of the Windows language), then English, Spanish and the classic localisation
        // order, each in its own language.
        public static List<LanguageChoice> All()
        {
            string system = Lang.SystemLanguage;
            var list = new List<LanguageChoice> { new LanguageChoice { Code = "", Flag = system, Text = Strings.LanguageAuto(Lang.NameOf(system)) } };
            foreach (var code in Lang.Order) list.Add(new LanguageChoice { Code = code, Flag = code, Text = Lang.NameOf(code) });
            return list;
        }

        // Owner-drawn combo item: flag + name (TextRenderer, so Chinese and Japanese get a fallback font).
        public static void Draw(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index >= 0)
            {
                var choice = (LanguageChoice)((ComboBox)sender).Items[e.Index];
                var flag = Flags.Get(choice.Flag, Dpi.S(12));
                e.Graphics.DrawImage(flag, e.Bounds.Left + Dpi.S(3), e.Bounds.Top + (e.Bounds.Height - flag.Height) / 2, flag.Width, flag.Height);
                var text = Rectangle.FromLTRB(e.Bounds.Left + flag.Width + Dpi.S(8), e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
                TextRenderer.DrawText(e.Graphics, choice.Text, e.Font, text, e.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            e.DrawFocusRectangle();
        }
    }

    class InputNamesForm : DialogBase
    {
        readonly Settings settings;
        readonly DdcMonitor monitor;
        readonly Dictionary<byte, TextBox> boxes = new Dictionary<byte, TextBox>();

        public InputNamesForm(Settings s, DdcMonitor m) : base(Strings.InputNamesTitle)
        {
            settings = s;
            monitor = m;
            Header(Strings.InputsOf(m.Model, m.Position));
            foreach (var o in m.InputOptions)
                boxes[o] = Field(Vcp.ValueName(Vcp.Input, o), new TextBox { Width = Dpi.S(220), MaxLength = 30, Text = s.InputName(m.Key, o) ?? "" });
            var pc = m.PcInput();
            Hint(Strings.InputNamesHint + (pc.HasValue ? " " + Strings.InputNamesPcHint(Vcp.ValueName(Vcp.Input, pc.Value)) : ""));
            Finish();
        }

        protected override bool Commit()
        {
            var names = new Dictionary<byte, string>();
            foreach (var kv in boxes)
            {
                string t = kv.Value.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                if (t.Length > 0) names[kv.Key] = t;
            }
            settings.InputNames[monitor.Key] = names;
            return true;
        }
    }

    class ProfileForm : DialogBase
    {
        class Row
        {
            public byte Code;
            public CheckBox Include;
            public NumericUpDown Number;
            public ComboBox Combo;
        }

        readonly DdcMonitor monitor;
        readonly Profile profile;
        readonly Func<Profile, string> validate;
        readonly TextBox nameBox;
        readonly List<Row> rows = new List<Row>();
        public Profile Result;

        public ProfileForm(DdcMonitor m, Profile p, Func<Profile, string> validate) : base(Strings.ProfileTitle)
        {
            monitor = m;
            profile = p;
            this.validate = validate;
            Header(Strings.ProfileFor(m.Model));
            nameBox = Field(Strings.ProfileName, new TextBox { Width = Dpi.S(220), Text = p.Name, MaxLength = 40 });

            Header(Strings.ProfileSettings);
            foreach (var code in Vcp.ProfileCodes)
            {
                if (!m.Supports(code)) continue;
                var row = new Row { Code = code };
                row.Include = new CheckBox { Text = Vcp.Name(code), AutoSize = true, Checked = p.Values.ContainsKey(code), Margin = Dpi.Pad(20, 5, 10, 3) };
                Control editor;
                if (Vcp.IsContinuous(code))
                {
                    VcpValue v;
                    int max = m.Values.TryGetValue(code, out v) && v.Max > 0 ? (int)Math.Min(v.Max, 65535) : 100;
                    row.Number = new NumericUpDown { Minimum = 0, Maximum = max, Width = Dpi.S(70) };
                    editor = row.Number;
                }
                else
                {
                    row.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.S(200) };
                    IEnumerable<byte> options = m.OptionsOf(code) ?? (code == Vcp.Mute ? new List<byte> { Vcp.MuteOff, Vcp.MuteOn } : new List<byte>());
                    foreach (var o in options) row.Combo.Items.Add(new Choice { Value = o, Text = Vcp.ValueName(code, o) });
                    editor = row.Combo;
                }
                uint value;
                if (p.Values.TryGetValue(code, out value)) SetEditor(row, value);
                else SetFromMonitor(row);
                var r = row;
                editor.Enabled = row.Include.Checked;
                row.Include.CheckedChanged += delegate { editor.Enabled = r.Include.Checked; };
                int line = Lay.Row(Body);
                Body.Controls.Add(row.Include, 0, line);
                Body.Controls.Add(editor, 1, line);
                rows.Add(row);
            }
            var take = Lay.Button(Strings.ProfileTakeCurrent, delegate { foreach (var row in rows) SetFromMonitor(row); });
            take.Margin = Dpi.Pad(20, 8, 3, 3);
            Wide(take);
            Hint(Strings.ProfileRgbHint);
            Finish();
        }

        void SetFromMonitor(Row row)
        {
            VcpValue v;
            if (monitor.Values.TryGetValue(row.Code, out v)) SetEditor(row, v.Current);
            else if (row.Number != null) row.Number.Value = Math.Min(50, row.Number.Maximum);
            else if (row.Combo != null && row.Combo.Items.Count > 0 && row.Combo.SelectedIndex < 0) row.Combo.SelectedIndex = 0;
        }

        static void SetEditor(Row row, uint value)
        {
            if (row.Number != null) row.Number.Value = Math.Min(value, (uint)row.Number.Maximum);
            if (row.Combo == null) return;
            int index = -1;
            for (int i = 0; i < row.Combo.Items.Count; i++)
                if (((Choice)row.Combo.Items[i]).Value == value) index = i;
            if (index < 0)
            {
                row.Combo.Items.Add(new Choice { Value = value, Text = Vcp.ValueName(row.Code, value) });
                index = row.Combo.Items.Count - 1;
            }
            row.Combo.SelectedIndex = index;
        }

        protected override bool Commit()
        {
            var result = new Profile { Name = Settings.CleanProfileName(nameBox.Text), Monitor = profile.Monitor };
            foreach (var row in rows)
            {
                if (!row.Include.Checked) continue;
                if (row.Number != null) result.Values[row.Code] = (uint)row.Number.Value;
                else if (row.Combo != null && row.Combo.SelectedItem != null) result.Values[row.Code] = ((Choice)row.Combo.SelectedItem).Value;
            }
            string error = result.Name.Length == 0 ? Strings.ProfileNeedsName
                : result.Values.Count == 0 ? Strings.ProfileNeedsSetting
                : validate(result);
            if (error != null)
            {
                Tell(error);
                return false;
            }
            Result = result;
            return true;
        }
    }
}
