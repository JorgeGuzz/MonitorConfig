namespace MonitorConfig
{
    // What is shown under each input: the user's name for it, or «Este PC» for the input this PC is plugged into.
    class InputLabels
    {
        readonly Settings settings;

        public InputLabels(Settings settings) { this.settings = settings; }

        public string Label(DdcMonitor m, byte code)
        {
            string name = settings.InputName(m.Key, code);
            if (!string.IsNullOrEmpty(name)) return name;
            return m.PcInput() == code ? Strings.ThisPc : "";
        }

        // "HDMI 1 · Switch"
        public string Text(DdcMonitor m, byte code)
        {
            string label = Label(m, code);
            return Vcp.ValueName(Vcp.Input, code) + (label.Length > 0 ? " · " + label : "");
        }
    }
}
