using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonitorConfig
{
    // Translations: one key=value file per language (src\Lang\*.txt), checked and packed into a compressed resource at
    // build time (tools\PackLang.cs). Only the language in use, plus English as fallback, is kept in memory.
    static class Lang
    {
        // Order of the selector: English, Spanish, then the classic localisation order.
        public static readonly string[] Order = { "en", "es", "fr", "it", "de", "pt", "ru", "ja", "zh" };

        public static string PackPath { get; set; }   // tests read the pack from disk; the app uses the resource inside the exe

        static Dictionary<string, string> current = new Dictionary<string, string>();
        static Dictionary<string, string> fallback = new Dictionary<string, string>();
        static readonly Dictionary<string, string> names = new Dictionary<string, string>();

        public static string Current { get; private set; }

        // The display language of the Windows account, if it is one of ours; otherwise English.
        public static string SystemLanguage
        {
            get
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return Order.Contains(two) ? two : "en";
            }
        }

        // setting: a language code, or empty for automatic.
        public static string Resolve(string setting)
        {
            return !string.IsNullOrEmpty(setting) && Order.Contains(setting) ? setting : SystemLanguage;
        }

        public static void Load(string setting)
        {
            string code = Resolve(setting);
            var sections = Read(new HashSet<string> { code, "en" });
            Dictionary<string, string> en, chosen;
            sections.TryGetValue("en", out en);
            sections.TryGetValue(code, out chosen);
            fallback = en ?? new Dictionary<string, string>();
            current = chosen ?? fallback;
            Current = chosen != null ? code : "en";
        }

        public static string Get(string key)
        {
            string value;
            if (current.TryGetValue(key, out value) || fallback.TryGetValue(key, out value)) return value;
            return key;   // a missing text shows its key (the build check prevents it)
        }

        public static bool Has(string key)
        {
            return current.ContainsKey(key) || fallback.ContainsKey(key);
        }

        // Name of a language in that language ("Deutsch", "日本語"), for the selector.
        public static string NameOf(string code)
        {
            string name;
            return names.TryGetValue(code, out name) ? name : code;
        }

        static Stream Open()
        {
            if (PackPath != null) return File.OpenRead(PackPath);
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("lang.pack");
        }

        // Pack format (inside gzip): "@@lang xx" starts a language; "@name=..." is its own name; then key=value lines
        // with "\n" for line breaks.
        static Dictionary<string, Dictionary<string, string>> Read(HashSet<string> wanted)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            try
            {
                using (var raw = Open())
                {
                    if (raw == null) return result;
                    using (var gz = new GZipStream(raw, CompressionMode.Decompress))
                    using (var reader = new StreamReader(gz, Encoding.UTF8))
                    {
                        string code = null;
                        Dictionary<string, string> table = null;
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.StartsWith("@@lang "))
                            {
                                code = line.Substring(7).Trim();
                                table = wanted.Contains(code) ? new Dictionary<string, string>() : null;
                                if (table != null) result[code] = table;
                                continue;
                            }
                            int eq = line.IndexOf('=');
                            if (code == null || eq <= 0) continue;
                            string key = line.Substring(0, eq);
                            if (key == "@name") { names[code] = line.Substring(eq + 1); continue; }
                            if (table != null) table[key] = line.Substring(eq + 1).Replace("\\n", "\n");
                        }
                    }
                }
            }
            catch (Exception ex) { Crash.Log(ex); }   // without texts the keys are shown: still usable
            return result;
        }
    }
}
