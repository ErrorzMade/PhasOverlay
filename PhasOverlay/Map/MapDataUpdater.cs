using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PhasOverlay
{
    /// <summary>
    /// Pulls newer map data once at startup and applies it on the next launch, never mid-session.
    /// Every failure keeps the last good state silently.
    /// </summary>
    public static class MapDataUpdater
    {
        public const int MaxSupportedSchema = 1;

        private const string BaseUrl = "https://raw.githubusercontent.com/ErrorzMade/PhasOverlay-data/main/maps/";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

        // A hostile or broken manifest must not be able to fill the disk.
        private const long MaxFileBytes = 8L * 1024 * 1024;
        private const long MaxTotalBytes = 64L * 1024 * 1024;
        private const int MaxFiles = 600;

        private const string StagingFolder = ".staging";
        private const string PendingFolder = ".pending";
        private const string KeepList = ".files";

        /// <summary>True once a newer set of files has downloaded and is waiting for a restart.</summary>
        public static bool UpdatePending { get; private set; }

        /// <summary>Raised on the pulling thread when <see cref="UpdatePending"/> becomes true.</summary>
        public static event Action? UpdateReady;

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private class ManifestDto
        {
            public int SchemaVersion { get; set; } = 1;
            public int DataVersion { get; set; }
            public List<ManifestFileDto> Files { get; set; } = new();
        }

        private class ManifestFileDto
        {
            public string Path { get; set; } = "";
            public long Size { get; set; }
            public string Sha256 { get; set; } = "";
        }

        public static async Task CheckAsync()
        {
            try { await RunAsync().ConfigureAwait(false); }
            catch { }
        }

        private static async Task RunAsync()
        {
            using var http = new HttpClient { Timeout = Timeout };
            using var cts = new CancellationTokenSource(Timeout);

            var manifest = await GetManifestAsync(http, cts.Token).ConfigureAwait(false);
            if (manifest == null || manifest.SchemaVersion > MaxSupportedSchema) return;

            if (manifest.DataVersion <= MapDataService.EffectiveDataVersion()) return;

            var wanted = Vet(manifest);
            if (wanted == null) return;

            // Against the cache too, so a stale cached file is replaced even where the bundled copy matches.
            var stale = new List<ManifestFileDto>();
            foreach (var f in wanted)
            {
                if (!Matches(MapDataService.ResolveWithCache(f.Path), f)) stale.Add(f);
            }
            if (stale.Count == 0) return;

            string staging = Path.Combine(MapDataService.CacheDir, StagingFolder);
            string pending = Path.Combine(MapDataService.CacheDir, PendingFolder);
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);

                foreach (var f in stale)
                {
                    if (!await DownloadAsync(http, f, staging, cts.Token).ConfigureAwait(false)) return;
                }

                string stagedJson = Path.Combine(staging, "maps.json");
                string jsonPath = File.Exists(stagedJson) ? stagedJson : MapDataService.ResolveWithCache("maps.json");
                if (!ReferencesResolve(jsonPath, staging)) return;

                var keep = new List<string>();
                foreach (var f in wanted) keep.Add(f.Path);
                File.WriteAllLines(Path.Combine(staging, KeepList), keep);

                // One rename, so the next launch finds either a whole download or none.
                if (Directory.Exists(pending)) Directory.Delete(pending, true);
                Directory.Move(staging, pending);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }

            UpdatePending = true;
            UpdateReady?.Invoke();
        }

        private static async Task<ManifestDto?> GetManifestAsync(HttpClient http, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "manifest.json");
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            using var response = await http.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            // A BOM at the front fails the parse.
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            body = body.Trim().TrimStart('\uFEFF');

            return JsonSerializer.Deserialize<ManifestDto>(body, Json);
        }

        /// <summary>The manifest's files, or null when any entry is unusable. One bad path rejects the lot.</summary>
        private static List<ManifestFileDto>? Vet(ManifestDto manifest)
        {
            if (manifest.Files.Count == 0 || manifest.Files.Count > MaxFiles) return null;

            long total = 0;
            foreach (var f in manifest.Files)
            {
                if (!IsSafePath(f.Path)) return null;
                if (f.Size <= 0 || f.Size > MaxFileBytes) return null;
                if (f.Sha256.Length != 64) return null;

                total += f.Size;
                if (total > MaxTotalBytes) return null;
            }
            return manifest.Files;
        }

        /// <summary>Only maps.json or a plain relative path under Images/ may be written.</summary>
        private static bool IsSafePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 200) return false;
            if (path.Contains('\\') || path.Contains("..") || path.Contains(':')) return false;
            if (path.StartsWith('/')) return false;

            return path == "maps.json" || path.StartsWith("Images/", StringComparison.Ordinal);
        }

        private static bool Matches(string fullPath, ManifestFileDto f)
        {
            try
            {
                var info = new FileInfo(fullPath);
                if (!info.Exists || info.Length != f.Size) return false;
                return string.Equals(Hash(fullPath), f.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static async Task<bool> DownloadAsync(HttpClient http, ManifestFileDto f, string staging, CancellationToken token)
        {
            try
            {
                byte[] bytes = await http.GetByteArrayAsync(BaseUrl + f.Path, token).ConfigureAwait(false);
                if (bytes.LongLength != f.Size) return false;
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), f.Sha256, StringComparison.OrdinalIgnoreCase)) return false;

                string target = Path.Combine(staging, f.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!Inside(staging, target)) return false;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, bytes, token).ConfigureAwait(false);
                return true;
            }
            catch { return false; }
        }

        private static bool Inside(string root, string candidate)
        {
            string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(candidate).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ReferencesResolve(string mapsJsonPath, string staging)
        {
            try
            {
                var file = JsonSerializer.Deserialize<MapFileDto>(File.ReadAllText(mapsJsonPath), Json);
                if (file == null || file.Maps.Count == 0 || file.SchemaVersion > MapDataService.MaxSupportedSchema) return false;

                foreach (var map in file.Maps)
                {
                    if (!Present(map.Overview, staging)) return false;

                    // Not possession photos: a photo not taken yet is a gap in the data, not a broken download.
                    foreach (var floor in AllFloors(map))
                    {
                        if (!Present(floor.Plan, staging)) return false;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        private static IEnumerable<FloorDto> AllFloors(MapDto map)
        {
            foreach (var f in map.Floors) yield return f;
            foreach (var v in map.Versions)
                foreach (var f in v.Floors) yield return f;
        }

        private static bool Present(string relative, string staging)
        {
            if (string.IsNullOrWhiteSpace(relative)) return true;
            if (!IsSafePath(relative)) return false;

            string staged = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(staged) || File.Exists(MapDataService.ResolveWithCache(relative));
        }

        /// <summary>
        /// Moves a finished download into the cache, maps.json last, then prunes files the data no
        /// longer lists. Runs before map data is first read in a session.
        /// </summary>
        internal static void ApplyPending()
        {
            string pending = Path.Combine(MapDataService.CacheDir, PendingFolder);
            if (!Directory.Exists(pending)) return;

            try
            {
                string list = Path.Combine(pending, KeepList);
                if (!File.Exists(list))
                {
                    Directory.Delete(pending, true);
                    return;
                }

                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(list))
                {
                    if (line.Length > 0) keep.Add(line);
                }

                string pendingJson = Path.Combine(pending, "maps.json");
                foreach (string source in Directory.GetFiles(pending, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(source, list, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(source, pendingJson, StringComparison.OrdinalIgnoreCase)) continue;
                    MoveInto(pending, source);
                }
                if (File.Exists(pendingJson)) MoveInto(pending, pendingJson);

                keep.Add("maps.json");
                Prune(keep);
                Directory.Delete(pending, true);
            }
            // A failed apply leaves the pending folder in place, so the next launch finishes it.
            catch { }
        }

        private static void Prune(HashSet<string> keep)
        {
            string root = MapDataService.CacheDir;
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.StartsWith(StagingFolder + "/", StringComparison.Ordinal)) continue;
                if (relative.StartsWith(PendingFolder + "/", StringComparison.Ordinal)) continue;
                if (!keep.Contains(relative)) File.Delete(file);
            }
        }

        private static void MoveInto(string staging, string source)
        {
            string relative = Path.GetRelativePath(staging, source);
            string target = Path.Combine(MapDataService.CacheDir, relative);
            if (!Inside(MapDataService.CacheDir, target)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target, true);
        }
    }
}
