using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhasOverlay
{
    // Raw shape of version.json (one object, overwritten on each release).
    public class VersionDto
    {
        public int SchemaVersion { get; set; } = 1;
        public string Version { get; set; } = "";   // x.y.z
        public string? Notes { get; set; }
        public string? Url { get; set; }
    }

    public class UpdateInfo
    {
        public Version Version = new(0, 0, 0);
        public string Notes = "";
        public string Url = UpdateService.DefaultReleasesUrl;

        public string VersionLabel => $"v{Version.Major}.{Version.Minor}.{Version.Build}";
    }

    /// <summary>
    /// Checks version.json for a release newer than this build. Notify-only: nothing is
    /// downloaded or replaced, the prompt just sends the user to the Releases page.
    /// </summary>
    public static class UpdateService
    {
        public const string RemoteUrl = "https://raw.githubusercontent.com/ErrorzMade/PhasOverlay-data/main/version.json";
        public const string DefaultReleasesUrl = "https://github.com/ErrorzMade/PhasOverlay/releases/latest";

        public static bool RemoteConfigured => !RemoteUrl.Contains("USERNAME");

        // Kept out of settings.txt: that file has two formats and several writers, and a skipped
        // version is not worth the migration risk.
        private static string StatePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay", "update.txt");

        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, v.Build);
            }
        }

        /// <summary>
        /// Returns the pending release, or null when up to date, skipped, offline or unparseable.
        /// Never throws and never blocks the app on the network.
        /// </summary>
        public static async Task<UpdateInfo?> CheckAsync()
        {
            if (!RemoteConfigured) return null;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                http.DefaultRequestHeaders.Add("User-Agent", "PhasOverlay");

                string remote = await http.GetStringAsync(RemoteUrl).ConfigureAwait(false);
                var info = Parse(remote);
                if (info == null) return null;

                if (info.Version <= CurrentVersion) return null;
                if (info.Version == GetSkippedVersion()) return null;

                return info;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Parses + validates version.json, returning null on any failure.</summary>
        public static UpdateInfo? Parse(string json)
        {
            try
            {
                var dto = JsonSerializer.Deserialize<VersionDto>(json, GhostDataService.Json);
                if (dto == null) return null;
                if (!Version.TryParse(dto.Version, out var parsed)) return null;
                if (parsed.Major < 0 || parsed.Minor < 0 || parsed.Build < 0) return null;

                string url = string.IsNullOrWhiteSpace(dto.Url) ? DefaultReleasesUrl : dto.Url.Trim();
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = DefaultReleasesUrl;

                return new UpdateInfo
                {
                    Version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0)),
                    Notes = dto.Notes?.Trim() ?? "",
                    Url = url
                };
            }
            catch { return null; }
        }

        public static Version? GetSkippedVersion()
        {
            try
            {
                if (!File.Exists(StatePath)) return null;
                foreach (var line in File.ReadAllLines(StatePath))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length == 2 && parts[0].Trim() == "SkippedVersion"
                        && Version.TryParse(parts[1].Trim(), out var v)) return v;
                }
            }
            catch { }
            return null;
        }

        public static void SkipVersion(Version v)
        {
            try
            {
                string? dir = Path.GetDirectoryName(StatePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(StatePath, $"SkippedVersion={v.Major}.{v.Minor}.{v.Build}");
            }
            catch { }
        }
    }
}
