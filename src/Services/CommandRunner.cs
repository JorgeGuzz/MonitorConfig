using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MonitorConfig
{
    // Command-line control, for scripts, shortcuts or a Stream Deck. Runs inside the copy already open (CommandServer)
    // or, when none is, in a short-lived one (CommandLine). The text it prints is English, as command-line tools are.
    static class CommandRunner
    {
        public class Result
        {
            public int Code;   // 0 done, 1 nothing could be done, 2 wrong command line
            public string Text;
        }

        public const string Usage =
            "MonitorConfig [--monitor <which>] <changes>\n" +
            "\n" +
            "  --brightness <n|+n|-n>   brightness in %, or a change of n points\n" +
            "  --contrast <n|+n|-n>     contrast in %\n" +
            "  --volume <n|+n|-n>       speaker volume in %\n" +
            "  --mute <on|off|toggle>   speaker mute\n" +
            "  --input <name>           input: DisplayPort, HDMI1, HDMI2, USB-C... or a name you gave it\n" +
            "  --set <code>=<value>     any VCP code (hex) to a raw value, e.g. --set 10=50\n" +
            "  --profile <name>         applies a saved profile\n" +
            "  --monitor <which>        model (XG2705), position from the left (1, 2) or key; default: all\n" +
            "  --list                   monitors, their values and profiles\n" +
            "\n" +
            "Exit code: 0 done, 1 no monitor could do it, 2 wrong command line.";

        static readonly string[] ValueOptions = { "--brightness", "--contrast", "--volume", "--mute", "--input", "--set", "--profile", "--monitor" };
        static readonly string[] FlagOptions = { "--list", "--help", "/?", "-h" };

        public static bool IsCommand(string[] args)
        {
            return args.Any(a => ValueOptions.Contains(a.ToLowerInvariant()) || FlagOptions.Contains(a.ToLowerInvariant()));
        }

        // The values a command starts from (relative changes, toggles): a fresh process reads them first.
        public static byte[] CodesNeeded(string[] args)
        {
            var codes = new List<byte> { Vcp.Brightness, Vcp.Volume, Vcp.Mute, Vcp.Input };
            if (args.Any(a => a.Equals("--contrast", StringComparison.OrdinalIgnoreCase))) codes.Add(Vcp.Contrast);
            return codes.ToArray();
        }

        class Change
        {
            public string Option, Value;
        }

        public static Result Run(string[] args, MonitorService service, ProfileService profiles, InputLabels labels, Settings settings)
        {
            string which = null;
            bool list = false;
            var changes = new List<Change>();
            for (int i = 0; i < args.Length; i++)
            {
                string option = args[i].ToLowerInvariant();
                if (FlagOptions.Contains(option))
                {
                    if (option == "--list") list = true;
                    else return Done(0, Usage);
                    continue;
                }
                if (!ValueOptions.Contains(option)) return Done(2, "Unknown option: " + args[i] + "\n\n" + Usage);
                if (i + 1 >= args.Length) return Done(2, args[i] + " needs a value.");
                string value = args[++i];
                if (option == "--monitor") which = value;
                else changes.Add(new Change { Option = option, Value = value });
            }

            var responsive = service.All.Where(m => m.Responsive).ToList();
            if (list) return Done(0, List(service, labels, settings));
            if (changes.Count == 0) return Done(2, Usage);

            List<DdcMonitor> targets;
            if (which == null) targets = responsive;
            else
            {
                var m = Find(service.All, which);
                if (m == null) return Done(2, "No monitor matches \"" + which + "\". Monitors: " + Names(service.All) + ".");
                if (!m.Responsive) return Done(1, m.Model + " does not answer over DDC/CI.");
                targets = new List<DdcMonitor> { m };
            }
            if (targets.Count == 0 && changes.Any(c => c.Option != "--profile")) return Done(1, "No monitor answers over DDC/CI.");

            var output = new StringBuilder();
            foreach (var change in changes)
            {
                string error = change.Option == "--profile"
                    ? ApplyProfile(change.Value, which == null ? null : targets, profiles, settings, output)
                    : Apply(change, targets, service, labels, output);
                if (error != null) return Done(error.StartsWith("!") ? 1 : 2, output + error.TrimStart('!'));
            }
            return Done(0, output.ToString().TrimEnd());
        }

        static Result Done(int code, string text)
        {
            return new Result { Code = code, Text = text };
        }

        // Model or EDID name, position from the left (1, 2...), or the settings key; a unique start of a model too.
        static DdcMonitor Find(List<DdcMonitor> all, string which)
        {
            int index;
            if (int.TryParse(which, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && index >= 1 && index <= all.Count) return all[index - 1];
            var exact = all.Where(m => Same(m.Model, which) || Same(m.EdidName, which) || Same(m.Key, which)).ToList();
            if (exact.Count == 1) return exact[0];
            var start = all.Where(m => (m.Model ?? "").StartsWith(which, StringComparison.OrdinalIgnoreCase)).ToList();
            return start.Count == 1 ? start[0] : null;
        }

        static bool Same(string a, string b)
        {
            return string.Equals(a ?? "", b, StringComparison.OrdinalIgnoreCase);
        }

        static string Names(List<DdcMonitor> all)
        {
            return all.Count == 0 ? "none" : string.Join(", ", all.Select((m, i) => (i + 1) + " = " + m.Model).ToArray());
        }

        // Letters and digits only, lower case: "HDMI 1" = "hdmi1", "USB-C" = "usbc".
        static string Simple(string s)
        {
            return new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        // null = done; otherwise the error ("!" first: the monitor, not the command line, is the problem).
        static string Apply(Change change, List<DdcMonitor> targets, MonitorService service, InputLabels labels, StringBuilder output)
        {
            switch (change.Option)
            {
                case "--brightness": return Level(Vcp.Brightness, "brightness", change.Value, targets, service, output);
                case "--contrast": return Level(Vcp.Contrast, "contrast", change.Value, targets, service, output);
                case "--volume": return Level(Vcp.Volume, "volume", change.Value, targets, service, output);
                case "--mute":
                {
                    string v = change.Value.ToLowerInvariant();
                    if (v != "on" && v != "off" && v != "toggle") return "--mute takes on, off or toggle.";
                    foreach (var m in Supporting(targets, Vcp.Mute, output, "mute"))
                    {
                        VcpValue now;
                        bool on = v == "on" || (v == "toggle" && !(m.Values.TryGetValue(Vcp.Mute, out now) && now.Current == Vcp.MuteOn));
                        service.Set(m, Vcp.Mute, on ? Vcp.MuteOn : Vcp.MuteOff);
                        output.AppendLine(m.Model + ": mute " + (on ? "on" : "off"));
                    }
                    return null;
                }
                case "--input":
                {
                    if (targets.Count != 1) return "--input needs --monitor when several monitors are connected (" + Names(targets) + ").";
                    var m = targets[0];
                    string wanted = Simple(change.Value);
                    if (wanted.Length == 0) return "--input needs an input name.";
                    if (wanted == "dp") wanted = "displayport";
                    byte? found = null;
                    foreach (var o in m.InputOptions)
                        if (Simple(Vcp.ValueName(Vcp.Input, o)) == wanted || Simple(labels.Label(m, o)) == wanted || Simple("0x" + o.ToString("X2")) == wanted)
                            found = o;
                    if (found == null)
                        return "The " + m.Model + " has no input \"" + change.Value + "\". Inputs: "
                            + string.Join(", ", m.InputOptions.Select(o => labels.Text(m, o)).ToArray()) + ".";
                    service.Set(m, Vcp.Input, found.Value);
                    output.AppendLine(m.Model + ": input " + labels.Text(m, found.Value));
                    return null;
                }
                case "--set":
                {
                    var parts = change.Value.Split('=');
                    int vcp;
                    uint raw;
                    string hex = parts[0].Trim();
                    if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
                    if (parts.Length != 2 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vcp) || vcp < 0 || vcp > 0xFF
                        || !uint.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out raw) || raw > 0xFFFF)
                        return "--set takes <hex code>=<value>, for example --set 10=50.";
                    foreach (var m in Supporting(targets, (byte)vcp, output, "code " + vcp.ToString("X2")))
                    {
                        service.Set(m, (byte)vcp, raw);
                        output.AppendLine(m.Model + ": " + vcp.ToString("X2") + " " + Vcp.Name((byte)vcp) + " = " + raw);
                    }
                    return null;
                }
            }
            return "Unknown option " + change.Option;
        }

        // Monitors that have this control; the others are named in the output and skipped.
        static List<DdcMonitor> Supporting(List<DdcMonitor> targets, byte code, StringBuilder output, string what)
        {
            var yes = targets.Where(m => m.Supports(code)).ToList();
            foreach (var m in targets.Except(yes)) output.AppendLine(m.Model + ": has no " + what + " control (skipped)");
            return yes;
        }

        static string Level(byte code, string name, string value, List<DdcMonitor> targets, MonitorService service, StringBuilder output)
        {
            int amount;
            bool relative = value.StartsWith("+") || value.StartsWith("-");
            if (!int.TryParse(value.TrimEnd('%'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount) || (!relative && (amount < 0 || amount > 100)))
                return "--" + name + " takes a percentage (0-100) or a change such as +10 or -10.";
            var monitors = Supporting(targets, code, output, name);
            if (monitors.Count == 0) return "!No monitor has a " + name + " control.";
            foreach (var m in monitors)
            {
                int percent = amount;
                if (relative)
                {
                    VcpValue now;
                    if (!m.Values.TryGetValue(code, out now))
                    {
                        output.AppendLine(m.Model + ": current " + name + " not known yet (skipped)");
                        continue;
                    }
                    percent = Math.Max(0, Math.Min(100, now.Percent + amount));
                }
                service.Set(m, code, service.ValueForPercent(m, code, percent));
                output.AppendLine(m.Model + ": " + name + " " + percent + " %");
            }
            return null;
        }

        // A profile by name; with --monitor, only that monitor's. Profiles of disconnected monitors are skipped.
        static string ApplyProfile(string name, List<DdcMonitor> only, ProfileService profiles, Settings settings, StringBuilder output)
        {
            var matches = settings.Profiles.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (only != null) matches = matches.Where(p => only.Contains(profiles.MonitorFor(p))).ToList();
            if (matches.Count == 0)
                return "No profile called \"" + name + "\". Profiles: "
                    + (settings.Profiles.Count == 0 ? "none" : string.Join(", ", settings.Profiles.Select(p => p.Name).Distinct().ToArray())) + ".";
            bool any = false;
            foreach (var p in matches)
            {
                var m = profiles.MonitorFor(p);
                if (profiles.Apply(p)) { any = true; output.AppendLine(m.Model + ": profile \"" + p.Name + "\""); }
                else output.AppendLine("Profile \"" + p.Name + "\": its monitor is not connected (skipped)");
            }
            return any ? null : "!" + "No monitor of profile \"" + name + "\" is connected.";
        }

        static string List(MonitorService service, InputLabels labels, Settings settings)
        {
            var sb = new StringBuilder();
            int i = 0;
            foreach (var m in service.All)
            {
                sb.Append(++i).Append(". ").Append(m.Model).Append("  (key ").Append(m.Key).Append(')');
                if (!m.Responsive) { sb.AppendLine("  no DDC/CI"); continue; }
                VcpValue v;
                if (m.Values.TryGetValue(Vcp.Brightness, out v)) sb.Append("  brightness ").Append(v.Percent).Append(" %");
                if (m.Values.TryGetValue(Vcp.Volume, out v)) sb.Append("  volume ").Append(v.Percent).Append(" %");
                var input = m.CurrentInput();
                if (input.HasValue) sb.Append("  input ").Append(labels.Text(m, input.Value));
                if (m.InSrgb) sb.Append("  (sRGB)");
                sb.AppendLine();
                if (m.Supports(Vcp.Input)) sb.AppendLine("   inputs: " + string.Join(", ", m.InputOptions.Select(o => labels.Text(m, o)).ToArray()));
            }
            if (i == 0) sb.AppendLine("No monitors.");
            if (settings.Profiles.Count > 0)
                sb.AppendLine("Profiles: " + string.Join(", ", settings.Profiles.Select(p => p.Name).Distinct().ToArray()));
            return sb.ToString().TrimEnd();
        }
    }
}
