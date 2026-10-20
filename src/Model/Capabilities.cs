using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MonitorConfig
{
    class VcpValue
    {
        public uint Current;
        public uint Max;
        public uint Type;

        public int Percent { get { return Vcp.Percent(Current, Max); } }
    }

    // Parsed DDC/CI capabilities string, e.g. "(prot(monitor)type(LCD)model(XG2705)vcp(02 04 10 14(01 04) ...)mccs_ver(2.1))".
    class Capabilities
    {
        public string Raw = "";
        public string Model;
        public string MccsVersion;
        public string Firmware;
        public readonly List<byte> Codes = new List<byte>();
        public readonly List<byte> Commands = new List<byte>();   // cmds(...): 0C = save current settings
        public readonly Dictionary<byte, List<byte>> Values = new Dictionary<byte, List<byte>>();

        public List<byte> ValuesOf(byte code)
        {
            List<byte> list;
            return Values.TryGetValue(code, out list) ? list : null;
        }

        public static Capabilities Parse(string raw)
        {
            var caps = new Capabilities();
            caps.Raw = (raw ?? "").Trim('\0', ' ', '\r', '\n');
            string s = caps.Raw;
            int i = s.StartsWith("(") ? 1 : 0;
            while (i < s.Length)
            {
                int start = i;
                while (i < s.Length && s[i] != '(' && s[i] != ')') i++;
                if (i >= s.Length) break;
                if (s[i] == ')') { i++; continue; }
                // The key is the identifier right before '(' (some monitors glue words: "type(lcd)MStarcmds(...)").
                string key = Regex.Match(s.Substring(start, i - start), @"[A-Za-z0-9_]*$").Value.ToLowerInvariant();
                int open = i, depth = 0;
                for (; i < s.Length; i++)
                {
                    if (s[i] == '(') depth++;
                    else if (s[i] == ')' && --depth == 0) break;
                }
                string content = s.Substring(open + 1, Math.Min(i, s.Length) - open - 1);
                i++;
                if (key.EndsWith("vcp")) ParseVcp(content, caps);
                else if (key.EndsWith("model")) caps.Model = content.Trim();
                else if (key.EndsWith("cmds"))
                    foreach (Match m in Regex.Matches(content, "[0-9A-Fa-f]{2}")) caps.Commands.Add(Convert.ToByte(m.Value, 16));
                else if (key == "mccs_ver") caps.MccsVersion = content.Trim();
                else if (key == "mpu_ver" || key == "mpu" || key == "fw_ver" || key == "firmware") caps.Firmware = content.Trim();
            }
            return caps;
        }

        static void ParseVcp(string content, Capabilities caps)
        {
            int last = -1, i = 0;
            while (i < content.Length)
            {
                char c = content[i];
                if (c == '(')
                {
                    int depth = 1, j = i + 1;
                    for (; j < content.Length && depth > 0; j++)
                    {
                        if (content[j] == '(') depth++;
                        else if (content[j] == ')') depth--;
                    }
                    int end = depth == 0 ? j - 1 : j;
                    if (last >= 0)
                    {
                        var list = new List<byte>();
                        foreach (Match m in Regex.Matches(content.Substring(i + 1, end - i - 1), "[0-9A-Fa-f]{2}"))
                        {
                            byte b = Convert.ToByte(m.Value, 16);
                            if (!list.Contains(b)) list.Add(b);
                        }
                        caps.Values[(byte)last] = list;
                    }
                    i = j;
                    continue;
                }
                if (IsHex(c) && i + 1 < content.Length && IsHex(content[i + 1]))
                {
                    last = Convert.ToInt32(content.Substring(i, 2), 16);
                    if (!caps.Codes.Contains((byte)last)) caps.Codes.Add((byte)last);
                    i += 2;
                    continue;
                }
                i++;
            }
        }

        static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
