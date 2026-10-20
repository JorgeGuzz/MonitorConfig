using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Dark colours for the main window and the dialogs. WinForms has no dark mode, so every control is recoloured here
    // (also those added later), the title bar is asked to DWM, menus get a dark renderer, and the tab and list headers
    // are drawn by hand. Message boxes, date pickers and up/down arrows keep the system look.
    static class DarkMode
    {
        public static readonly Color Back = Color.FromArgb(32, 32, 32), Panel = Color.FromArgb(43, 43, 43), Field = Color.FromArgb(48, 48, 48),
            Border = Color.FromArgb(80, 80, 80), Hover = Color.FromArgb(62, 62, 62), Text = Color.FromArgb(235, 235, 235),
            Dim = Color.FromArgb(160, 160, 160), Link = Color.FromArgb(110, 170, 255), Error = Color.FromArgb(255, 120, 110);

        // Decided when a window is built (windows are rebuilt when the theme changes).
        public static bool On { get { return Theme.AppDark; } }

        public static Color TextColor { get { return On ? Text : SystemColors.ControlText; } }
        public static Color ErrorColor { get { return On ? Error : Color.Firebrick; } }

        static readonly ConditionalWeakTable<Control, object> hooked = new ConditionalWeakTable<Control, object>();
        static readonly ToolStripRenderer renderer = new DarkRenderer(), light = new ToolStripProfessionalRenderer();

        static readonly System.Collections.Generic.Dictionary<string, Bitmap> suns = new System.Collections.Generic.Dictionary<string, Bitmap>();

        // The theme button's icon: a lit, yellow sun in light mode and an unlit, outlined one in dark mode
        // (drawn 4x larger and reduced, like the flags).
        public static Bitmap Sun(bool dark, int size)
        {
            string key = dark + "@" + size;
            Bitmap sun;
            if (suns.TryGetValue(key, out sun)) return sun;
            const int k = 4;
            float s = size * k, c = s / 2;
            sun = new Bitmap(size, size);
            using (var big = new Bitmap(size * k, size * k))
            {
                using (var g = Graphics.FromImage(big))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var line = dark ? Color.FromArgb(185, 185, 185) : Color.FromArgb(225, 145, 0);
                    using (var pen = new Pen(line, s * 0.09f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    {
                        for (int i = 0; i < 8; i++)   // rays
                        {
                            double a = i * Math.PI / 4;
                            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                            g.DrawLine(pen, c + cos * s * 0.35f, c + sin * s * 0.35f, c + cos * s * 0.46f, c + sin * s * 0.46f);
                        }
                        float r = s * 0.22f;
                        using (var fill = new SolidBrush(dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(255, 196, 0)))
                            g.FillEllipse(fill, c - r, c - r, 2 * r, 2 * r);
                        pen.Width = s * 0.07f;
                        g.DrawEllipse(pen, c - r, c - r, 2 * r, 2 * r);
                    }
                }
                using (var g = Graphics.FromImage(sun))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(big, new Rectangle(0, 0, size, size));
                }
            }
            suns[key] = sun;
            return sun;
        }

        // The tray menu and its submenus, dark or light.
        public static void StyleMenu(ToolStrip strip, bool dark)
        {
            strip.Renderer = dark ? renderer : light;
            foreach (ToolStripItem item in strip.Items)
            {
                var drop = item as ToolStripDropDownItem;
                if (drop != null) StyleMenu(drop.DropDown, dark);
            }
        }

        public static void Apply(Control c)
        {
            if (!On) return;
            Style(c);
            foreach (Control child in c.Controls) Apply(child);
            object unused;
            if (!hooked.TryGetValue(c, out unused))
            {
                hooked.Add(c, null);
                c.ControlAdded += delegate(object sender, ControlEventArgs e) { Apply(e.Control); };
            }
        }

        static void Style(Control c)
        {
            if (c is LinkLabel)
            {
                var l = (LinkLabel)c;
                l.ForeColor = Text;
                l.LinkColor = l.ActiveLinkColor = l.VisitedLinkColor = Link;
                l.DisabledLinkColor = Dim;
            }
            else if (c is Label)
            {
                var l = (Label)c;
                if (l.BorderStyle == BorderStyle.Fixed3D && !l.AutoSize)   // Lay.Separator
                {
                    l.BorderStyle = BorderStyle.None;
                    l.Height = 1;
                    l.BackColor = Border;
                }
                else l.ForeColor = l.ForeColor == SystemColors.GrayText ? Dim : l.ForeColor == Color.Firebrick ? Error : Text;
            }
            else if (LooksLikeButton(c))
            {
                var b = (ButtonBase)c;
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Panel;
                b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = Hover;
                b.FlatAppearance.MouseDownBackColor = Border;
                b.FlatAppearance.CheckedBackColor = Theme.Accent;
                b.UseVisualStyleBackColor = false;
            }
            else if (c is CheckBox || c is RadioButton) c.ForeColor = Text;
            else if (c is TextBoxBase || c is NumericUpDown || c is ListBox)
            {
                c.BackColor = Field;
                c.ForeColor = Text;
                var box = c as TextBox;   // the 3D borders come out white
                if (box != null)
                {
                    box.BorderStyle = BorderStyle.FixedSingle;
                    // A read-only box paints once with the system colours while its window is being created: again.
                    if (box.ReadOnly) box.HandleCreated += delegate { box.BeginInvoke((Action)box.Invalidate); };
                }
                var number = c as NumericUpDown;
                if (number != null) number.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is ComboBox)
            {
                var cb = (ComboBox)c;
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Field;
                cb.ForeColor = Text;
                if (cb.DrawMode == DrawMode.Normal)   // a flat drop-down list ignores BackColor: drawn here
                {
                    cb.DrawMode = DrawMode.OwnerDrawFixed;
                    cb.DrawItem += DrawComboItem;
                }
            }
            else if (c is ListView) StyleList((ListView)c);
            else if (c is TrackBar) c.BackColor = c.Parent != null ? c.Parent.BackColor : Back;
            else if (c is ToolStrip)
            {
                var strip = (ToolStrip)c;
                strip.Renderer = renderer;
                strip.BackColor = Panel;
                strip.ForeColor = Text;
                foreach (ToolStripItem item in strip.Items) StyleItem(item);
            }
            else if (c is TabPage)
            {
                var page = (TabPage)c;
                page.UseVisualStyleBackColor = false;
                page.BackColor = Back;
                page.ForeColor = Text;
            }
            else if (c is DateTimePicker) { }   // keeps the system look
            else
            {
                c.BackColor = Back;
                c.ForeColor = Text;
                if (c is ScrollableControl) Themed(c, "DarkMode_Explorer");   // dark scrollbars
            }
        }

        // Buttons, and check/radio boxes drawn as buttons (the input selector).
        static bool LooksLikeButton(Control c)
        {
            var check = c as CheckBox;
            if (check != null) return check.Appearance == Appearance.Button;
            var radio = c as RadioButton;
            if (radio != null) return radio.Appearance == Appearance.Button;
            return c is ButtonBase;
        }

        static void StyleItem(ToolStripItem item)
        {
            item.ForeColor = Text;
            var drop = item as ToolStripDropDownItem;
            if (drop == null) return;
            drop.DropDown.Renderer = renderer;
            foreach (ToolStripItem child in drop.DropDownItems) StyleItem(child);
        }

        static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            var cb = (ComboBox)sender;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(selected ? Theme.Accent : Field)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index < 0) return;
            var text = Rectangle.FromLTRB(e.Bounds.Left + Dpi.S(2), e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
            TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), e.Font, text, !cb.Enabled ? Dim : selected ? Color.White : Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        // Body coloured; header drawn here (its empty end is darkened by the theme); dark scrollbars.
        static void StyleList(ListView list)
        {
            list.BackColor = Field;
            list.ForeColor = Text;
            list.BorderStyle = BorderStyle.FixedSingle;
            Themed(list, "DarkMode_Explorer");
            if (list.View != View.Details || list.OwnerDraw) return;
            list.OwnerDraw = true;
            list.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
            list.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
            list.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (var b = new SolidBrush(Panel)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Border)) e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 3, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                var text = Rectangle.FromLTRB(e.Bounds.Left + Dpi.S(5), e.Bounds.Top, e.Bounds.Right - Dpi.S(3), e.Bounds.Bottom);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, text, Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            Action header = delegate
            {
                IntPtr h = SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (h != IntPtr.Zero) SetWindowTheme(h, "DarkMode_ItemsView", null);
            };
            if (list.IsHandleCreated) header();
            else list.HandleCreated += delegate { header(); };
        }

        static void Themed(Control c, string theme)
        {
            if (c.IsHandleCreated) SetWindowTheme(c.Handle, theme, null);
            else c.HandleCreated += delegate { SetWindowTheme(c.Handle, theme, null); };
        }

        // Dark title bar (Windows 10 1809 and later; attribute 19 before build 18985, 20 since).
        public static void TitleBar(Form f)
        {
            if (!On) return;
            int yes = 1;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref yes, sizeof(int)) != 0) DwmSetWindowAttribute(f.Handle, 19, ref yes, sizeof(int));
            }
            catch (Exception ex) { Crash.Log(ex); }   // older Windows: the title bar stays light
        }

        const int LVM_GETHEADER = 0x101F;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? (e.Item.ForeColor == Color.Firebrick || e.Item.ForeColor == Error ? Error : Text) : Dim;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = e.Item.Enabled ? Text : Dim;
                base.OnRenderArrow(e);
            }
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color MenuStripGradientBegin { get { return Panel; } }
            public override Color MenuStripGradientEnd { get { return Panel; } }
            public override Color StatusStripGradientBegin { get { return Panel; } }
            public override Color StatusStripGradientEnd { get { return Panel; } }
            public override Color ToolStripDropDownBackground { get { return Panel; } }
            public override Color ToolStripBorder { get { return Panel; } }
            public override Color ToolStripGradientBegin { get { return Panel; } }
            public override Color ToolStripGradientMiddle { get { return Panel; } }
            public override Color ToolStripGradientEnd { get { return Panel; } }
            public override Color ImageMarginGradientBegin { get { return Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Panel; } }
            public override Color ImageMarginGradientEnd { get { return Panel; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Border; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Field; } }
            public override Color MenuItemPressedGradientMiddle { get { return Field; } }
            public override Color MenuItemPressedGradientEnd { get { return Field; } }
            public override Color ButtonSelectedHighlight { get { return Hover; } }
            public override Color ButtonSelectedGradientBegin { get { return Hover; } }
            public override Color ButtonSelectedGradientMiddle { get { return Hover; } }
            public override Color ButtonSelectedGradientEnd { get { return Hover; } }
            public override Color ButtonSelectedBorder { get { return Border; } }
            public override Color ButtonPressedGradientBegin { get { return Field; } }
            public override Color ButtonPressedGradientMiddle { get { return Field; } }
            public override Color ButtonPressedGradientEnd { get { return Field; } }
            public override Color CheckBackground { get { return Color.FromArgb(0, 84, 153); } }
            public override Color CheckSelectedBackground { get { return Color.FromArgb(0, 100, 180); } }
            public override Color CheckPressedBackground { get { return Color.FromArgb(0, 100, 180); } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Panel; } }
        }
    }

    // Tab headers drawn here in dark mode (the native ones only come light); the pages colour themselves.
    class ThemedTabControl : TabControl
    {
        public ThemedTabControl()
        {
            if (DarkMode.On) SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!GetStyle(ControlStyles.UserPaint)) { base.OnPaint(e); return; }
            e.Graphics.Clear(DarkMode.Back);
            using (var border = new Pen(DarkMode.Border))
            {
                var page = DisplayRectangle;
                page.Inflate(Dpi.S(2), Dpi.S(2));
                e.Graphics.DrawRectangle(border, page.X, page.Y, page.Width - 1, page.Height - 1);
                for (int i = 0; i < TabCount; i++)
                {
                    var r = GetTabRect(i);
                    bool selected = i == SelectedIndex;
                    if (selected) r.Inflate(0, Dpi.S(1));
                    using (var b = new SolidBrush(selected ? DarkMode.Back : DarkMode.Panel)) e.Graphics.FillRectangle(b, r);
                    e.Graphics.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - 1);
                    if (selected)
                        using (var cover = new Pen(DarkMode.Back)) e.Graphics.DrawLine(cover, r.Left + 1, r.Bottom - 1, r.Right - 2, r.Bottom - 1);
                    TextRenderer.DrawText(e.Graphics, TabPages[i].Text, Font, r, selected ? DarkMode.Text : DarkMode.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}
