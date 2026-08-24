using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhasOverlay
{
    // Raw shape of weekly.json (one object, overwritten each week).
    public class WeeklyDto
    {
        public int SchemaVersion { get; set; } = 1;
        public string Date { get; set; } = "";        // yyyy-MM-dd
        public string? Title { get; set; }
        public bool FriendlyGhost { get; set; } = false;
        public double? GhostSpeed { get; set; }        // 0.5 / 0.75 / 1.0 / 1.25 / 1.5
        public int? EvidenceGiven { get; set; }        // 0..3
        public string? MapSize { get; set; }           // small | medium | large
        public string? HuntDuration { get; set; }      // low | medium | high
    }

    public class WeeklyEntry
    {
        public DateTime Date;
        public string Title = "";
        public bool FriendlyGhost;
        public double GhostSpeed = 1.0;
        public int EvidenceGiven = 3;
        public int MapSizeIndex;       // 0 small, 1 medium, 2 large
        public int HuntTier;           // 0 low, 1 med, 2 high
        public bool IsOutdated;

        public string Label
        {
            get
            {
                string d = Date.ToString("dd/MM/yy", CultureInfo.InvariantCulture);
                return IsOutdated ? $"Weekly ({d}, outdated)" : $"Weekly ({d})";
            }
        }

        public string Tooltip
        {
            get
            {
                string t = string.IsNullOrWhiteSpace(Title) ? "This week's challenge" : Title;
                if (FriendlyGhost) t += "\nFriendly ghost, no hunts this week.";
                return t;
            }
        }
    }

    public enum WeeklyUpdateResult
    {
        Updated,
        Unchanged,
        NetworkFailure,
        InvalidData,
        UnsupportedSchema,
        WriteFailure
    }

    /// <summary>
    /// Owns weekly.json. Same cache/pull-if-changed pipeline as <see cref="GhostDataService"/>,
    /// but with no bundled fallback (shipping one bakes in a stale week).
    /// </summary>
    public static class WeeklyDataService
    {
        public const string RemoteUrl = "https://raw.githubusercontent.com/ErrorzMade/PhasOverlay-data/main/weekly.json";

        public static bool RemoteConfigured => !RemoteUrl.Contains("USERNAME");

        // See GhostDataService.MaxSupportedSchema. The two files version independently.
        public const int MaxSupportedSchema = 1;

        /// <summary>True once a weekly.json declaring a newer schema has been refused.</summary>
        public static bool DataTooNew { get; private set; }

        // All week arithmetic runs in UTC so DST (GMT<->BST) never shifts the boundary.
        private const DayOfWeek WeeklyResetDay = DayOfWeek.Monday;

        public static string LocalPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay", "weekly.json");

        /// <summary>The cached weekly (validated), or null if none is cached / it's invalid.</summary>
        public static WeeklyEntry? GetWeekly()
        {
            try
            {
                if (File.Exists(LocalPath)) return Parse(File.ReadAllText(LocalPath));
            }
            catch { }
            return null;
        }

        /// <summary>Pulls a valid changed weekly into the local cache.</summary>
        public static async Task<WeeklyUpdateResult> CheckForUpdatesAsync(
            bool revalidate = false,
            CancellationToken cancellationToken = default)
        {
            if (!RemoteConfigured) return WeeklyUpdateResult.NetworkFailure;

            string remote;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                http.DefaultRequestHeaders.Add("User-Agent", "PhasOverlay");

                using var request = new HttpRequestMessage(HttpMethod.Get, RemoteUrl);
                if (revalidate)
                    request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                remote = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return WeeklyUpdateResult.NetworkFailure;
            }

            WeeklyParseStatus status = ParseCore(remote, out _);
            if (status == WeeklyParseStatus.UnsupportedSchema)
            {
                DataTooNew = true;
                return WeeklyUpdateResult.UnsupportedSchema;
            }
            if (status != WeeklyParseStatus.Valid) return WeeklyUpdateResult.InvalidData;

            string local = "";
            try { if (File.Exists(LocalPath)) local = File.ReadAllText(LocalPath); } catch { }

            if (Normalize(remote) == Normalize(local)) return WeeklyUpdateResult.Unchanged;

            return TryWriteLocal(remote) ? WeeklyUpdateResult.Updated : WeeklyUpdateResult.WriteFailure;
        }

        /// <summary>
        /// Parses + validates weekly JSON, returning null on any failure. date + evidenceGiven are
        /// always required; speed/map/hunt are required only when not a friendly-ghost week.
        /// </summary>
        public static WeeklyEntry? Parse(string json)
        {
            WeeklyParseStatus status = ParseCore(json, out var entry);
            if (status == WeeklyParseStatus.UnsupportedSchema) DataTooNew = true;
            return status == WeeklyParseStatus.Valid ? entry : null;
        }

        private enum WeeklyParseStatus
        {
            Valid,
            Invalid,
            UnsupportedSchema
        }

        private static WeeklyParseStatus ParseCore(string json, out WeeklyEntry? entry)
        {
            entry = null;
            try
            {
                var dto = JsonSerializer.Deserialize<WeeklyDto>(json, GhostDataService.Json);
                if (dto == null) return WeeklyParseStatus.Invalid;

                if (dto.SchemaVersion > MaxSupportedSchema)
                    return WeeklyParseStatus.UnsupportedSchema;

                if (!DateTime.TryParse(dto.Date, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
                    return WeeklyParseStatus.Invalid;

                if (dto.EvidenceGiven is not int ev || ev < 0 || ev > 3) return WeeklyParseStatus.Invalid;

                entry = new WeeklyEntry
                {
                    Date = parsedDate.Date,
                    Title = dto.Title ?? "",
                    FriendlyGhost = dto.FriendlyGhost,
                    EvidenceGiven = ev,
                };

                if (dto.FriendlyGhost)
                {
                    entry.GhostSpeed = 1.0;
                    entry.MapSizeIndex = 0;
                    entry.HuntTier = 0;
                }
                else
                {
                    if (dto.GhostSpeed is not double spd || SpeedToIndex(spd) < 0) return WeeklyParseStatus.Invalid;
                    int mapIdx = MapToIndex(dto.MapSize);
                    int huntTier = HuntToTier(dto.HuntDuration);
                    if (mapIdx < 0 || huntTier < 0) return WeeklyParseStatus.Invalid;

                    entry.GhostSpeed = spd;
                    entry.MapSizeIndex = mapIdx;
                    entry.HuntTier = huntTier;
                }

                entry.IsOutdated = WeekStartUtc(entry.Date) < WeekStartUtc(DateTime.UtcNow);
                return WeeklyParseStatus.Valid;
            }
            catch
            {
                entry = null;
                return WeeklyParseStatus.Invalid;
            }
        }

        public static DateTime WeekStartUtc(DateTime t)
        {
            int diff = ((int)t.DayOfWeek - (int)WeeklyResetDay + 7) % 7;
            return t.Date.AddDays(-diff);
        }

        private static readonly double[] Speeds = { 0.5, 0.75, 1.0, 1.25, 1.5 };
        public static int SpeedToIndex(double m)
        {
            for (int i = 0; i < Speeds.Length; i++) if (Math.Abs(Speeds[i] - m) < 0.001) return i;
            return -1;
        }

        private static int MapToIndex(string? s) => (s?.Trim().ToLowerInvariant()) switch
        {
            "small" => 0,
            "medium" => 1,
            "large" => 2,
            _ => -1
        };

        private static int HuntToTier(string? s) => (s?.Trim().ToLowerInvariant()) switch
        {
            "low" => 0,
            "medium" => 1,
            "med" => 1,
            "high" => 2,
            _ => -1
        };

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

        private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();
    }
}
