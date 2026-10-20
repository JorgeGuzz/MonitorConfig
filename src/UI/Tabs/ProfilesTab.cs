using System;
using System.Linq;
using System.Windows.Forms;

namespace MonitorConfig
{
    // The selected monitor's profiles: apply, create, edit, delete.
    class ProfilesTab : MonitorTab
    {
        readonly ListView list;
        readonly Button applyButton, editButton, deleteButton;

        public ProfilesTab(TrayApp app) : base(app, Strings.Profiles)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = Dpi.Pad(10) };
            t.SuspendLayout();
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill };
            list.Columns.Add(Strings.ProfileColumn, Dpi.S(160));
            list.Columns.Add(Strings.SettingsColumn, Dpi.S(420));
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            list.ItemActivate += delegate { ApplySelected(); };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Dpi.Pad(8, 0, 0, 0) };
            applyButton = Lay.Button(Strings.Apply, delegate { ApplySelected(); });
            var newButton = Lay.Button(Strings.NewProfile, delegate { Create(); });
            editButton = Lay.Button(Strings.EditProfile, delegate { Edit(); });
            deleteButton = Lay.Button(Strings.DeleteProfile, delegate { Delete(); });
            foreach (var b in new[] { applyButton, newButton, editButton, deleteButton })
            {
                b.AutoSize = false;
                b.Size = Dpi.Size(110, 28);
                buttons.Controls.Add(b);
            }
            t.Controls.Add(list, 0, 0);
            t.Controls.Add(buttons, 1, 0);
            var note = Lay.Note(Strings.ProfilesNote);
            t.Controls.Add(note, 0, 1);
            t.SetColumnSpan(note, 2);
            Controls.Add(t);
        }

        public override void Bind(DdcMonitor m)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var p in App.Settings.Profiles)
            {
                if (m != null && p.Monitor.Length > 0 && p.Monitor != m.Key) continue;
                var item = new ListViewItem(p.Name) { Tag = p };
                item.SubItems.Add(p.Summary());
                list.Items.Add(item);
            }
            list.EndUpdate();
            UpdateButtons();
        }

        Profile Selected { get { return list.SelectedItems.Count > 0 ? (Profile)list.SelectedItems[0].Tag : null; } }

        void UpdateButtons()
        {
            applyButton.Enabled = editButton.Enabled = deleteButton.Enabled = Selected != null;
        }

        void ApplySelected()
        {
            var p = Selected;
            if (p != null) App.Profiles.Apply(p);
        }

        void Create()
        {
            var m = M;
            if (m == null || !m.Responsive) return;
            int n = 1;
            string name;
            do { name = Strings.ProfileDefaultName(n++); } while (App.Settings.Profiles.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            var p = new Profile { Name = name, Monitor = m.Key };
            VcpValue preset;
            bool user = m.Values.TryGetValue(Vcp.ColorPreset, out preset) && preset.Current == Vcp.PresetUser1;
            var codes = user
                ? new[] { Vcp.Brightness, Vcp.Contrast, Vcp.Sharpness, Vcp.ColorPreset, Vcp.RedGain, Vcp.GreenGain, Vcp.BlueGain }
                : new[] { Vcp.Brightness, Vcp.Contrast, Vcp.Sharpness, Vcp.ColorPreset };
            foreach (var code in codes)
            {
                VcpValue v;
                if (m.Supports(code) && m.Values.TryGetValue(code, out v)) p.Values[code] = v.Current;
            }
            var result = App.EditProfile(FindForm(), m, p, null);
            if (result == null) return;
            App.Settings.Profiles.Add(result);
            App.SaveSettings();
            Bind(M);
        }

        void Edit()
        {
            var p = Selected;
            if (p == null || M == null) return;
            var result = App.EditProfile(FindForm(), M, p.Clone(), p);
            if (result == null) return;
            int index = App.Settings.Profiles.IndexOf(p);
            if (index >= 0) App.Settings.Profiles[index] = result;
            App.SaveSettings();
            Bind(M);
        }

        void Delete()
        {
            var p = Selected;
            if (p == null || !Confirm(Strings.DeleteProfileQuestion(p.Name), Strings.DeleteProfileTitle, MessageBoxIcon.Question)) return;
            App.Settings.Profiles.Remove(p);
            App.SaveSettings();
            Bind(M);
        }
    }
}
