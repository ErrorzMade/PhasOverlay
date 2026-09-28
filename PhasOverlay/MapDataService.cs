using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace PhasOverlay
{
    public class MapFileDto
    {
        public int SchemaVersion { get; set; } = 1;
        // Bumped by the publish script. A download is only applied when it beats what is installed.
        public int DataVersion { get; set; }
        public List<MapDto> Maps { get; set; } = new();
        // Optional. Without it the icon table built into the app is used.
        public List<PossessionDto> Possessions { get; set; } = new();
    }

    public class PossessionDto
    {
        public string Item { get; set; } = "";
        public string Icon { get; set; } = "";
    }

    public class MapDto
    {
        public string Name { get; set; } = "";
        public string Size { get; set; } = "";           // small / medium / large
        public string Overview { get; set; } = "";       // exterior shot for the picker card
        public string CursedFolder { get; set; } = "";
        public bool SharedCursedSpot { get; set; }
        public List<FloorDto> Floors { get; set; } = new();
        public List<MapVersionDto> Versions { get; set; } = new();
        // Optional, "1.3.1" form. Older builds skip the map rather than draw it wrong.
        public string MinAppVersion { get; set; } = "";

        [JsonIgnore] public string OverviewFullPath => MapDataService.Resolve(Overview);
        // Decoded at card size rather than 800x450, so the picker costs a few MB instead of ~19MB.
        [JsonIgnore] public System.Windows.Media.ImageSource? Thumb { get; set; }

        // A restricted map keeps its floors inside its versions, so both lists count.
        [JsonIgnore] public bool HasPlan =>
            Floors.Concat(Versions.SelectMany(v => v.Floors))
                  .Any(f => !string.IsNullOrWhiteSpace(f.Plan) && File.Exists(MapDataService.Resolve(f.Plan)));
    }

    public class FloorDto
    {
        public string Name { get; set; } = "";
        public string Plan { get; set; } = "";
        public List<MarkerDto> Markers { get; set; } = new();
    }

    // Restricted maps only. Each version is a section of the building with floors of its own.
    public class MapVersionDto
    {
        public string Name { get; set; } = "";
        public List<FloorDto> Floors { get; set; } = new();
    }

    // X and Y are 0-1 fractions of the plan, so a redrawn or resized plan keeps its markers.
    public class MarkerDto
    {
        public string Item { get; set; } = "";
        public string Kind { get; set; } = "cursed";
        public double X { get; set; }
        public double Y { get; set; }
        public string Room { get; set; } = "";
        public string Image { get; set; } = "";

        [JsonIgnore] public string ImageFullPath => MapDataService.Resolve(Image);
    }

    /// <summary>
    /// Owns the map roster. The downloaded cache is used only when its dataVersion beats the
    /// bundled copy's, and then outranks it file by file. Any problem with it leaves the bundled data.
    /// </summary>
    public static class MapDataService
    {
        public const int MaxSupportedSchema = 1;

        public static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay", "maps");

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        // The startup update check reads this from a background thread while the UI may too.
        private static readonly object Gate = new();

        private static List<MapDto>? _maps;
        private static int _dataVersion;
        private static List<PossessionDto> _possessions = new();
        private static bool _cacheInForce;

        /// <summary>Where a map file loads from: the cache first while downloaded data is in force, else the bundled copy.</summary>
        public static string Resolve(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return "";
            LoadFile();
            return _cacheInForce ? ResolveWithCache(relative) : Bundled(relative);
        }

        /// <summary>Where a file would load from once downloaded data is in force.</summary>
        public static string ResolveWithCache(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return "";
            string cached = Path.Combine(CacheDir, Tail(relative));
            return File.Exists(cached) ? cached : Bundled(relative);
        }

        private static string Bundled(string relative) => Path.Combine(AppContext.BaseDirectory, Tail(relative));

        private static string Tail(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);

        /// <summary>The data version in force, cached or bundled, for the updater to beat.</summary>
        public static int EffectiveDataVersion()
        {
            LoadFile();
            return _dataVersion;
        }

        /// <summary>A plan or photo at its native size, frozen so it can cross threads. Null on any failure.</summary>
        public static BitmapImage? LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>The possession icon table from the data in force. Empty when the file carries none.</summary>
        public static List<PossessionDto> GetPossessions()
        {
            LoadFile();
            return _possessions;
        }

        public static List<MapDto> GetMaps()
        {
            LoadFile();
            return _maps!;
        }

        private static void LoadFile()
        {
            lock (Gate)
            {
                if (_maps != null) return;

                MapDataUpdater.ApplyPending();

                var bundled = Read(Bundled("maps.json"));
                var cached = Read(Path.Combine(CacheDir, "maps.json"));

                // Only a newer download wins, so an app update's bundled data is never hidden by an old one.
                _cacheInForce = cached != null && (bundled == null || cached.DataVersion > bundled.DataVersion);
                var file = _cacheInForce ? cached : bundled;

                _maps = new List<MapDto>();
                if (file == null) return;

                _dataVersion = file.DataVersion;
                _possessions = file.Possessions ?? new List<PossessionDto>();
                foreach (var map in file.Maps)
                {
                    if (Supported(map.MinAppVersion)) _maps.Add(map);
                }
            }
        }

        private static MapFileDto? Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var file = JsonSerializer.Deserialize<MapFileDto>(File.ReadAllText(path), Json);
                return file != null && file.SchemaVersion <= MaxSupportedSchema && file.Maps.Count > 0 ? file : null;
            }
            catch { return null; }
        }

        /// <summary>Whether this build meets a map's minAppVersion. An unreadable value counts as too new.</summary>
        private static bool Supported(string minAppVersion)
        {
            if (string.IsNullOrWhiteSpace(minAppVersion)) return true;
            if (!Version.TryParse(minAppVersion.Trim(), out var required)) return false;

            var app = Assembly.GetExecutingAssembly().GetName().Version;
            return app != null && app >= required;
        }

        public static List<MapDto> BySize(string size)
        {
            var result = new List<MapDto>();
            foreach (var m in GetMaps())
            {
                if (string.Equals(m.Size, size, StringComparison.OrdinalIgnoreCase)) result.Add(m);
            }
            return result;
        }
    }
}
