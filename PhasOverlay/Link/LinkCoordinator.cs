using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PhasOverlay.Link
{
    public enum LinkStatus
    {
        Idle,
        Creating,
        Connecting,
        Connected,
        Reconnecting,
        Error
    }

    public sealed class LinkParticipant
    {
        public string Id { get; init; } = "";
        public string Username { get; init; } = "";
        public bool IsHost { get; init; }
    }

    public sealed class LinkStateChange
    {
        public LinkStatus Status { get; init; }
        public string RoomCode { get; init; } = "";
        public bool IsHost { get; init; }
        public string Notice { get; init; } = "";
        public IReadOnlyList<LinkParticipant> Participants { get; init; } = Array.Empty<LinkParticipant>();
    }

    /// <summary>
    /// Owns the room and the socket for the application's lifetime. Windows subscribe to its
    /// events; it never touches a window directly.
    /// </summary>
    public sealed class LinkCoordinator : IDisposable
    {
        public static readonly TimeSpan DefaultSnapshotTimeout = TimeSpan.FromSeconds(10);

        private readonly LinkClient _client;
        private readonly LinkStorage _storage;
        private readonly Action<Action> _dispatch;
        private readonly Random _jitter = new();
        private readonly object _reconnectGate = new();
        private readonly object _snapshotGate = new();
        private readonly TimeSpan _snapshotTimeout;
        private readonly Func<int, CancellationToken, Task>? _reconnectDelay;

        private CancellationTokenSource _lifetime = new();
        private CancellationTokenSource? _snapshotDeadline;
        private int _snapshotGeneration = -1;
        private Task? _reconnectSupervisor;
        private long _reconnectRequest;
        private int _reconnectSupervisorId;
        private int _reconnectAttempt;
        private string _contentHash = "";
        private string _candidateToken = "";
        private LinkProfile? _profile;
        private TaskCompletionSource<bool>? _roomEnded;

        public LinkCoordinator(
            Uri service,
            LinkStorage storage,
            Action<Action> dispatch,
            LinkClient? client = null,
            TimeSpan? snapshotTimeout = null,
            Func<int, CancellationToken, Task>? reconnectDelay = null)
        {
            _storage = storage;
            _dispatch = dispatch;
            _client = client ?? new LinkClient(service);
            _snapshotTimeout = snapshotTimeout ?? DefaultSnapshotTimeout;
            if (_snapshotTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(snapshotTimeout));
            _reconnectDelay = reconnectDelay;
            _client.MessageReceived += OnMessage;
            _client.SocketClosed += OnClosed;
        }

        public LinkStatus Status { get; private set; } = LinkStatus.Idle;
        public string RoomCode { get; private set; } = "";
        public bool IsHost { get; private set; }
        public long Revision { get; private set; } = -1;
        public SharedTrackerState? State { get; private set; }
        public IReadOnlyList<LinkParticipant> Participants { get; private set; } = Array.Empty<LinkParticipant>();
        public bool IsLinked => Status is LinkStatus.Creating or LinkStatus.Connecting
            or LinkStatus.Connected or LinkStatus.Reconnecting;
        public bool CanEditShared => Status == LinkStatus.Connected;
        public bool SettingsLocked => IsLinked && (!IsHost || Status != LinkStatus.Connected || ConfigPending);
        public bool ConfigPending { get; private set; }

        public string Username => _profile?.Username ?? "";
        public string ParticipantId => _profile?.ParticipantId ?? "";
        public bool HasProfile => _profile != null;

        public event Action<LinkStateChange>? Changed;
        public event Action<SharedTrackerState>? SnapshotApplied;
        public event Action<IReadOnlyList<RemoteChange>, bool>? PatchApplied;

        /// <summary>
        /// True when the room owns this change, so the caller must not apply it locally. Still true
        /// while reconnecting, where the change is dropped rather than queued.
        /// </summary>
        public bool TrySetEvidence(string evidence, int value) =>
            TrySend(new JsonObject { ["type"] = "set", ["field"] = "evidence", ["key"] = evidence, ["value"] = value });

        public bool TrySetFilter(string field, string key, bool value) =>
            TrySend(new JsonObject { ["type"] = "set", ["field"] = field, ["key"] = key, ["value"] = value });

        public bool TrySetCard(string ghost, int value) =>
            TrySend(new JsonObject { ["type"] = "set", ["field"] = "card", ["key"] = ghost, ["value"] = value });

        public bool TryReset() => TrySend(new JsonObject { ["type"] = "reset" });

        public bool TryConfigure(RoomSettings settings, int limit)
        {
            if (!IsLinked) return false;
            if (!IsHost || ConfigPending || Status != LinkStatus.Connected) return true;
            if (settings == null || !settings.IsValid()) return true;

            ConfigPending = true;
            bool sent = TrySend(new JsonObject
            {
                ["type"] = "configure",
                ["settings"] = settings.ToJson(),
                ["limit"] = Math.Clamp(limit, 0, 3)
            });
            if (!sent) ConfigPending = false;
            RaiseChanged();
            return true;
        }

        private bool TrySend(JsonObject intent)
        {
            if (!IsLinked) return false;
            if (Status != LinkStatus.Connected)
            {
                SetStatus(Status, "Reconnecting before that change can be applied.");
                return true;
            }
            int generation = _client.Generation;
            _ = SendIntentAsync(intent, generation);
            return true;
        }

        private async Task SendIntentAsync(JsonObject intent, int generation)
        {
            bool sent;
            try { sent = await _client.SendAsync(intent, _lifetime.Token); }
            catch (OperationCanceledException) { return; }
            if (sent || generation != _client.Generation) return;

            _dispatch(() =>
            {
                if (generation != _client.Generation || Status != LinkStatus.Connected) return;
                _client.Abort();
                ScheduleReconnect();
            });
        }

        /// <summary>Loads the stored profile, or creates one for this username. The participant id
        /// is generated once and reused, so a reconnect replaces the old socket in the roster.</summary>
        public bool SetUsername(string username)
        {
            string? normalized = LinkProtocol.NormalizeUsername(username);
            if (normalized == null) return false;

            var existing = _profile ?? _storage.LoadProfile();
            var profile = new LinkProfile
            {
                Username = normalized,
                ParticipantId = existing?.ParticipantId ?? LinkProtocol.NewParticipantId()
            };
            _profile = profile;
            _storage.SaveProfile(profile);

            if (Status == LinkStatus.Connected)
                TrySend(new JsonObject { ["type"] = "rename", ["username"] = normalized });

            RaiseChanged();
            return true;
        }

        public LinkProfile? LoadStoredProfile() => _profile ??= _storage.LoadProfile();

        public async Task<bool> CreateRoomAsync(SharedTrackerState state, string contentHash)
        {
            if (LoadStoredProfile() == null || !LinkProtocol.ValidContentHash(contentHash))
            {
                Fail("Choose a username before creating a room.");
                return false;
            }

            _contentHash = contentHash;
            ResetLifetime();
            SetStatus(LinkStatus.Creating, "");

            string hostToken = LinkProtocol.NewToken();
            for (int attempt = 0; attempt < 4; attempt++)
            {
                string code = LinkProtocol.NewRoomCode();
                if (!_storage.SaveHostToken(code, hostToken))
                {
                    Fail("Windows could not protect the host key, so the room was not created.");
                    return false;
                }

                RoomCreateResult result;
                try
                {
                    result = await _client.CreateRoomAsync(code, state, hostToken, contentHash, _lifetime.Token);
                }
                catch (OperationCanceledException)
                {
                    _storage.SaveHostToken(code, null);
                    throw;
                }
                if (result == RoomCreateResult.CodeTaken)
                {
                    _storage.SaveHostToken(code, null);
                    continue;
                }
                if (result != RoomCreateResult.Created)
                {
                    _storage.SaveHostToken(code, null);
                    Fail(result == RoomCreateResult.Unavailable
                        ? "The Link service is unavailable. Check your connection and try again."
                        : "The room could not be created. Try again.");
                    return false;
                }

                RoomCode = code;
                Revision = -1;
                IsHost = false;
                ConfigPending = false;
                _reconnectAttempt = 0;
                _storage.SaveRoomCode(code);
                return await OpenAsync(false);
            }

            Fail("A unique room code could not be created. Try again.");
            return false;
        }

        public bool TryTransferHost(string participantId)
        {
            if (!IsHost || Status != LinkStatus.Connected) return false;
            if (!LinkProtocol.ValidParticipantId(participantId)) return false;
            if (participantId == ParticipantId) return false;
            return TrySend(new JsonObject { ["type"] = "transfer_host", ["targetId"] = participantId });
        }

        public async Task<bool> JoinAsync(string codeOrInvite, string contentHash)
        {
            string? code = LinkProtocol.RoomCodeFromInvite(codeOrInvite);
            if (code == null || !LinkProtocol.ValidContentHash(contentHash))
            {
                Fail("Enter a valid 6-character room code.");
                return false;
            }

            _contentHash = contentHash;
            RoomCode = code;
            Revision = -1;
            IsHost = false;
            ConfigPending = false;
            Participants = Array.Empty<LinkParticipant>();
            _reconnectAttempt = 0;
            ResetLifetime();
            return await OpenAsync(false);
        }

        /// <summary>
        /// A host leaving ends the room, so the intent goes first and the socket is given a moment
        /// to receive the authoritative room_ended before it is closed.
        /// </summary>
        public async Task LeaveAsync()
        {
            if (IsHost && Status == LinkStatus.Connected)
            {
                _roomEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    await _client.SendAsync(new JsonObject { ["type"] = "end_room" }, _lifetime.Token);
                }
                catch (OperationCanceledException) { }
                await Task.WhenAny(_roomEnded.Task, Task.Delay(1500));
                _roomEnded = null;
            }

            _storage.SaveHostToken(RoomCode, null);
            _storage.SaveRoomCode(null);
            ResetLifetime();
            await _client.DisconnectAsync(1000, "leave");
            RoomCode = "";
            IsHost = false;
            ConfigPending = false;
            Revision = -1;
            Participants = Array.Empty<LinkParticipant>();
            SetStatus(LinkStatus.Idle, "");
        }

        private async Task<bool> OpenAsync(bool reconnecting, bool scheduleFailure = true)
        {
            SetStatus(reconnecting ? LinkStatus.Reconnecting : LinkStatus.Connecting,
                reconnecting ? "Connection lost. Retrying automatically." : "");

            if (!await _client.ConnectAsync(RoomCode, _contentHash, _lifetime.Token))
            {
                if (scheduleFailure) ScheduleReconnect();
                return false;
            }

            int generation = _client.Generation;
            StartSnapshotDeadline(generation);
            if (await IdentifyAsync()) return true;

            await RecoverConnectionAsync(generation, "identify_failed", scheduleFailure);
            return false;
        }

        /// <summary>
        /// A fresh host-candidate token is generated per connection and only its hash is sent, so a
        /// transfer can grant host without the current host ever seeing the target's raw token.
        /// </summary>
        private async Task<bool> IdentifyAsync()
        {
            var profile = LoadStoredProfile();
            if (profile == null)
            {
                Fail("Choose a username before joining a room.");
                return false;
            }

            _candidateToken = LinkProtocol.NewToken();
            string? hostToken = _storage.LoadHostToken(RoomCode);

            var identify = new JsonObject
            {
                ["type"] = "identify",
                ["id"] = profile.ParticipantId,
                ["username"] = profile.Username,
                ["candidateHash"] = LinkProtocol.HashToken(_candidateToken),
                ["hostToken"] = hostToken == null ? null : JsonValue.Create(hostToken)
            };
            return await _client.SendAsync(identify, _lifetime.Token);
        }

        /// <summary>Promotes this connection's candidate token to the room's host token. The former
        /// host's copy stops working the moment the Worker rotates the stored hash.</summary>
        private void HandleHostGranted()
        {
            if (!LinkProtocol.ValidToken(_candidateToken)) return;

            IsHost = true;
            if (!_storage.SaveHostToken(RoomCode, _candidateToken))
            {
                SetStatus(Status, "Windows could not protect the host key. Host will not survive a restart.");
            }

            _candidateToken = LinkProtocol.NewToken();
            TrySend(new JsonObject
            {
                ["type"] = "host_candidate",
                ["candidateHash"] = LinkProtocol.HashToken(_candidateToken)
            });
            RaiseChanged();
        }

        private void ScheduleReconnect()
        {
            if (_lifetime.IsCancellationRequested || RoomCode.Length == 0) return;
            SetStatus(LinkStatus.Reconnecting, "Connection lost. Retrying automatically.");

            lock (_reconnectGate)
            {
                _reconnectRequest++;
                if (_reconnectSupervisor is { IsCompleted: false }) return;

                int id = ++_reconnectSupervisorId;
                _reconnectSupervisor = RunReconnectSupervisorAsync(id, _lifetime.Token);
            }
        }

        private async Task RunReconnectSupervisorAsync(int id, CancellationToken token)
        {
            await Task.Yield();
            try
            {
                while (!token.IsCancellationRequested && RoomCode.Length > 0)
                {
                    long request;
                    lock (_reconnectGate) request = _reconnectRequest;

                    int attempt = _reconnectAttempt++;
                    if (_reconnectDelay == null)
                    {
                        int delay = LinkBackoff.DelayMs(attempt, _jitter.NextDouble());
                        await Task.Delay(delay, token);
                    }
                    else
                    {
                        await _reconnectDelay(attempt, token);
                    }
                    if (token.IsCancellationRequested || RoomCode.Length == 0) return;

                    bool opened = await OpenAsync(true, false);
                    if (!opened) continue;

                    lock (_reconnectGate)
                    {
                        if (id != _reconnectSupervisorId) return;
                        if (request != _reconnectRequest) continue;
                        _reconnectSupervisor = null;
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_reconnectGate)
                {
                    if (id == _reconnectSupervisorId) _reconnectSupervisor = null;
                }
            }
        }

        private void OnClosed(LinkClosedInfo info, int generation)
        {
            if (generation != _client.Generation) return;
            _dispatch(() =>
            {
                if (generation != _client.Generation) return;
                CancelSnapshotDeadline(generation);
                switch (info.Kind)
                {
                    case LinkCloseKind.RoomEnded:
                        Fail("The host ended the room.");
                        return;
                    case LinkCloseKind.Replaced:
                        Fail("This Link profile connected somewhere else.");
                        return;
                    case LinkCloseKind.Rejected:
                        Fail("The room refused the connection.");
                        return;
                    default:
                        _client.Abort();
                        ScheduleReconnect();
                        return;
                }
            });
        }

        private void OnMessage(string payload, int generation)
        {
            if (generation != _client.Generation) return;
            var node = LinkClient.ParseServerMessage(payload);
            if (node is not JsonObject message) return;
            _dispatch(() =>
            {
                if (generation != _client.Generation) return;
                Handle(message);
            });
        }

        private void Handle(JsonObject message)
        {
            if (!TryString(message, "type", out string type)) return;
            switch (type)
            {
                case "snapshot": HandleSnapshot(message); return;
                case "patch": HandlePatch(message); return;
                case "reset": HandleReset(message); return;
                case "presence": HandlePresence(message); return;
                case "host": HandleHost(message); return;
                case "host_granted":
                    if (!HasExactKeys(message, "type") || !LinkProtocol.ValidToken(_candidateToken)) return;
                    HandleHostGranted();
                    return;
                case "room_ended":
                    if (!HasExactKeys(message, "type", "reason")
                        || !TryString(message, "reason", out string reason)
                        || reason is not ("host_left" or "inactive")) return;
                    _roomEnded?.TrySetResult(true);
                    _storage.SaveHostToken(RoomCode, null);
                    Fail("The room ended.");
                    return;
                case "error": HandleError(message); return;
            }
        }

        private void HandleSnapshot(JsonObject message)
        {
            if (!HasExactKeys(message, "type", "revision", "participants", "host", "state")) return;
            if (!TryLong(message, "revision", 0, out long revision)) return;
            if (!TryBool(message, "host", out bool isHost)) return;
            if (!TryReadParticipants(message, out var participants)) return;
            var element = ToElement(message["state"]);
            if (element == null) return;
            var state = SharedTrackerState.Parse(element.Value);
            if (state == null) return;

            CancelSnapshotDeadline();
            Revision = revision;
            IsHost = isHost;
            if (!isHost) _storage.SaveHostToken(RoomCode, null);
            ConfigPending = false;
            Participants = participants;
            _reconnectAttempt = 0;

            State = state;
            SetStatus(LinkStatus.Connected, "");
            SnapshotApplied?.Invoke(state);
        }

        private void HandlePatch(JsonObject message)
        {
            if (!HasExactKeys(message, "type", "revision", "changes")) return;
            if (!TryLong(message, "revision", 0, out long revision)) return;
            if (!AcceptRevision(revision)) return;
            if (message["changes"] is not JsonArray array
                || array.Count < 1 || array.Count > LinkProtocol.MaxPatchChanges) return;

            var changes = new List<RemoteChange>();
            foreach (var entry in array)
            {
                var change = ParseChange(entry);
                if (change == null) return;
                changes.Add(change);
            }

            var next = State?.Clone();
            if (next == null) return;
            foreach (var change in changes)
            {
                if (!next.ApplyChange(change)) return;
            }

            Revision = revision;
            State = next;

            bool completesConfiguration = changes.Any(change => change.Field == "settings");
            bool wasPending = ConfigPending;
            if (completesConfiguration) ConfigPending = false;
            PatchApplied?.Invoke(changes, false);
            if (wasPending && completesConfiguration) RaiseChanged();
        }

        private void HandleReset(JsonObject message)
        {
            if (!HasExactKeys(message, "type", "revision", "state")) return;
            if (!TryLong(message, "revision", 0, out long revision)) return;
            if (!AcceptRevision(revision)) return;

            var element = ToElement(message["state"]);
            if (element == null) return;
            var state = SharedTrackerState.Parse(element.Value);
            if (state == null) return;

            Revision = revision;
            State = state;
            PatchApplied?.Invoke(Array.Empty<RemoteChange>(), true);
            RaiseChanged();
        }

        /// <summary>A stale revision is ignored; a gap means a missed patch, so the socket is
        /// dropped and the reconnect brings a complete snapshot.</summary>
        private bool AcceptRevision(long revision)
        {
            if (revision <= Revision) return false;
            if (Revision >= 0 && revision != Revision + 1)
            {
                _ = ForceResyncAsync();
                return false;
            }
            return true;
        }

        private Task ForceResyncAsync()
        {
            CancelSnapshotDeadline();
            _client.Abort();
            _reconnectAttempt = 0;
            ScheduleReconnect();
            return Task.CompletedTask;
        }

        private void HandlePresence(JsonObject message)
        {
            if (!HasExactKeys(message, "type", "participants")) return;
            if (!TryReadParticipants(message, out var participants)) return;
            Participants = participants;
            RaiseChanged();
        }

        private void HandleHost(JsonObject message)
        {
            if (!HasExactKeys(message, "type", "host") || !TryBool(message, "host", out bool isHost)) return;
            bool wasHost = IsHost;
            // Dropped before IsHost moves, so an observer never sees a non-host holding a live token.
            if (wasHost && !isHost) _storage.SaveHostToken(RoomCode, null);
            IsHost = isHost;
            RaiseChanged();
        }

        private void HandleError(JsonObject message)
        {
            if (!HasExactKeys(message, "type", "code", "message")) return;
            if (!TryString(message, "code", out string code) || code.Length > 64) return;
            if (!TryString(message, "message", out string notice) || notice.Length > 256) return;
            if (code is "room_not_found" or "content_mismatch" or "schema_mismatch"
                or "protocol_mismatch" or "room_full" or "identity_in_use")
            {
                Fail(notice.Length > 0 ? notice : "The room refused the connection.");
                return;
            }
            ConfigPending = false;
            SetStatus(Status, notice);
        }

        private static bool TryReadParticipants(JsonObject message, out IReadOnlyList<LinkParticipant> participants)
        {
            participants = Array.Empty<LinkParticipant>();
            if (message["participants"] is not JsonArray array
                || array.Count < 1 || array.Count > LinkProtocol.RoomLimit) return false;
            var list = new List<LinkParticipant>();
            foreach (var entry in array)
            {
                if (entry is not JsonObject participant
                    || !HasExactKeys(participant, "id", "username", "host")) return false;
                if (!TryString(participant, "id", out string id)
                    || !LinkProtocol.ValidParticipantId(id)) return false;
                if (!TryString(participant, "username", out string username)
                    || LinkProtocol.NormalizeUsername(username) != username) return false;
                if (!TryBool(participant, "host", out bool isHost)) return false;
                if (list.Any(existing => existing.Id == id)) return false;
                list.Add(new LinkParticipant
                {
                    Id = id,
                    Username = username,
                    IsHost = isHost
                });
            }
            if (list.Count(p => p.IsHost) > 1) return false;
            participants = list;
            return true;
        }

        private static RemoteChange? ParseChange(JsonNode? entry)
        {
            if (entry is not JsonObject change) return null;
            if (!TryString(change, "field", out string field)) return null;

            if (field == "settings")
            {
                if (!HasExactKeys(change, "field", "value")) return null;
                var element = ToElement(change["value"]);
                if (element == null) return null;
                var settings = RoomSettings.Parse(element.Value);
                return settings == null ? null : new RemoteChange { Field = field, Settings = settings };
            }

            if (field == "limit")
            {
                if (!HasExactKeys(change, "field", "value")
                    || !TryInt(change, "value", out int limit)) return null;
                return new RemoteChange { Field = field, IntValue = limit };
            }

            if (!HasExactKeys(change, "field", "key", "value")
                || !TryString(change, "key", out string key)) return null;

            if (field is "hunt" or "speed")
            {
                if (!TryBool(change, "value", out bool flag)) return null;
                return new RemoteChange { Field = field, Key = key, BoolValue = flag };
            }

            if (field is "evidence" or "card")
            {
                if (!TryInt(change, "value", out int value)) return null;
                return new RemoteChange { Field = field, Key = key, IntValue = value };
            }

            return null;
        }

        private static bool HasExactKeys(JsonObject value, params string[] expected)
        {
            if (value.Count != expected.Length) return false;
            foreach (string key in expected)
            {
                if (!value.ContainsKey(key)) return false;
            }
            return true;
        }

        private static bool TryString(JsonObject owner, string name, out string value)
        {
            value = "";
            if (owner[name] is not JsonValue node
                || !node.TryGetValue(out string? parsed) || parsed == null) return false;
            value = parsed;
            return true;
        }

        private static bool TryBool(JsonObject owner, string name, out bool value)
        {
            value = false;
            return owner[name] is JsonValue node && node.TryGetValue(out value);
        }

        private static bool TryInt(JsonObject owner, string name, out int value)
        {
            value = 0;
            return owner[name] is JsonValue node && node.TryGetValue(out value);
        }

        private static bool TryLong(JsonObject owner, string name, long minimum, out long value)
        {
            value = 0;
            return owner[name] is JsonValue node && node.TryGetValue(out value) && value >= minimum;
        }

        private static JsonElement? ToElement(JsonNode? node)
        {
            if (node == null) return null;
            try { return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString()); }
            catch (JsonException) { return null; }
        }

        private void Fail(string notice)
        {
            ResetLifetime();
            _ = _client.DisconnectAsync(1000, "ended");
            RoomCode = "";
            IsHost = false;
            ConfigPending = false;
            Participants = Array.Empty<LinkParticipant>();
            SetStatus(LinkStatus.Error, notice);
        }

        private void ResetLifetime()
        {
            CancelSnapshotDeadline();
            try { _lifetime.Cancel(); } catch { }
            _lifetime.Dispose();
            lock (_reconnectGate)
            {
                _reconnectRequest = 0;
                _reconnectSupervisor = null;
                _reconnectSupervisorId++;
            }
            _lifetime = new CancellationTokenSource();
        }

        private void SetStatus(LinkStatus status, string notice)
        {
            Status = status;
            _notice = notice;
            RaiseChanged();
        }

        private string _notice = "";

        private void RaiseChanged() => Changed?.Invoke(new LinkStateChange
        {
            Status = Status,
            RoomCode = RoomCode,
            IsHost = IsHost,
            Notice = _notice,
            Participants = Participants
        });

        public void Dispose()
        {
            CancelSnapshotDeadline();
            try { _lifetime.Cancel(); } catch { }
            _lifetime.Dispose();
            _client.Dispose();
        }

        private void StartSnapshotDeadline(int generation)
        {
            CancelSnapshotDeadline();
            var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            lock (_snapshotGate)
            {
                _snapshotDeadline = deadline;
                _snapshotGeneration = generation;
            }
            _ = WaitForSnapshotAsync(deadline, generation);
        }

        private async Task WaitForSnapshotAsync(CancellationTokenSource deadline, int generation)
        {
            try { await Task.Delay(_snapshotTimeout, deadline.Token); }
            catch (OperationCanceledException) { return; }

            _dispatch(() =>
            {
                lock (_snapshotGate)
                {
                    if (_snapshotDeadline != deadline || _snapshotGeneration != generation) return;
                }
                if (generation != _client.Generation || Status == LinkStatus.Connected) return;
                _ = RecoverConnectionAsync(generation, "snapshot_timeout");
            });
        }

        private Task RecoverConnectionAsync(int generation, string reason, bool scheduleReconnect = true)
        {
            if (_lifetime.IsCancellationRequested || generation != _client.Generation) return Task.CompletedTask;
            CancelSnapshotDeadline(generation);
            _client.Abort();
            if (scheduleReconnect && !_lifetime.IsCancellationRequested) ScheduleReconnect();
            return Task.CompletedTask;
        }

        private void CancelSnapshotDeadline(int? generation = null)
        {
            CancellationTokenSource? deadline;
            lock (_snapshotGate)
            {
                if (generation.HasValue && _snapshotGeneration != generation.Value) return;
                deadline = _snapshotDeadline;
                _snapshotDeadline = null;
                _snapshotGeneration = -1;
            }
            if (deadline == null) return;
            try { deadline.Cancel(); } catch { }
            deadline.Dispose();
        }
    }
}
