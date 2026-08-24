using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhasOverlay.Link
{
    public static class LinkProtocol
    {
        public const int Version = 2;
        public const int RoomLimit = 8;
        public const int MaxIntentBytes = 4096;
        public const int MaxServerMessageBytes = 16384;
        public const int MaxCards = 64;
        public const int MaxPatchChanges = 8;
        public const int DifficultyWeekly = 5;
        public const int DifficultyCustom = 6;
        public const string NativeClientHeader = "X-PhasOverlay-Client";
        public const string NativeClientValue = "desktop-v1";
        public const string InviteBase = "https://phasoverlay.xyz/";

        public const string RoomAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static readonly IReadOnlyList<string> Evidence = new[]
        {
            "EMF Level 5",
            "D.O.T.S Projector",
            "Ultraviolet",
            "Freezing Temperatures",
            "Ghost Orb",
            "Ghost Writing",
            "Spirit Box"
        };
        public static readonly IReadOnlyList<string> HuntKeys = new[] { "veryearly", "early", "normal", "late" };
        public static readonly IReadOnlyList<string> SpeedKeys = new[] { "slow", "normal", "fast" };

        private static readonly Regex RoomCodeRe = new("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{6}$", RegexOptions.Compiled);
        private static readonly Regex TokenRe = new("^[A-Za-z0-9_-]{43}$", RegexOptions.Compiled);
        private static readonly Regex ParticipantIdRe = new("^[A-Za-z0-9_-]{22}$", RegexOptions.Compiled);
        private static readonly Regex ControlRe = new(
            "[\u0000-\u001f\u007f-\u009f\u061c\u200e\u200f\u202a-\u202e\u2066-\u2069]",
            RegexOptions.Compiled);
        private static readonly Regex CardControlRe = new("[\u0000-\u001f\u007f]", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRe = new("\\s+", RegexOptions.Compiled);

        private static readonly HashSet<string> BlockedCardNames = new(StringComparer.Ordinal)
        {
            "__proto__", "prototype", "constructor"
        };

        public static bool ValidRoomCode(string? value) => value != null && RoomCodeRe.IsMatch(value);
        public static bool ValidToken(string? value) => value != null && TokenRe.IsMatch(value);
        public static bool ValidParticipantId(string? value) => value != null && ParticipantIdRe.IsMatch(value);
        public static bool ValidContentHash(string? value) => ValidToken(value);

        public static bool ValidCardName(string? value) =>
            !string.IsNullOrEmpty(value) && value!.Length <= 64
            && !CardControlRe.IsMatch(value) && !BlockedCardNames.Contains(value);

        public static string? NormalizeUsername(string? value)
        {
            if (value == null) return null;
            string normalized = WhitespaceRe.Replace(value.Normalize(NormalizationForm.FormC).Trim(), " ");
            int codePoints = CountCodePoints(normalized);
            if (codePoints < 2 || codePoints > 20) return null;
            if (ControlRe.IsMatch(normalized)) return null;
            return normalized;
        }

        private static int CountCodePoints(string value)
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) i++;
                count++;
            }
            return count;
        }

        public static string NewRoomCode()
        {
            var chars = new char[6];
            int limit = 256 / RoomAlphabet.Length * RoomAlphabet.Length;
            int filled = 0;
            Span<byte> buffer = stackalloc byte[6];
            while (filled < chars.Length)
            {
                RandomNumberGenerator.Fill(buffer);
                foreach (byte value in buffer)
                {
                    if (value >= limit) continue;
                    chars[filled++] = RoomAlphabet[value % RoomAlphabet.Length];
                    if (filled == chars.Length) break;
                }
            }
            return new string(chars);
        }

        public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));
        public static string NewParticipantId() => Base64Url(RandomNumberGenerator.GetBytes(16));

        public static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        public static string HashToken(string token) =>
            Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        /// <summary>Compatibility key for a room. Matches the browser's normalization of the same file.</summary>
        public static string ContentHash(string ghostJson) =>
            HashToken(ghostJson.Replace("\r\n", "\n").Trim());

        public static string InviteUrl(string code) => InviteBase + "?room=" + code;

        public static string? RoomCodeFromInvite(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string trimmed = value.Trim();
            string direct = trimmed.Replace(" ", "").ToUpperInvariant();
            if (ValidRoomCode(direct)) return direct;

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
            foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int split = pair.IndexOf('=');
                if (split <= 0) continue;
                if (!pair.AsSpan(0, split).Equals("room", StringComparison.OrdinalIgnoreCase)) continue;
                string code = Uri.UnescapeDataString(pair[(split + 1)..]).ToUpperInvariant();
                if (ValidRoomCode(code)) return code;
            }
            return null;
        }

        public static bool HasExactKeys(JsonElement value, params string[] expected) =>
            HasExactKeys(value, (IReadOnlyList<string>)expected);

        public static bool HasExactKeys(JsonElement value, IReadOnlyList<string> expected)
        {
            if (value.ValueKind != JsonValueKind.Object) return false;
            int count = 0;
            foreach (var property in value.EnumerateObject())
            {
                if (!expected.Contains(property.Name, StringComparer.Ordinal)) return false;
                count++;
            }
            return count == expected.Count;
        }

        public static bool TryInt(JsonElement owner, string name, int min, int max, out int result)
        {
            result = 0;
            if (!owner.TryGetProperty(name, out var value)) return false;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int parsed)) return false;
            if (parsed < min || parsed > max) return false;
            result = parsed;
            return true;
        }

        public static bool TryString(JsonElement owner, string name, out string result)
        {
            result = "";
            if (!owner.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return false;
            result = value.GetString() ?? "";
            return true;
        }
    }
}
