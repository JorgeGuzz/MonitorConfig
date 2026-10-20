using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Portable settings: an INI next to the exe (falls back to %APPDATA% if that folder is read-only).
    // The key names are part of the file format and stay in Spanish.
    class Settings
    {
        public const string FileName = "MonitorConfig.ini";

        public bool StartMinimized;
        public bool CloseToTray = true;
        public bool ShowOsd = true;
        public bool ConfirmInput = true;
        public bool KeepInput = true;
        public string Language = "";   // "" = the Windows display language
        public string ActiveMonitor = "";
        public int WindowX = int.MinValue, WindowY = int.MinValue;
        public readonly List<Profile> Profiles = new List<Profile>();
        public readonly Dictionary<string, string> CapsCache = new Dictionary<string, string>();
        public readonly Dictionary<string, Dictionary<byte, string>> InputNames = new Dictionary<string, Dictionary<byte, string>>();
        public string Path;
        public string LastError;

        static string ExeDir { get { return System.IO.Path.GetDirectoryName(Application.ExecutablePath); } }
        static string AppDataPath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorConfig", FileName); }
        }

        // path: a specific file (developer tools); null = the portable location.
        public static Settings Load(string path = null)
        {
            if (path == null)
            {
                string local = System.IO.Path.Combine(ExeDir, FileName);
                path = File.Exists(local) ? local : (File.Exists(AppDataPath) ? AppDataPath : local);
            }
            var s = new Settings { Path = path };
            if (File.Exists(path))
            {
                try { s.Parse(File.ReadAllText(path, Encoding.UTF8)); }
                catch (Exception ex) { s.LastError = ex.Message; }
            }
            return s;
        }

        public void Parse(string text)
        {
            string section = "";
            Profile profile = null;
            Dictionary<byte, string> inputs = null;
            foreach (var rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2);
                    profile = null;
                    inputs = null;
                    if (section.StartsWith("Entradas:", StringComparison.OrdinalIgnoreCase))
                    {
                        inputs = new Dictionary<byte, string>();
                        InputNames[section.Substring(9)] = inputs;
                    }
                    if (section.StartsWith("Perfil:", StringComparison.OrdinalIgnoreCase))
                    {
                        profile = new Profile { Name = section.Substring(7) };
                        Profiles.Add(profile);
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();

                if (inputs != null)
                {
                    int code;
                    if (TryCode(key, out code) && value.Length > 0) inputs[(byte)code] = value;
                }
                else if (profile != null)
                {
                    int code;
                    uint v;
                    if (key.Equals("Monitor", StringComparison.OrdinalIgnoreCase)) profile.Monitor = value;
                    else if (TryCode(key, out code) && uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                        profile.Values[(byte)code] = v;
                }
                else if (section.StartsWith("Capacidades", StringComparison.OrdinalIgnoreCase))
                {
                    CapsCache[key] = value;
                }
                else if (section.Equals("General", StringComparison.OrdinalIgnoreCase))
                {
                    switch (key)
                    {
                        case "IniciarMinimizado": StartMinimized = value == "1"; break;
                        case "CerrarALaBandeja": CloseToTray = value == "1"; break;
                        case "IndicadorEnPantalla": ShowOsd = value == "1"; break;
                        case "ConfirmarEntrada": ConfirmInput = value == "1"; break;
                        case "MantenerEntrada": KeepInput = value == "1"; break;
                        case "Monitor": ActiveMonitor = value; break;
                        case "Idioma": Language = value; break;
                        case "VentanaX": WindowX = ParseInt(value, int.MinValue); break;
                        case "VentanaY": WindowY = ParseInt(value, int.MinValue); break;
                    }
                }
            }
        }

        static bool TryCode(string key, out int code)
        {
            return int.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code) && code >= 0 && code <= 0xFF;
        }

        static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("; MonitorConfig\r\n");
            sb.Append("[General]\r\n");
            Line(sb, "IniciarMinimizado", StartMinimized ? "1" : "0");
            Line(sb, "CerrarALaBandeja", CloseToTray ? "1" : "0");
            Line(sb, "IndicadorEnPantalla", ShowOsd ? "1" : "0");
            Line(sb, "ConfirmarEntrada", ConfirmInput ? "1" : "0");
            Line(sb, "MantenerEntrada", KeepInput ? "1" : "0");
            Line(sb, "Monitor", ActiveMonitor);
            Line(sb, "Idioma", Language);
            if (WindowX != int.MinValue) Line(sb, "VentanaX", WindowX.ToString(CultureInfo.InvariantCulture));
            if (WindowY != int.MinValue) Line(sb, "VentanaY", WindowY.ToString(CultureInfo.InvariantCulture));
            foreach (var p in Profiles)
            {
                sb.Append("\r\n[Perfil:").Append(p.Name).Append("]\r\n");
                Line(sb, "Monitor", p.Monitor);
                foreach (var kv in p.Values) Line(sb, kv.Key.ToString("X2"), kv.Value.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var kv in InputNames)
            {
                if (kv.Value.Count == 0) continue;
                sb.Append("\r\n[Entradas:").Append(kv.Key).Append("]\r\n");
                foreach (var name in kv.Value.OrderBy(x => x.Key)) Line(sb, name.Key.ToString("X2"), name.Value);
            }
            if (CapsCache.Count > 0)
            {
                sb.Append("\r\n[Capacidades]\r\n");
                foreach (var kv in CapsCache) Line(sb, kv.Key, kv.Value);
            }
            return sb.ToString();
        }

        static void Line(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append('=').Append((value ?? "").Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
        }

        public bool Save()
        {
            string text = Serialize();
            foreach (var candidate in new[] { Path, AppDataPath })
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(candidate));
                    string tmp = candidate + ".tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    if (!File.Exists(candidate)) File.Move(tmp, candidate);
                    else
                    {
                        // Atomic swap: a power cut never leaves the folder without settings.
                        try { File.Replace(tmp, candidate, null); }
                        catch (PlatformNotSupportedException) { File.Delete(candidate); File.Move(tmp, candidate); }
                    }
                    Path = candidate;
                    LastError = null;
                    return true;
                }
                catch (Exception ex) { LastError = ex.Message; }
            }
            return false;
        }

        // Moves everything stored under a monitor's old key to its new one. Returns true if something moved.
        public bool MigrateMonitorKey(string legacy, string key)
        {
            if (legacy == key) return false;
            bool changed = false;
            Dictionary<byte, string> names;
            if (InputNames.TryGetValue(legacy, out names))
            {
                InputNames.Remove(legacy);
                if (!InputNames.ContainsKey(key)) InputNames[key] = names;
                changed = true;
            }
            foreach (var p in Profiles)
                if (p.Monitor == legacy) { p.Monitor = key; changed = true; }
            if (ActiveMonitor == legacy) { ActiveMonitor = key; changed = true; }
            string caps;
            if (CapsCache.TryGetValue(legacy, out caps))
            {
                CapsCache.Remove(legacy);
                if (!CapsCache.ContainsKey(key)) CapsCache[key] = caps;
                changed = true;
            }
            return changed;
        }

        public string InputName(string monitor, byte code)
        {
            Dictionary<byte, string> names;
            string name;
            return InputNames.TryGetValue(monitor, out names) && names.TryGetValue(code, out name) ? name : null;
        }

        // Profile names end up inside "[Perfil:...]": keep them on one line and without brackets.
        public static string CleanProfileName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
                if (c != '[' && c != ']' && c != '\r' && c != '\n' && c != '=') sb.Append(c);
            return sb.ToString().Trim();
        }
    }
}
