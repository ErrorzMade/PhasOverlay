using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhasOverlay
{
    /// <summary>
    /// The map the player last opened. Its own file, since the settings.txt writers each rebuild
    /// that file and would drop a key they do not know. The floor is session only.
    /// </summary>
    public static class MapSelection
    {
        private const string DefaultFloorName = "Ground";

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay", "map.txt");

        private static bool _loaded;
        private static string _map = "", _version = "";

        // Never written, so a restart always lands on Ground.
        private static string _floor = "";

        public static FloorDto? DefaultFloor(List<FloorDto> floors)
            => floors.FirstOrDefault(f => string.Equals(f.Name.Trim(), DefaultFloorName, StringComparison.OrdinalIgnoreCase))
               ?? floors.FirstOrDefault();

        public static void Save(string map, string version, string floor)
        {
            EnsureLoaded();

            _floor = floor;
            if (_map == map && _version == version) return;

            _map = map;
            _version = version;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string temp = FilePath + ".tmp";
                File.WriteAllLines(temp, new[] { $"Map={map}", $"Version={version}" });
                File.Move(temp, FilePath, true);
            }
            catch { }
        }

        public static void Clear()
        {
            EnsureLoaded();
            _map = _version = _floor = "";
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        /// <summary>The saved selection against the current data, or null when there is none or its map has no plan.</summary>
        public static (MapDto Map, MapVersionDto? Version, FloorDto? Floor)? Current()
        {
            EnsureLoaded();
            if (_map.Length == 0) return null;

            var map = MapDataService.GetMaps()
                .FirstOrDefault(m => string.Equals(m.Name, _map, StringComparison.OrdinalIgnoreCase));
            if (map == null || !map.HasPlan) return null;

            MapVersionDto? version = null;
            var floors = map.Floors;
            if (map.Versions.Count > 0)
            {
                version = map.Versions.FirstOrDefault(v => string.Equals(v.Name, _version, StringComparison.OrdinalIgnoreCase))
                          ?? map.Versions[0];
                floors = version.Floors;
            }

            var floor = floors.FirstOrDefault(f => string.Equals(f.Name, _floor, StringComparison.OrdinalIgnoreCase))
                        ?? DefaultFloor(floors);

            return (map, version, floor);
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line[..eq];
                    string value = line[(eq + 1)..].Trim();
                    if (key == "Map") _map = value;
                    else if (key == "Version") _version = value;
                }
            }
            catch { }
        }
    }
}
