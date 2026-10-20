using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MonitorConfig
{
    // Asks GitHub for the latest published release. The request carries nothing but the app's version in the
    // User-Agent; it runs on a pool thread and reports back through the given dispatcher.
    static class UpdateChecker
    {
        public const string Repo = "JorgeGuzz/MonitorConfig";
        const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

        public class Release
        {
            public Version Version;
            public string Tag;
            public string Url { get { return "https://github.com/" + Repo + "/releases/tag/" + Tag; } }
        }

        // "v1.2.0" -> 1.2.0 (always three parts, so 1.2.0 equals the assembly's 1.2.0.0); null if it is not a version.
        public static Version ParseTag(string tag)
        {
            var m = Regex.Match(tag ?? "", @"^v?(\d+)\.(\d+)(?:\.(\d+))?$");
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        // The "tag_name" of GitHub's latest-release JSON.
        public static Release Parse(string json)
        {
            var m = Regex.Match(json ?? "", "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (!m.Success) return null;
            var version = ParseTag(m.Groups[1].Value);
            return version == null ? null : new Release { Version = version, Tag = m.Groups[1].Value };
        }

        public static Version Current
        {
            get
            {
                var v = typeof(UpdateChecker).Assembly.GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static bool IsNewer(Release latest)
        {
            return latest != null && latest.Version > Current;
        }

        // done(release or null, error or null) runs on the UI through post.
        public static void CheckAsync(Action<Action> post, Action<Release, Exception> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Release release = null;
                Exception error = null;
                try { release = Fetch(); }
                catch (Exception ex) { error = ex; }
                post(() => done(release, error));
            });
        }

        static Release Fetch()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2: GitHub refuses older ones
            var request = (HttpWebRequest)WebRequest.Create(LatestApi);
            request.UserAgent = "MonitorConfig/" + Current;
            request.Accept = "application/vnd.github+json";
            request.Timeout = request.ReadWriteTimeout = 10000;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                var release = Parse(reader.ReadToEnd());
                if (release == null) throw new InvalidDataException("No release version in GitHub's answer.");
                return release;
            }
        }
    }
}
