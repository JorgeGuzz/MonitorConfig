// Checks the language files against en.txt (same keys, same {n} placeholders) and packs them, gzip-compressed,
// into the resource the app reads (Lang.cs). Usage: PackLang <folder with xx.txt> <out.pack>
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

static class PackLang
{
    static Dictionary<string, string> Parse(string path, List<string> errors)
    {
        var table = new Dictionary<string, string>();
        int n = 0;
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            n++;
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith(";")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) { errors.Add(Path.GetFileName(path) + ":" + n + ": missing '='"); continue; }
            string key = line.Substring(0, eq);
            if (table.ContainsKey(key)) errors.Add(Path.GetFileName(path) + ":" + n + ": repeated key " + key);
            table[key] = line.Substring(eq + 1);
        }
        return table;
    }

    static string Holes(string text)
    {
        return string.Join(",", Regex.Matches(text, @"\{\d+\}").Cast<Match>().Select(m => m.Value).Distinct().OrderBy(x => x).ToArray());
    }

    static int Main(string[] args)
    {
        string dir = args[0], output = args[1];
        var errors = new List<string>();
        var reference = Parse(Path.Combine(dir, "en.txt"), errors);
        var sb = new StringBuilder();
        var codes = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.txt").OrderBy(f => f))
        {
            string code = Path.GetFileNameWithoutExtension(file);
            var table = Parse(file, errors);
            foreach (var kv in reference)
            {
                string value;
                if (!table.TryGetValue(kv.Key, out value)) errors.Add(code + ": missing " + kv.Key);
                else if (Holes(value) != Holes(kv.Value)) errors.Add(code + ": " + kv.Key + " uses {" + Holes(value) + "} but en.txt {" + Holes(kv.Value) + "}");
            }
            foreach (var key in table.Keys)
                if (!reference.ContainsKey(key)) errors.Add(code + ": extra key " + key);
            sb.Append("@@lang ").Append(code).Append('\n');
            foreach (var kv in table) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            codes.Add(code);
        }
        if (errors.Count > 0)
        {
            foreach (var e in errors) Console.Error.WriteLine(e);
            return 1;
        }
        var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
        using (var file = File.Create(output))
        using (var gz = new GZipStream(file, CompressionLevel.Optimal))
            gz.Write(bytes, 0, bytes.Length);
        Console.WriteLine("Languages: " + string.Join(" ", codes.ToArray()) + " (" + reference.Count + " texts, " + bytes.Length / 1024 + " KB -> " + new FileInfo(output).Length / 1024 + " KB)");
        return 0;
    }
}
