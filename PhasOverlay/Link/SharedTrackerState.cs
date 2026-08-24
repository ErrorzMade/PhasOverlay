using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhasOverlay.Link
{
    public sealed class RoomSettings
    {
        public int Difficulty { get; set; } = 1;
        public int Map { get; set; }
        public int CustomTier { get; set; } = 1;
        public int SpeedIndex { get; set; } = 2;
        public int HuntTier { get; set; } = 1;

        public RoomSettings Clone() => new()
        {
            Difficulty = Difficulty,
            Map = Map,
            CustomTier = CustomTier,
            SpeedIndex = SpeedIndex,
            HuntTier = HuntTier
        };

        public bool Equals(RoomSettings other) =>
            other != null && Difficulty == other.Difficulty && Map == other.Map
            && CustomTier == other.CustomTier && SpeedIndex == other.SpeedIndex
            && HuntTier == other.HuntTier;

        public JsonObject ToJson() => new()
        {
            ["difficulty"] = Difficulty,
            ["map"] = Map,
            ["customTier"] = CustomTier,
            ["speedIndex"] = SpeedIndex,
            ["huntTier"] = HuntTier
        };

        public static RoomSettings? Parse(JsonElement value)
        {
            if (!LinkProtocol.HasExactKeys(value, "difficulty", "map", "customTier", "speedIndex", "huntTier"))
                return null;

            if (!LinkProtocol.TryInt(value, "difficulty", 0, 6, out int difficulty)) return null;
            if (!LinkProtocol.TryInt(value, "map", 0, 2, out int map)) return null;
            if (!LinkProtocol.TryInt(value, "customTier", 0, 2, out int customTier)) return null;
            if (!LinkProtocol.TryInt(value, "speedIndex", 0, 4, out int speedIndex)) return null;
            if (!LinkProtocol.TryInt(value, "huntTier", 0, 2, out int huntTier)) return null;

            var settings = new RoomSettings
            {
                Difficulty = difficulty,
                Map = map,
                CustomTier = customTier,
                SpeedIndex = speedIndex,
                HuntTier = huntTier
            };
            return settings.IsValid() ? settings : null;
        }

        public bool IsValid()
        {
            if (Difficulty < 0 || Difficulty > 6) return false;
            if (Map < 0 || Map > 2) return false;
            if (CustomTier < 0 || CustomTier > 2) return false;
            if (SpeedIndex < 0 || SpeedIndex > 4) return false;
            if (HuntTier < 0 || HuntTier > 2) return false;

            if (Difficulty == 0 && HuntTier != 0) return false;
            if (Difficulty == 1 && HuntTier != 1) return false;
            if (Difficulty >= 2 && Difficulty <= 4 && HuntTier != 2) return false;
            if (Difficulty == LinkProtocol.DifficultyCustom && HuntTier != CustomTier) return false;
            if (Difficulty != LinkProtocol.DifficultyWeekly && Difficulty != LinkProtocol.DifficultyCustom
                && SpeedIndex != 2) return false;
            return true;
        }
    }

    public sealed class SharedTrackerState
    {
        public Dictionary<string, int> Evidence { get; private set; } =
            LinkProtocol.Evidence.ToDictionary(name => name, _ => 0);
        public int Limit { get; set; } = 3;
        public Dictionary<string, bool> Hunt { get; private set; } =
            LinkProtocol.HuntKeys.ToDictionary(key => key, _ => false);
        public Dictionary<string, bool> Speed { get; private set; } =
            LinkProtocol.SpeedKeys.ToDictionary(key => key, _ => false);
        public Dictionary<string, int> Cards { get; private set; } = new();
        public RoomSettings Settings { get; set; } = new();

        public SharedTrackerState Clone()
        {
            var clone = new SharedTrackerState { Limit = Limit, Settings = Settings.Clone() };
            clone.Evidence = new Dictionary<string, int>(Evidence);
            clone.Hunt = new Dictionary<string, bool>(Hunt);
            clone.Speed = new Dictionary<string, bool>(Speed);
            clone.Cards = new Dictionary<string, int>(Cards);
            return clone;
        }

        public JsonObject ToJson()
        {
            var evidence = new JsonObject();
            foreach (string name in LinkProtocol.Evidence) evidence[name] = Evidence[name];

            var hunt = new JsonObject();
            foreach (string key in LinkProtocol.HuntKeys) hunt[key] = Hunt[key];

            var speed = new JsonObject();
            foreach (string key in LinkProtocol.SpeedKeys) speed[key] = Speed[key];

            var cards = new JsonObject();
            foreach (var pair in Cards) cards[pair.Key] = pair.Value;

            return new JsonObject
            {
                ["evidence"] = evidence,
                ["limit"] = Limit,
                ["hunt"] = hunt,
                ["speed"] = speed,
                ["cards"] = cards,
                ["settings"] = Settings.ToJson()
            };
        }

        public static SharedTrackerState? Parse(JsonElement value)
        {
            if (!LinkProtocol.HasExactKeys(value, "evidence", "limit", "hunt", "speed", "cards", "settings"))
                return null;
            if (!LinkProtocol.TryInt(value, "limit", 0, 3, out int limit)) return null;

            var settings = RoomSettings.Parse(value.GetProperty("settings"));
            if (settings == null) return null;

            var state = new SharedTrackerState { Limit = limit, Settings = settings };

            var evidence = value.GetProperty("evidence");
            if (!LinkProtocol.HasExactKeys(evidence, LinkProtocol.Evidence)) return null;
            foreach (string name in LinkProtocol.Evidence)
            {
                if (!LinkProtocol.TryInt(evidence, name, 0, 2, out int tri)) return null;
                state.Evidence[name] = tri;
            }

            if (!ParseFlags(value.GetProperty("hunt"), LinkProtocol.HuntKeys, state.Hunt)) return null;
            if (state.Hunt.Values.Count(on => on) > 1) return null;
            if (!ParseFlags(value.GetProperty("speed"), LinkProtocol.SpeedKeys, state.Speed)) return null;

            var cards = value.GetProperty("cards");
            if (cards.ValueKind != JsonValueKind.Object) return null;
            int marked = 0;
            foreach (var card in cards.EnumerateObject())
            {
                if (state.Cards.Count >= LinkProtocol.MaxCards) return null;
                if (!LinkProtocol.ValidCardName(card.Name)) return null;
                if (card.Value.ValueKind != JsonValueKind.Number
                    || !card.Value.TryGetInt32(out int cardState)
                    || cardState < 0 || cardState > 2) return null;
                if (cardState == 1 && ++marked > 1) return null;
                state.Cards[card.Name] = cardState;
            }

            return state;
        }

        private static bool ParseFlags(JsonElement source, IReadOnlyList<string> keys, Dictionary<string, bool> target)
        {
            if (!LinkProtocol.HasExactKeys(source, keys)) return false;
            foreach (string key in keys)
            {
                var flag = source.GetProperty(key);
                if (flag.ValueKind != JsonValueKind.True && flag.ValueKind != JsonValueKind.False) return false;
                target[key] = flag.ValueKind == JsonValueKind.True;
            }
            return true;
        }

        /// <summary>Applies one authoritative change from a server patch. Returns false if invalid.</summary>
        public bool ApplyChange(RemoteChange change)
        {
            switch (change.Field)
            {
                case "limit":
                    if (change.IntValue < 0 || change.IntValue > 3) return false;
                    Limit = change.IntValue;
                    return true;
                case "settings":
                    if (change.Settings == null || !change.Settings.IsValid()) return false;
                    Settings = change.Settings.Clone();
                    return true;
                case "evidence":
                    if (!Evidence.ContainsKey(change.Key) || change.IntValue < 0 || change.IntValue > 2) return false;
                    Evidence[change.Key] = change.IntValue;
                    return true;
                case "hunt":
                    if (!Hunt.ContainsKey(change.Key)) return false;
                    if (change.BoolValue && Hunt.Any(pair => pair.Key != change.Key && pair.Value)) return false;
                    Hunt[change.Key] = change.BoolValue;
                    return true;
                case "speed":
                    if (!Speed.ContainsKey(change.Key)) return false;
                    Speed[change.Key] = change.BoolValue;
                    return true;
                case "card":
                    if (!LinkProtocol.ValidCardName(change.Key)) return false;
                    if (change.IntValue < 0 || change.IntValue > 2) return false;
                    if (change.IntValue != 0 && !Cards.ContainsKey(change.Key)
                        && Cards.Count >= LinkProtocol.MaxCards) return false;
                    if (change.IntValue == 1
                        && Cards.Any(pair => pair.Key != change.Key && pair.Value == 1)) return false;
                    if (change.IntValue == 0) Cards.Remove(change.Key);
                    else Cards[change.Key] = change.IntValue;
                    return true;
                default:
                    return false;
            }
        }

        public void Reset()
        {
            foreach (string name in LinkProtocol.Evidence) Evidence[name] = 0;
            foreach (string key in LinkProtocol.HuntKeys) Hunt[key] = false;
            foreach (string key in LinkProtocol.SpeedKeys) Speed[key] = false;
            Cards.Clear();
        }
    }

    public sealed class RemoteChange
    {
        public string Field { get; init; } = "";
        public string Key { get; init; } = "";
        public int IntValue { get; init; }
        public bool BoolValue { get; init; }
        public RoomSettings? Settings { get; init; }
    }
}
