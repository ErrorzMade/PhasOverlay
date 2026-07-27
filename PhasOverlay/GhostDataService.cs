using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PhasOverlay
{
    public class GhostFileDto
    {
        public int SchemaVersion { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public List<GhostDto> Ghosts { get; set; } = new();
    }

    public class GhostDto
    {
        public string Name { get; set; } = "";
        public string ShortFact { get; set; } = "";
        public string Speed { get; set; } = "";        // e.g. "0.4 - 3.0", "1.7", "Varies"
        public string Sanity { get; set; } = "";
        public string LosSpeedup { get; set; } = "Yes";
        public string? LosTooltip { get; set; }
        public List<string> Evidence { get; set; } = new();
        public string? ForcedEvidence { get; set; }
        public List<string> CanBe { get; set; } = new();    // slow / normal / fast
        public List<string> CanHunt { get; set; } = new();  // veryearly / early / normal / late
        public string Tell { get; set; } = "";

        // Optional. If omitted, the standard LOS-speedup curve (1.7 -> 2.805 over 13s) is used.
        public GraphDto? SpeedGraph { get; set; }
        // The "mark / rule out" tells shown in the ghost's detail modal.
        public List<BehaviorDto> Behaviors { get; set; } = new();
        // Cosmetic: hide the behaviours scrollbar (used for a couple of long lists).
        public bool? HideBehaviorScroll { get; set; }
    }

    // Speed-over-time curve for the detail modal's graph.
    public class GraphDto
    {
        public double Base { get; set; } = 1.7;
        public double Max { get; set; } = 2.805;
        public double TimeToMax { get; set; } = 13.0;
    }

    // One "mark / rule out" tell. A behaviour can depend on whether another ghost is
    // still a live possibility (e.g. Gallu's salt tell changes once Wraith is ruled out):
    // set ConditionGhost + TextIfConditionValid for that.
    public class BehaviorDto
    {
        public string Type { get; set; } = "mark";   // "mark" or "ruleout"
        public string Prefix { get; set; } = "";
        public string Text { get; set; } = "";
        public string? ConditionGhost { get; set; }        // when this ghost is still possible...
        public string? TextIfConditionValid { get; set; }  // ...show this text instead of Text
    }

    /// <summary>
    /// Owns the ghost data file. Priority is local (remote-updatable) -> bundled
    /// fallback, and the app never blocks on or breaks because of the network:
    ///   • no local file        -> seed from the bundled copy, then try remote
    ///   • local file present    -> check remote; pull only if it actually changed
    ///   • cannot reach remote   -> keep whatever is already on disk
    /// </summary>
    public static class GhostDataService
    {
        // Editing ghosts.json in the public ErrorzMade/PhasOverlay-data repo propagates to everyone
        // on next launch. (jsDelivr CDN is an alternative if raw.githubusercontent rate-limits.)
        public const string RemoteUrl = "https://raw.githubusercontent.com/ErrorzMade/PhasOverlay-data/main/ghosts.json";

        // Remote checks are skipped while this is still the placeholder URL.
        public static bool RemoteConfigured => !RemoteUrl.Contains("USERNAME");

        // Highest ghosts.json layout this build can read. Bump only when the file's *shape* changes
        // in a way old readers can't handle, never for content edits (new ghosts, fixed values).
        public const int MaxSupportedSchema = 1;

        /// <summary>True once a ghosts.json declaring a newer schema has been refused.</summary>
        public static bool DataTooNew { get; private set; }

        public static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string LocalPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay", "ghosts.json");

        public static string BundledPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ghosts.json");

        /// <summary>
        /// Returns the best available ghost JSON right now (never touches the network).
        /// Seeds the local cache from the bundled copy on first run.
        /// </summary>
        public static string GetGhostJson()
        {
            // 1) Prefer the local cache (this is what remote updates write to).
            try
            {
                if (File.Exists(LocalPath))
                {
                    string local = File.ReadAllText(LocalPath);
                    if (IsValid(local)) return local;
                }
            }
            catch { }

            // 2) Fall back to the bundled copy, and seed the local cache from it.
            try
            {
                string bundled = File.ReadAllText(BundledPath);
                TryWriteLocal(bundled);
                return bundled;
            }
            catch { }

            // 3) Last resort: empty (app still runs, just with no ghosts).
            return "{\"schemaVersion\":1,\"ghosts\":[]}";
        }

        /// <summary>
        /// Checks the remote file and pulls it into the local cache only if it changed.
        /// Returns true when the local cache was updated (so callers can reload).
        /// Any failure (offline, timeout, bad data) is swallowed and leaves disk as-is.
        /// </summary>
        public static async Task<bool> CheckForUpdatesAsync()
        {
            if (!RemoteConfigured) return false;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                http.DefaultRequestHeaders.Add("User-Agent", "PhasOverlay");

                string remote = await http.GetStringAsync(RemoteUrl).ConfigureAwait(false);
                if (!IsValid(remote)) return false;

                string local = "";
                try { if (File.Exists(LocalPath)) local = File.ReadAllText(LocalPath); } catch { }

                if (Normalize(remote) == Normalize(local)) return false; // no change

                return TryWriteLocal(remote); // pull changes
            }
            catch
            {
                return false; // can't connect / bad response -> leave local as-is
            }
        }

        private static bool TryWriteLocal(string content)
        {
            try
            {
                string? dir = Path.GetDirectoryName(LocalPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(LocalPath, content);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// A file is usable only if this build understands its layout. Without the schema gate a
        /// future restructured file still deserializes (unknown fields are ignored), passes the
        /// count check, and gets cached, leaving a permanently blank tracker.
        /// </summary>
        private static bool IsValid(string json)
        {
            try
            {
                var file = JsonSerializer.Deserialize<GhostFileDto>(json, Json);
                if (file?.Ghosts == null || file.Ghosts.Count == 0) return false;

                if (file.SchemaVersion > MaxSupportedSchema)
                {
                    DataTooNew = true;
                    return false;
                }
                return true;
            }
            catch { return false; }
        }

        // Ignore line-ending / trailing-whitespace differences when comparing.
        private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();
    }
}
