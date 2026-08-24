using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhasOverlay.Link
{
    public sealed class LinkProfile
    {
        public string Username { get; set; } = "";
        public string ParticipantId { get; set; } = "";
    }

    public class LinkStorage
    {
        private readonly string _directory;

        public LinkStorage(string? directory = null)
        {
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay");
        }

        private string ProfilePath => Path.Combine(_directory, "link.json");

        public LinkProfile? LoadProfile()
        {
            try
            {
                if (!File.Exists(ProfilePath)) return null;
                var root = JsonNode.Parse(File.ReadAllText(ProfilePath))?.AsObject();
                if (root == null) return null;

                string? username = LinkProtocol.NormalizeUsername((string?)root["username"]);
                string? id = (string?)root["participantId"];
                if (username == null || !LinkProtocol.ValidParticipantId(id)) return null;
                return new LinkProfile { Username = username, ParticipantId = id! };
            }
            catch { return null; }
        }

        public bool SaveProfile(LinkProfile profile)
        {
            string? username = LinkProtocol.NormalizeUsername(profile.Username);
            if (username == null || !LinkProtocol.ValidParticipantId(profile.ParticipantId)) return false;

            var root = ReadRoot();
            root["username"] = username;
            root["participantId"] = profile.ParticipantId;
            return WriteRoot(root);
        }

        public string? LoadRoomCode()
        {
            var code = (string?)ReadRoot()["room"];
            return LinkProtocol.ValidRoomCode(code) ? code : null;
        }

        public bool SaveRoomCode(string? code)
        {
            var root = ReadRoot();
            if (code == null) root.Remove("room");
            else if (LinkProtocol.ValidRoomCode(code)) root["room"] = code;
            else return false;
            return WriteRoot(root);
        }

        /// <summary>Returns the host token for a room, or null when absent or unreadable. A failed
        /// unprotect is a lost host credential, never a reason to fall back to plaintext.</summary>
        public string? LoadHostToken(string code)
        {
            if (!LinkProtocol.ValidRoomCode(code)) return null;
            var root = ReadRoot();
            if (((string?)root["hostRoom"]) != code) return null;

            string? sealedToken = (string?)root["hostToken"];
            if (string.IsNullOrEmpty(sealedToken)) return null;

            string? token = Unprotect(sealedToken);
            return LinkProtocol.ValidToken(token) ? token : null;
        }

        public bool SaveHostToken(string code, string? token)
        {
            var root = ReadRoot();
            if (token == null)
            {
                root.Remove("hostRoom");
                root.Remove("hostToken");
                return WriteRoot(root);
            }

            if (!LinkProtocol.ValidRoomCode(code) || !LinkProtocol.ValidToken(token)) return false;
            string? sealedToken = Protect(token);
            if (sealedToken == null) return false;

            root["hostRoom"] = code;
            root["hostToken"] = sealedToken;
            return WriteRoot(root);
        }

        private JsonObject ReadRoot()
        {
            try
            {
                if (!File.Exists(ProfilePath)) return new JsonObject();
                return JsonNode.Parse(File.ReadAllText(ProfilePath))?.AsObject() ?? new JsonObject();
            }
            catch { return new JsonObject(); }
        }

        private bool WriteRoot(JsonObject root)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(ProfilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch { return false; }
        }

        protected virtual string? Protect(string value)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                return Convert.ToBase64String(Dpapi.Protect(bytes));
            }
            catch { return null; }
        }

        protected virtual string? Unprotect(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Dpapi.Unprotect(Convert.FromBase64String(value)));
            }
            catch { return null; }
        }
    }

    internal static class Dpapi
    {
        private const int CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved,
            IntPtr prompt, int flags, out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved,
            IntPtr prompt, int flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);

        public static byte[] Protect(byte[] value) => Run(value, true);
        public static byte[] Unprotect(byte[] value) => Run(value, false);

        private static byte[] Run(byte[] value, bool protect)
        {
            var input = new DataBlob();
            var output = new DataBlob();
            try
            {
                input.cbData = value.Length;
                input.pbData = Marshal.AllocHGlobal(value.Length);
                Marshal.Copy(value, 0, input.pbData, value.Length);

                bool ok = protect
                    ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
                if (!ok) throw new InvalidOperationException("dpapi_failed");

                byte[] result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }
    }
}
