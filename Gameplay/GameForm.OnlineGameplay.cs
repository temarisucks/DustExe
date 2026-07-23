using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Dust;

internal sealed partial class GameForm
{
    private const int OnlineGameplayProtocolVersion = 1;
    private const float OnlineSnapshotInterval = .09f;

    private static readonly JsonSerializerOptions OnlineGameplayJson =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private readonly ConcurrentQueue<OnlineMessage> _onlineGameplayInbox = new();
    private readonly Dictionary<string, OnlineRemotePlayer> _onlinePlayers =
        new(StringComparer.Ordinal);
    private long _onlineRunSeed;
    private long _onlineClientSequence;
    private long _onlineLastLocalMovementSequence;
    private long _onlineLastAcceptedLocalSequence;
    private long _onlineLastServerSequence;
    private long _onlineWorldRevision;
    private long _onlineSimulationTick;
    private long _onlineAuthorityRevision;
    private string _onlineAuthorityHostId = string.Empty;
    private float _onlineSnapshotTimer;
    private bool _onlineAppearanceSent;
    private bool _onlineLocalDefeated;
    private bool _onlineCompletionApplied;
    private bool _onlineFailureApplied;
    private bool _onlineLocalWarningActive;
    private bool _onlineRosterRefreshPending;
    private OnlineWorldSnapshot? _onlinePendingSnapshot;
    private long _onlineLastAppliedShopRevision;

    private bool IsOnlineGameplayActive =>
        _onlineMatchActive && _onlineLobby?.Seed is not null &&
        !string.IsNullOrWhiteSpace(_onlinePlayerId);

    private bool IsOnlineSimulationHost =>
        IsOnlineGameplayActive && IsOnlineLobbyHost &&
        _onlineClient.IsConnected && !_onlineReconnecting;

    private bool OnlineGameplayHostAvailable =>
        !IsOnlineGameplayActive ||
        _onlineClient.IsConnected && !_onlineReconnecting &&
        _onlineLobby is { } lobby &&
        lobby.Players.FirstOrDefault(player =>
            player.PlayerId == lobby.HostPlayerId)?.Connected == true;

    private bool IsOnlineLocalPlayerProtected =>
        IsOnlineGameplayActive && (_mode == ScreenMode.Shop || _onlineLocalDefeated);

    /// <summary>Called by the online lobby screen when the server starts a run.</summary>
    private void BeginOnlineRun(OnlineLobbyState state)
    {
        if (state.Seed is null || state.Players.Count is < 1 or > 4) return;

        _onlineRunSeed = state.Seed.Value;
        _activeRunSettings = state.Settings.Snapshot();
        _level = Math.Clamp(state.RunLevel, 1, 1000);
        _survivorDifficultyOffset = 0;
        _survivorDifficultyPenaltyPending = false;
        _onlineAuthorityRevision = state.AuthorityEpoch;
        _onlineAuthorityHostId = state.HostPlayerId;
        _onlineClientSequence = 0;
        _onlineLastLocalMovementSequence = 0;
        _onlineLastAcceptedLocalSequence = 0;
        _onlineLastServerSequence = 0;
        _onlineWorldRevision = 0;
        _onlineSimulationTick = 0;
        _onlineSnapshotTimer = 0;
        _onlineAppearanceSent = false;
        _onlineLocalDefeated = false;
        _onlineCompletionApplied = false;
        _onlineFailureApplied = false;
        _onlineLocalWarningActive = false;
        _onlineRosterRefreshPending = false;
        _onlinePendingSnapshot = null;
        _onlineLastAppliedShopRevision = 0;
        _onlinePlayers.Clear();
        while (_onlineGameplayInbox.TryDequeue(out _)) { }
        StartGame(preserveLevel: true);
    }

    /// <summary>
    /// UI transport callbacks enqueue gameplay envelopes here. They are applied
    /// from the WinForms tick, never from the socket receive thread.
    /// </summary>
    private void QueueOnlineGameplayMessage(OnlineMessage message) =>
        _onlineGameplayInbox.Enqueue(message);

    /// <summary>
    /// A movement request can be predicted locally and then disappear with a
    /// dropped socket. Let the first resumed checkpoint rewind that prediction
    /// instead of waiting forever for an acknowledgement that cannot arrive.
    /// </summary>
    private void ReconcileOnlinePredictionAfterReconnect()
    {
        _onlineLastLocalMovementSequence = _onlineLastAcceptedLocalSequence;
        CancelPendingTraversal();
    }

    /// <summary>Tracks disconnects and the server's deterministic host election.</summary>
    private void NotifyOnlineLobbyStateChanged(OnlineLobbyState state)
    {
        var previousHost = _onlineAuthorityHostId;
        _onlineAuthorityHostId = state.HostPlayerId;
        _onlineAuthorityRevision = Math.Max(_onlineAuthorityRevision, state.AuthorityEpoch);
        if (_mode == ScreenMode.Loading)
        {
            _onlineRosterRefreshPending = true;
            return;
        }

        var present = state.Players.Select(player => player.PlayerId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var removed in _onlinePlayers.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            DropOnlineCargo(_onlinePlayers[removed]);
            _onlinePlayers.Remove(removed);
        }

        foreach (var lobbyPlayer in state.Players)
        {
            if (lobbyPlayer.PlayerId == _onlinePlayerId) continue;
            var player = EnsureOnlinePlayer(lobbyPlayer);
            player.Connected = lobbyPlayer.Connected;
            if (!player.Connected && IsOnlineSimulationHost)
                DropOnlineCargo(player);
        }
        ReleaseUnavailableOnlineEscort(state);

        if (previousHost != _onlineAuthorityHostId && IsOnlineSimulationHost)
        {
            // The most recent authoritative checkpoint has already been applied
            // locally. The promoted peer resumes it under the new lobby revision.
            _onlineSnapshotTimer = OnlineSnapshotInterval;
            _onlineWorldRevision++;
        }
    }

    private void ReleaseUnavailableOnlineEscort(OnlineLobbyState state)
    {
        if (_survivorObjective is not
            {
                Stage: SurvivorObjectiveStage.Escorting,
                EscortPlayerId: { } escortId
            } objective)
            return;
        if (state.Players.Any(player =>
                player.PlayerId == escortId && player.Connected))
            return;
        objective.Stage = SurvivorObjectiveStage.Searching;
        objective.EscortPlayerId = null;
    }

    private void PrepareOnlineDeterministicGeneration()
    {
        if (!IsOnlineGameplayActive) return;
        _random = new DustRandom(_onlineRunSeed);
    }

    private void TickOnlineGameplay(float deltaTime)
    {
        if (!IsOnlineGameplayActive) return;
        // InitializeGameState constructs the world on a worker thread. Do not
        // enumerate or mutate that graph until the loading transition publishes
        // the completed state back to the UI thread.
        if (_maze is null || _mode == ScreenMode.Loading) return;
        if (_mode is not (ScreenMode.Playing or ScreenMode.Shop or
            ScreenMode.Won or ScreenMode.Failed))
            return;
        if (_onlineRosterRefreshPending && _onlineLobby is { } refreshedLobby)
        {
            _onlineRosterRefreshPending = false;
            NotifyOnlineLobbyStateChanged(refreshedLobby);
        }

        DrainOnlineGameplayMessages();
        _onlineSimulationTick++;

        EnsureOnlineRoster();
        if (IsOnlineSimulationHost)
        {
            foreach (var player in _onlinePlayers.Values)
            {
                if (player.MoveProgress >= 1 &&
                    player.PendingMoves.TryDequeue(out var pending))
                {
                    TryMoveOnlinePlayer(player, pending.Direction);
                    player.LastInputSequence = Math.Max(
                        player.LastInputSequence, pending.Sequence);
                }
            }
        }
        if (!_onlineAppearanceSent && _onlineClient.IsConnected && !_onlineReconnecting &&
            _mode is ScreenMode.Playing or ScreenMode.Shop)
        {
            _onlineAppearanceSent = true;
            SendOnlineAppearance();
        }

        UpdateOnlinePlayerPresentation(deltaTime);
        if (_onlinePendingSnapshot is not null && _mode is ScreenMode.Playing or ScreenMode.Shop)
        {
            var pending = _onlinePendingSnapshot;
            _onlinePendingSnapshot = null;
            ApplyOnlineSnapshot(pending);
        }

        if (!IsOnlineSimulationHost) return;
        _onlineSnapshotTimer += deltaTime;
        if (_onlineSnapshotTimer < OnlineSnapshotInterval) return;
        _onlineSnapshotTimer %= OnlineSnapshotInterval;
        SendOnlineSnapshot();
    }

    private void DrainOnlineGameplayMessages()
    {
        while (_onlineGameplayInbox.TryDequeue(out var message))
        {
            if (string.Equals(message.Type, "game.checkpoint", StringComparison.Ordinal))
            {
                ApplyOnlineCheckpointEnvelope(message.Data);
                continue;
            }
            if (!string.Equals(message.Type, "game.event", StringComparison.Ordinal))
                continue;
            var data = message.Data;
            if (!TryProperty(data, "serverSequence", out var serverNode) ||
                !serverNode.TryGetInt64(out var serverSequence) ||
                serverSequence <= _onlineLastServerSequence)
                continue;
            _onlineLastServerSequence = serverSequence;

            var kind = StringProperty(data, "kind");
            var sender = StringProperty(data, "senderPlayerId");
            var clientSequence = LongProperty(data, "clientSequence");
            var authorityEpoch = LongProperty(data, "authorityEpoch");
            if (authorityEpoch != _onlineLobby?.AuthorityEpoch) continue;
            if (!TryProperty(data, "payload", out var payload)) continue;

            if (string.Equals(kind, "input", StringComparison.Ordinal))
            {
                if (IsOnlineSimulationHost)
                    ApplyOnlineInput(sender, clientSequence, payload);
                continue;
            }

            if (!string.Equals(kind, "snapshot", StringComparison.Ordinal) ||
                IsOnlineSimulationHost ||
                sender != _onlineLobby?.HostPlayerId)
                continue;
            try
            {
                var snapshot = JsonSerializer.Deserialize<OnlineWorldSnapshot>(
                    payload.GetRawText(), OnlineGameplayJson);
                if (snapshot is not null) ApplyOnlineSnapshot(snapshot);
            }
            catch (JsonException)
            {
                // A malformed checkpoint is ignored; the next host checkpoint
                // repairs state without taking down the gameplay loop.
            }
        }
    }

    private void ApplyOnlineCheckpointEnvelope(JsonElement data)
    {
        if (!TryProperty(data, "available", out var available) ||
            available.ValueKind != JsonValueKind.True ||
            !TryProperty(data, "payload", out var payload))
            return;
        var authorityEpoch = LongProperty(data, "authorityEpoch");
        if (authorityEpoch != _onlineLobby?.AuthorityEpoch) return;
        var serverSequence = LongProperty(data, "serverSequence");
        var sourceAuthorityEpoch = LongProperty(data, "sourceAuthorityEpoch");
        var isMigration = sourceAuthorityEpoch > 0 &&
                          sourceAuthorityEpoch < authorityEpoch;
        if (!isMigration && serverSequence < _onlineLastServerSequence) return;
        try
        {
            var snapshot = JsonSerializer.Deserialize<OnlineWorldSnapshot>(
                payload.GetRawText(), OnlineGameplayJson);
            if (snapshot is null) return;
            // A migration checkpoint was authored by the previous host. The
            // relay supplies the new epoch and only the elected peer may publish
            // its continuation.
            snapshot.HostPlayerId = _onlineLobby?.HostPlayerId ?? snapshot.HostPlayerId;
            snapshot.AuthorityRevision = authorityEpoch;
            _onlineLastServerSequence = Math.Max(
                _onlineLastServerSequence, serverSequence);
            if (snapshot.WorldRevision <= _onlineWorldRevision)
                _onlineWorldRevision = Math.Max(0, snapshot.WorldRevision - 1);
            ApplyOnlineSnapshot(snapshot);
        }
        catch (JsonException)
        {
            // Wait for the active authority's next checkpoint.
        }
    }

    private OnlineRemotePlayer EnsureOnlinePlayer(OnlineLobbyPlayer lobbyPlayer)
    {
        if (_onlinePlayers.TryGetValue(lobbyPlayer.PlayerId, out var existing))
        {
            existing.Username = lobbyPlayer.Username;
            existing.JoinOrder = lobbyPlayer.JoinOrder;
            existing.Connected = lobbyPlayer.Connected;
            return existing;
        }

        var spawn = _maze is null ? Point.Empty : _playerCell;
        var paletteIndex = Math.Abs(lobbyPlayer.JoinOrder) % _palette.Length;
        var player = new OnlineRemotePlayer
        {
            PlayerId = lobbyPlayer.PlayerId,
            Username = lobbyPlayer.Username,
            JoinOrder = lobbyPlayer.JoinOrder,
            Connected = lobbyPlayer.Connected,
            Drone = (DroneModel)(Math.Abs(lobbyPlayer.JoinOrder) % _droneButtons.Length),
            CoreColor = _palette[paletteIndex],
            FrameColor = _palette[(paletteIndex + 5) % _palette.Length],
            Cell = spawn,
            PreviousCell = spawn,
            VisualCell = spawn,
            PreviousVisualCell = spawn,
            MoveFrom = spawn,
            MoveTo = spawn
        };
        _onlinePlayers.Add(player.PlayerId, player);
        return player;
    }

    private void EnsureOnlineRoster()
    {
        if (_onlineLobby is null) return;
        foreach (var lobbyPlayer in _onlineLobby.Players)
        {
            if (lobbyPlayer.PlayerId == _onlinePlayerId) continue;
            EnsureOnlinePlayer(lobbyPlayer);
        }
    }

    private void SendOnlineAppearance()
    {
        var perks = _settings.Progression.EquippedPerks
            .Select(perk => (int)perk)
            .ToArray();
        SendOnlineInput("appearance", new
        {
            drone = (int)_drone,
            coreArgb = _playerColor.ToArgb(),
            frameArgb = _playerFrameColor.ToArgb(),
            maximumHealth = GetMaximumHealth(),
            accountCredits = _settings.TotalCredits,
            shopRepairReserve = _shopRepairReserve,
            shopProtectionCharges = _shopProtectionCharges,
            equippedPerks = perks
        });
    }

    private void SendOnlineMoveIntent(Direction direction)
    {
        if (!IsOnlineGameplayActive) return;
        _onlineLastLocalMovementSequence = SendOnlineInput("move", new
        {
            direction = (int)direction
        });
        if (IsOnlineSimulationHost)
            _onlineLastAcceptedLocalSequence = _onlineLastLocalMovementSequence;
    }

    /// <returns>True when a non-host must wait for authoritative application.</returns>
    private bool RelayOnlineInteraction()
    {
        if (!IsOnlineGameplayActive) return false;
        if (_onlineLocalDefeated) return true;
        if (!OnlineGameplayHostAvailable)
        {
            ShowOnlineAuthorityUnavailableNotice();
            return true;
        }
        SendOnlineInput("interact", new { });
        return !IsOnlineSimulationHost;
    }

    /// <returns>True when a non-host must wait for authoritative application.</returns>
    private bool RelayOnlinePerkActivation()
    {
        if (!IsOnlineGameplayActive) return false;
        if (_onlineLocalDefeated) return true;
        if (!OnlineGameplayHostAvailable)
        {
            ShowOnlineAuthorityUnavailableNotice();
            return true;
        }
        SendOnlineInput("perk", new { });
        return !IsOnlineSimulationHost;
    }

    private void RelayOnlineShopLeave()
    {
        if (IsOnlineGameplayActive && OnlineGameplayHostAvailable)
            SendOnlineInput("leaveShop", new { });
    }

    private bool RelayOnlineShopPurchase(int stockIndex)
    {
        if (!IsOnlineGameplayActive || IsOnlineSimulationHost) return false;
        if (!OnlineGameplayHostAvailable)
        {
            ShowOnlineAuthorityUnavailableNotice();
            return true;
        }
        SendOnlineInput("shopBuy", new { stockIndex });
        StartShopDialogue("Request passed into the authority line. Hold still.");
        return true;
    }

    private bool RelayOnlineShopSale(SalvageKind kind)
    {
        if (!IsOnlineGameplayActive || IsOnlineSimulationHost) return false;
        if (!OnlineGameplayHostAvailable)
        {
            ShowOnlineAuthorityUnavailableNotice();
            return true;
        }
        SendOnlineInput("shopSell", new { salvageKind = (int)kind });
        StartShopDialogue("The kiosk is weighing your salvage against the shared ledger.");
        return true;
    }

    private void ShowOnlineAuthorityUnavailableNotice()
    {
        _missionNotice = "AUTHORITY SIGNAL LOST / HOLD POSITION";
        _missionNoticeTimer = 2.4f;
        _audio.Play(AudioCue.Select);
    }

    private long SendOnlineInput(string command, object body)
    {
        var sequence = Interlocked.Increment(ref _onlineClientSequence);
        if (_onlineClient is null || !_onlineClient.IsConnected) return sequence;
        _ = ObserveOnlineSendAsync(_onlineClient.SendAsync("game.input", new
        {
            clientSequence = sequence,
            payload = new
            {
                command,
                body
            }
        }));
        return sequence;
    }

    private void SendOnlineSnapshot()
    {
        if (_onlineClient is null || !_onlineClient.IsConnected || !IsOnlineSimulationHost)
            return;
        var snapshot = BuildOnlineSnapshot();
        var sequence = Interlocked.Increment(ref _onlineClientSequence);
        _ = ObserveOnlineSendAsync(_onlineClient.SendAsync("game.snapshot", new
        {
            clientSequence = sequence,
            authorityEpoch = _onlineLobby?.AuthorityEpoch ?? _onlineAuthorityRevision,
            payload = snapshot
        }));
    }

    private static async Task ObserveOnlineSendAsync(Task send)
    {
        try
        {
            await send;
        }
        catch
        {
            // ConnectionClosed owns the visible transport failure and reconnect
            // route. Input methods must never throw on the WinForms event thread.
        }
    }

    private void ApplyOnlineInput(string sender, long sequence, JsonElement payload)
    {
        if (sender == _onlinePlayerId)
        {
            _onlineLastAcceptedLocalSequence =
                Math.Max(_onlineLastAcceptedLocalSequence, sequence);
            return;
        }

        if (!_onlinePlayers.TryGetValue(sender, out var player))
        {
            var lobbyPlayer = _onlineLobby?.Players.FirstOrDefault(item =>
                item.PlayerId == sender);
            if (lobbyPlayer is null) return;
            player = EnsureOnlinePlayer(lobbyPlayer);
        }
        if (sequence <= player.LastReceivedInputSequence)
            return;
        player.LastReceivedInputSequence = sequence;
        var command = StringProperty(payload, "command");
        if (!TryProperty(payload, "body", out var body)) body = default;

        switch (command)
        {
            case "appearance":
                ApplyOnlineAppearance(player, body);
                break;
            case "move":
                if (TryProperty(body, "direction", out var directionNode) &&
                    directionNode.TryGetInt32(out var direction) &&
                    Enum.IsDefined(typeof(Direction), direction))
                {
                    if (_maze is null || player.MoveProgress < 1)
                    {
                        if (player.PendingMoves.Count < 2)
                            player.PendingMoves.Enqueue(
                                new OnlineMoveIntent(sequence, (Direction)direction));
                        else
                            player.LastInputSequence = sequence;
                    }
                    else
                    {
                        TryMoveOnlinePlayer(player, (Direction)direction);
                        player.LastInputSequence = sequence;
                    }
                }
                break;
            case "interact":
                TryInteractOnlinePlayer(player);
                break;
            case "perk":
                TryActivateOnlinePlayerPerk(player);
                break;
            case "leaveShop":
                player.InShop = false;
                break;
            case "shopBuy":
                ResolveOnlineShopPurchase(
                    player, IntProperty(body, "stockIndex", -1));
                break;
            case "shopSell":
                ResolveOnlineShopSale(
                    player, IntProperty(body, "salvageKind", -1));
                break;
        }
        _onlineWorldRevision++;
    }

    private static void ApplyOnlineAppearance(OnlineRemotePlayer player, JsonElement body)
    {
        var drone = IntProperty(body, "drone", 0);
        player.Drone = (DroneModel)Math.Clamp(drone, 0, 4);
        player.CoreColor = Color.FromArgb(IntProperty(body, "coreArgb",
            Color.FromArgb(119, 197, 152).ToArgb()));
        player.FrameColor = Color.FromArgb(IntProperty(body, "frameArgb",
            Color.FromArgb(181, 184, 151).ToArgb()));
        player.MaximumHealth = Math.Clamp(IntProperty(body, "maximumHealth", 3), 3, 5);
        player.AccountCredits = Math.Clamp(
            LongProperty(body, "accountCredits"), 0, 1_000_000_000_000L);
        player.ShopRepairReserve = Math.Clamp(
            IntProperty(body, "shopRepairReserve", 0), 0, 20);
        player.ShopProtectionCharges = Math.Clamp(
            IntProperty(body, "shopProtectionCharges", 0), 0, 20);
        player.AppearanceReady = true;
        player.EquippedPerks.Clear();
        if (!TryProperty(body, "equippedPerks", out var perks) ||
            perks.ValueKind != JsonValueKind.Array)
            return;
        foreach (var node in perks.EnumerateArray())
            if (node.TryGetInt32(out var value) && Enum.IsDefined(typeof(PerkId), value))
                player.EquippedPerks.Add((PerkId)value);
    }

    private void ResolveOnlineShopPurchase(OnlineRemotePlayer player, int stockIndex)
    {
        if (!player.Connected || player.Defeated || !player.InShop ||
            !player.AppearanceReady)
        {
            SetOnlineShopReply(player,
                "The account line is not ready for a purchase.", AudioCue.Select);
            return;
        }
        if (stockIndex < 0 || stockIndex >= _shopStock.Count)
        {
            SetOnlineShopReply(player,
                "That hook does not exist in this kiosk.", AudioCue.Select);
            return;
        }

        var item = _shopStock[stockIndex];
        if (item.Stock <= 0)
        {
            SetOnlineShopReply(player,
                "Empty hook. The shared stock is gone.", AudioCue.Select);
            return;
        }
        var available = player.AccountCredits > long.MaxValue - _fieldCredits
            ? long.MaxValue
            : player.AccountCredits + _fieldCredits;
        if (available < item.Price)
        {
            SetOnlineShopReply(player,
                "Your account is lighter than your request.", AudioCue.Select);
            return;
        }

        var fieldSpend = Math.Min(_fieldCredits, item.Price);
        _fieldCredits -= fieldSpend;
        player.AccountCredits -= item.Price - fieldSpend;
        item.Stock--;

        switch (item.Kind)
        {
            case ShopItemKind.FramePatch:
                ApplyOnlinePurchasedRepair(player, 1);
                SetOnlineShopReply(player,
                    "One fracture closed. The authority marked the stock.",
                    AudioCue.Confirm);
                break;
            case ShopItemKind.ReconstructionGel:
                ApplyOnlinePurchasedRepair(player, 2);
                SetOnlineShopReply(player,
                    "The gel remembers the shape your frame forgot.",
                    AudioCue.Confirm);
                break;
            case ShopItemKind.AegisFuse:
                player.ShopProtectionCharges++;
                SetOnlineShopReply(player,
                    "Ward armed. The next hit belongs to the fuse.",
                    AudioCue.Confirm);
                break;
        }
    }

    private static void ApplyOnlinePurchasedRepair(
        OnlineRemotePlayer player, int repairPoints)
    {
        var applied = Math.Min(player.Damage, Math.Max(0, repairPoints));
        player.Damage -= applied;
        player.ShopRepairReserve += Math.Max(0, repairPoints - applied);
    }

    private void ResolveOnlineShopSale(OnlineRemotePlayer player, int kindValue)
    {
        if (!player.Connected || player.Defeated || !player.InShop ||
            !player.AppearanceReady ||
            !Enum.IsDefined(typeof(SalvageKind), kindValue))
        {
            SetOnlineShopReply(player,
                "The kiosk rejected that salvage line.", AudioCue.Select);
            return;
        }

        var kind = (SalvageKind)kindValue;
        var salvage = _roomSalvage.Where(item =>
            item.Collected && !item.Sold && item.Kind == kind).ToArray();
        if (salvage.Length == 0)
        {
            SetOnlineShopReply(player,
                "That salvage has already cleared the shared ledger.",
                AudioCue.Select);
            return;
        }

        foreach (var item in salvage) item.Sold = true;
        var value = salvage.Sum(item => (long)item.Value);
        player.AccountCredits = value > long.MaxValue - player.AccountCredits
            ? long.MaxValue
            : player.AccountCredits + value;
        SetOnlineShopReply(player,
            $"{SalvageName(kind)}. {value:000} credits. The ledger is closed.",
            AudioCue.Collect);
    }

    private static void SetOnlineShopReply(
        OnlineRemotePlayer player, string message, AudioCue cue)
    {
        player.ShopTransactionRevision++;
        player.ShopMessage = message;
        player.ShopCue = (int)cue;
    }

    private void TryMoveOnlinePlayer(OnlineRemotePlayer player, Direction direction)
    {
        if (_maze is null || !player.Connected || player.Defeated || player.Extracted ||
            player.InShop || player.MoveProgress < 1) return;

        var traversal = new List<Point>(2);
        player.TraversalUsedGhostForm = false;
        var cursor = player.Cell;
        var distance = player.HasPerk(PerkId.Hop) ? 2 : 1;
        for (var step = 0; step < distance; step++)
        {
            var normallyOpen = _maze.CanMove(cursor, direction);
            var destination = _maze.Move(cursor, direction);
            var inBounds = destination.X >= 0 && destination.X < _maze.Width &&
                           destination.Y >= 0 && destination.Y < _maze.Height;
            var canPhase = player.GhostFormTimer > 0 && inBounds;
            if (!normallyOpen && !canPhase) break;
            if (IsSurvivorBlockingCell(destination)) break;
            player.TraversalUsedGhostForm |= !normallyOpen && canPhase;
            traversal.Add(destination);
            cursor = destination;
            if (cursor == _exitCell) break;
        }
        if (traversal.Count == 0) return;

        BeginRoomDoorTraversal(player.Cell, traversal);
        player.PreviousCell = player.Cell;
        player.Cell = traversal[^1];
        player.MoveFrom = player.VisualCell;
        player.MoveTo = player.Cell;
        player.MoveProgress = 0;
        player.Traversal.Clear();
        player.Traversal.AddRange(traversal);
    }

    private void UpdateOnlinePlayerPresentation(float deltaTime)
    {
        foreach (var player in _onlinePlayers.Values)
        {
            player.PreviousVisualCell = player.VisualCell;
            player.Invulnerability = Math.Max(0, player.Invulnerability - deltaTime);
            player.CamouflageTimer = Math.Max(0, player.CamouflageTimer - deltaTime);
            player.CamouflageCooldown = Math.Max(0, player.CamouflageCooldown - deltaTime);
            player.GhostFormTimer = Math.Max(0, player.GhostFormTimer - deltaTime);
            player.GhostFormCooldown = Math.Max(0, player.GhostFormCooldown - deltaTime);
            player.HollowKillerCooldown = Math.Max(0, player.HollowKillerCooldown - deltaTime);

            if (player.MoveProgress < 1)
            {
                player.MoveProgress = Math.Min(1, player.MoveProgress + deltaTime * 8.125f);
                var eased = 1f - MathF.Pow(1f - player.MoveProgress, 3);
                player.VisualCell = new PointF(
                    player.MoveFrom.X + (player.MoveTo.X - player.MoveFrom.X) * eased,
                    player.MoveFrom.Y + (player.MoveTo.Y - player.MoveFrom.Y) * eased);
                if (player.MoveProgress >= 1 && IsOnlineSimulationHost)
                    FinishOnlinePlayerTraversal(player);
            }

            var frameX = player.VisualCell.X - player.PreviousVisualCell.X;
            var frameY = player.VisualCell.Y - player.PreviousVisualCell.Y;
            var travel = MathF.Sqrt(frameX * frameX + frameY * frameY);
            var impulse = Math.Clamp(travel / .09f, 0, 1);
            var bank = travel > .0001f ? frameX / travel * impulse : 0;
            var pitch = travel > .0001f ? frameY / travel * impulse : 0;
            player.Bank += (bank - player.Bank) * .34f;
            player.Pitch += (pitch - player.Pitch) * .34f;
        }
    }

    private void FinishOnlinePlayerTraversal(OnlineRemotePlayer player)
    {
        var from = player.PreviousCell;
        foreach (var cell in player.Traversal)
        {
            OnlinePlayerEnteredCell(player, from, cell);
            from = cell;
        }
        player.Traversal.Clear();
        player.TraversalUsedGhostForm = false;
        if (player.Cell == _exitCell && CircuitObjectiveComplete &&
            _mode is ScreenMode.Playing or ScreenMode.Shop)
            CompleteWin();
    }

    private void OnlinePlayerEnteredCell(OnlineRemotePlayer player, Point from, Point to)
    {
        if (_maze is null) return;
        if (_maze.TryGetEnteredRoom(from, to, out var room))
        {
            _revealedRoomIds.Add(room.Id);
            _roomDoorOpenProgress[room.Id] = 1;
        }
        else if (player.TraversalUsedGhostForm &&
                 _maze.TryGetRoomAt(to, out var breachedRoom) &&
                 _maze.GetRoomAt(from)?.Id != breachedRoom.Id)
        {
            _revealedRoomIds.Add(breachedRoom.Id);
        }

        foreach (var pickup in _creditPickups.Where(item =>
                     !item.Collected && !item.MagnetMoving && item.Cell == to))
        {
            pickup.Collected = true;
            _fieldCredits += pickup.Value;
        }
        var salvage = _roomSalvage.FirstOrDefault(item =>
            !item.Collected && !item.Sold && item.Cell == to);
        if (salvage is not null) salvage.Collected = true;
    }

    private void TryInteractOnlinePlayer(OnlineRemotePlayer player)
    {
        if (_maze is null || !player.Connected || player.Defeated || player.Extracted ||
            player.MoveProgress < 1) return;

        if (player.InShop)
        {
            player.InShop = false;
            return;
        }

        var circuitSwitch = _circuitSwitches
            .Where(item => !item.Activated && CanOnlineInteract(player.Cell, item.Cell))
            .OrderBy(item => Manhattan(player.Cell, item.Cell))
            .ThenBy(item => item.Number)
            .FirstOrDefault();
        if (circuitSwitch is not null)
        {
            circuitSwitch.Activated = true;
            return;
        }

        if (TryInteractOnlineSurvivor(player)) return;
        if (_shopKiosk is not null && _shopKiosk.Cell == player.Cell)
        {
            player.InShop = true;
            return;
        }

        var cargo = _cargoItems
            .Where(item => !item.Carried && item.CarrierPlayerId is null &&
                           !item.Delivered && item.Required &&
                           CanOnlineInteract(player.Cell, item.Cell))
            .OrderBy(item => Manhattan(player.Cell, item.Cell))
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .FirstOrDefault();
        if (cargo is null) return;
        cargo.CarrierPlayerId = player.PlayerId;
    }

    private bool TryInteractOnlineSurvivor(OnlineRemotePlayer player)
    {
        if (_survivorObjective is not { } objective) return false;
        if (CanOnlineInteract(player.Cell, objective.WorkerCell) &&
            objective.Stage is SurvivorObjectiveStage.Uncontacted or SurvivorObjectiveStage.Searching)
        {
            objective.Stage = SurvivorObjectiveStage.Escorting;
            objective.EscortPlayerId = player.PlayerId;
            return true;
        }
        if (!CanOnlineInteract(player.Cell, objective.RequesterCell) ||
            objective.Stage == SurvivorObjectiveStage.Rescued)
            return false;
        if (objective.Stage == SurvivorObjectiveStage.Escorting &&
            objective.EscortPlayerId != player.PlayerId)
            return false;
        objective.Stage = objective.Stage switch
        {
            SurvivorObjectiveStage.Uncontacted => SurvivorObjectiveStage.Searching,
            SurvivorObjectiveStage.Escorting => SurvivorObjectiveStage.Rescued,
            _ => objective.Stage
        };
        if (objective.Stage == SurvivorObjectiveStage.Rescued)
            objective.EscortPlayerId = null;
        return true;
    }

    private bool CanOnlineInteract(Point playerCell, Point target)
    {
        if (playerCell == target) return true;
        if (_maze is null) return false;
        foreach (var direction in AllDirections)
            if (_maze.CanMove(playerCell, direction) &&
                _maze.Move(playerCell, direction) == target)
                return true;
        return false;
    }

    private void TryActivateOnlinePlayerPerk(OnlineRemotePlayer player)
    {
        if (!player.Connected || player.Defeated || player.Extracted || player.InShop) return;
        if (player.HasPerk(PerkId.HollowKiller))
        {
            if (player.HollowKillerCooldown > 0) return;
            player.HollowKillerCooldown = HollowKillerRecharge;
            var center = player.VisualCell;
            var radiusSquared = HollowKillerRadius * HollowKillerRadius;
            _hollows.RemoveAll(hollow =>
                PerkDistanceSquared(hollow.VisualCell, center) <= radiusSquared);
            _sentries.RemoveAll(sentry =>
                PerkDistanceSquared(sentry.Cell, center) <= radiusSquared);
            _sentryProjectiles.RemoveAll(projectile =>
                PerkDistanceSquared(projectile.Position, center) <= radiusSquared);
            return;
        }
        if (player.HasPerk(PerkId.GhostForm))
        {
            if (player.GhostFormTimer > 0 || player.GhostFormCooldown > 0) return;
            player.GhostFormTimer = GhostFormDuration;
            player.GhostFormCooldown = GhostFormRecharge;
            return;
        }
        if (!player.HasPerk(PerkId.Camouflage) ||
            player.CamouflageTimer > 0 || player.CamouflageCooldown > 0)
            return;
        player.CamouflageTimer = CamouflageDuration;
        player.CamouflageCooldown = CamouflageRecharge;
    }

    private void DropOnlineCargo(OnlineRemotePlayer player)
    {
        foreach (var cargo in _cargoItems.Where(item =>
                     item.CarrierPlayerId == player.PlayerId && !item.Delivered))
        {
            cargo.CarrierPlayerId = null;
            cargo.Cell = player.Cell;
        }
    }

    private void DamageOnlinePlayer(OnlineRemotePlayer player, bool causedByHollow = true)
    {
        if (player.Invulnerability > 0 || player.Defeated || player.InShop ||
            !player.Connected) return;
        if (player.ShopProtectionCharges > 0)
        {
            player.ShopProtectionCharges--;
            player.Invulnerability = 1.25f;
            player.ShopTransactionRevision++;
            player.ShopMessage = "AEGIS FUSE SPENT / DAMAGE NULL";
            player.ShopCue = (int)AudioCue.Confirm;
            return;
        }
        DropOnlineCargo(player);
        player.Damage++;
        player.TotalDamageSustained++;
        player.LastDamageWasHollow = causedByHollow;
        if (player.ShopRepairReserve > 0 && player.Damage > 0)
        {
            player.ShopRepairReserve--;
            player.Damage--;
            player.ShopTransactionRevision++;
            player.ShopMessage = "BANKED REPAIR DEPLOYED / FRAME RESTORED";
            player.ShopCue = (int)AudioCue.Confirm;
        }
        player.Invulnerability = 2.4f;
        if (player.Damage >= player.MaximumHealth)
        {
            player.Defeated = true;
            player.MoveProgress = 1;
            player.Traversal.Clear();
            if (!HasAnyLivingOnlinePlayer()) EnterFailure();
            return;
        }
        TeleportOnlinePlayerToSafety(player);
    }

    private void TeleportOnlinePlayerToSafety(OnlineRemotePlayer player)
    {
        if (_maze is null) return;
        var candidates = new List<Point>();
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
        {
            var cell = new Point(x, y);
            if (cell == _exitCell || cell == player.Cell || _maze.GetRoomAt(cell) is not null)
                continue;
            if (IsSurvivorBlockingCell(cell) ||
                _hollows.Any(hollow => Manhattan(hollow.Cell, cell) < 7) ||
                _sentries.Any(sentry => Manhattan(sentry.Cell, cell) < 7))
                continue;
            candidates.Add(cell);
        }
        if (candidates.Count == 0) return;
        var destination = candidates[_random.Next(candidates.Count)];
        player.Cell = destination;
        player.PreviousCell = destination;
        player.VisualCell = destination;
        player.PreviousVisualCell = destination;
        player.MoveFrom = destination;
        player.MoveTo = destination;
        player.MoveProgress = 1;
        player.Traversal.Clear();
        player.TraversalUsedGhostForm = false;
    }

    private bool HasAnyLivingOnlinePlayer() =>
        !_onlineLocalDefeated ||
        _onlinePlayers.Values.Any(player =>
            player.Connected && !player.Defeated && !player.Extracted);

    private void HandleOnlineLocalDefeat()
    {
        _onlineLocalDefeated = true;
        _failurePending = false;
        _hitEffect = 0;
        _invulnerability = float.MaxValue;
        _pendingWin = false;
        if (!HasAnyLivingOnlinePlayer()) EnterFailure();
    }

    private void CheckOnlineRemoteHollowCollisions()
    {
        if (!IsOnlineSimulationHost) return;
        foreach (var player in _onlinePlayers.Values)
        {
            if (!OnlinePlayerCanBeTargeted(player) || player.Invulnerability > 0) continue;
            foreach (var hollow in _hollows)
            {
                var separation = SweptSeparationSquared(
                    player.PreviousVisualCell, player.VisualCell,
                    hollow.PreviousVisualCell, hollow.VisualCell);
                if (separation > .27f) continue;
                DamageOnlinePlayer(player);
                break;
            }
        }
    }

    private bool CheckOnlineRemoteSentryContact()
    {
        if (!IsOnlineSimulationHost) return false;
        foreach (var player in _onlinePlayers.Values)
        {
            if (!OnlinePlayerCanBeTargeted(player) || player.Invulnerability > 0) continue;
            if (!_sentries.Any(sentry => sentry.Phase != SentryPhase.Buried &&
                    DistanceSquared(player.VisualCell, sentry.Cell) <= .24f))
                continue;
            DamageOnlinePlayer(player, causedByHollow: false);
            return true;
        }
        return false;
    }

    private bool TryHitOnlinePlayerWithProjectile(SentryProjectile projectile)
    {
        if (!IsOnlineSimulationHost) return false;
        foreach (var player in _onlinePlayers.Values)
        {
            if (!OnlinePlayerCanBeTargeted(player) || player.Invulnerability > 0) continue;
            var separation = SweptSeparationSquared(
                player.PreviousVisualCell, player.VisualCell,
                projectile.PreviousPosition, projectile.Position);
            if (separation > .075f) continue;
            DamageOnlinePlayer(player, causedByHollow: false);
            return true;
        }
        return false;
    }

    private bool OnlinePlayerCanBeTargeted(OnlineRemotePlayer player) =>
        player.Connected && !player.Defeated && !player.Extracted && !player.InShop;

    private bool TryFindOnlineHollowTarget(
        Hollow hollow,
        out string playerId,
        out PointF visualCell,
        out Point logicalCell)
    {
        playerId = string.Empty;
        visualCell = PointF.Empty;
        logicalCell = Point.Empty;
        var bestDistance = float.MaxValue;

        if (!_onlineLocalDefeated && !IsOnlineLocalPlayerProtected &&
            !IsPlayerInvisibleToEnemies &&
            CanHollowSeeFrom(hollow, hollow.VisualCell, _visualCell, hollow.HasSight))
        {
            bestDistance = PerkDistanceSquared(hollow.VisualCell, _visualCell);
            playerId = _onlinePlayerId ?? string.Empty;
            visualCell = _visualCell;
            logicalCell = _playerCell;
        }

        foreach (var player in _onlinePlayers.Values)
        {
            if (!OnlinePlayerCanBeTargeted(player) || player.CamouflageTimer > 0) continue;
            if (!CanHollowSeeFrom(hollow, hollow.VisualCell, player.VisualCell,
                    hollow.HasSight && hollow.TargetPlayerId == player.PlayerId))
                continue;
            var distance = PerkDistanceSquared(hollow.VisualCell, player.VisualCell);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            playerId = player.PlayerId;
            visualCell = player.VisualCell;
            logicalCell = player.Cell;
        }
        return playerId.Length > 0;
    }

    private Point OnlineHollowTargetCell(Hollow hollow)
    {
        if (!IsOnlineSimulationHost || string.IsNullOrWhiteSpace(hollow.TargetPlayerId))
            return _playerCell;
        if (hollow.TargetPlayerId == _onlinePlayerId) return _playerCell;
        return _onlinePlayers.TryGetValue(hollow.TargetPlayerId, out var player)
            ? player.Cell
            : hollow.LastSeen;
    }

    private bool TryFindOnlineSentryTarget(
        Sentry sentry,
        out string playerId,
        out PointF visualCell)
    {
        playerId = string.Empty;
        visualCell = PointF.Empty;
        var bestDistance = float.MaxValue;
        if (!_onlineLocalDefeated && !IsOnlineLocalPlayerProtected &&
            !IsPlayerInvisibleToEnemies && CanSentrySeePosition(sentry, _visualCell))
        {
            bestDistance = PerkDistanceSquared(sentry.Cell, _visualCell);
            playerId = _onlinePlayerId ?? string.Empty;
            visualCell = _visualCell;
        }
        foreach (var player in _onlinePlayers.Values)
        {
            if (!OnlinePlayerCanBeTargeted(player) || player.CamouflageTimer > 0 ||
                !CanSentrySeePosition(sentry, player.VisualCell))
                continue;
            var distance = PerkDistanceSquared(sentry.Cell, player.VisualCell);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            playerId = player.PlayerId;
            visualCell = player.VisualCell;
        }
        return playerId.Length > 0;
    }

    private PointF OnlineSentryTargetVisual(Sentry sentry)
    {
        if (!IsOnlineSimulationHost || sentry.TargetPlayerId == _onlinePlayerId)
            return _visualCell;
        return sentry.TargetPlayerId is not null &&
               _onlinePlayers.TryGetValue(sentry.TargetPlayerId, out var player)
            ? player.VisualCell
            : _visualCell;
    }

    private void TriggerOnlineDetectionWarning(string? playerId)
    {
        if (playerId == _onlinePlayerId) TriggerDetectionWarning();
    }

    private OnlineWorldSnapshot BuildOnlineSnapshot()
    {
        var elapsed = _mode == ScreenMode.Won
            ? _wonTime
            : DateTime.Now - _startedAt;
        var players = new List<OnlinePlayerSnapshot>
        {
            BuildLocalOnlinePlayerSnapshot()
        };
        players.AddRange(_onlinePlayers.Values
            .OrderBy(player => player.JoinOrder)
            .Select(BuildRemoteOnlinePlayerSnapshot));

        return new OnlineWorldSnapshot
        {
            Tick = _onlineSimulationTick,
            WorldRevision = ++_onlineWorldRevision,
            AuthorityRevision = _onlineAuthorityRevision,
            HostPlayerId = _onlinePlayerId ?? string.Empty,
            Seed = _onlineRunSeed,
            RandomState = _random.State.ToString("X16", CultureInfo.InvariantCulture),
            Level = _level,
            ElapsedMilliseconds = Math.Max(0, (long)elapsed.TotalMilliseconds),
            RunCompleted = _mode == ScreenMode.Won,
            RunFailed = _mode == ScreenMode.Failed,
            FieldCredits = _fieldCredits,
            Players = players.ToArray(),
            Hollows = _hollows.Select(hollow => new OnlineHollowSnapshot
            {
                Type = (int)hollow.Type,
                State = (int)hollow.State,
                CellX = hollow.Cell.X,
                CellY = hollow.Cell.Y,
                TargetX = hollow.TargetCell.X,
                TargetY = hollow.TargetCell.Y,
                PreviousX = hollow.PreviousCell.X,
                PreviousY = hollow.PreviousCell.Y,
                LastSeenX = hollow.LastSeen.X,
                LastSeenY = hollow.LastSeen.Y,
                VisualX = hollow.VisualCell.X,
                VisualY = hollow.VisualCell.Y,
                PreviousVisualX = hollow.PreviousVisualCell.X,
                PreviousVisualY = hollow.PreviousVisualCell.Y,
                MoveFromX = hollow.MoveFrom.X,
                MoveFromY = hollow.MoveFrom.Y,
                MoveToX = hollow.MoveTo.X,
                MoveToY = hollow.MoveTo.Y,
                MoveProgress = hollow.MoveProgress,
                Cooldown = hollow.Cooldown,
                SenseCooldown = hollow.SenseCooldown,
                SearchTimer = hollow.SearchTimer,
                FacingAngle = hollow.FacingAngle,
                DesiredFacingAngle = hollow.DesiredFacingAngle,
                LookCooldown = hollow.LookCooldown,
                AnimationPhase = hollow.AnimationPhase,
                AggressionScale = hollow.AggressionScale,
                HasSight = hollow.HasSight,
                TargetPlayerId = hollow.TargetPlayerId
            }).ToArray(),
            Sentries = _sentries.Select(sentry => new OnlineSentrySnapshot
            {
                CellX = sentry.Cell.X,
                CellY = sentry.Cell.Y,
                PreviousX = sentry.PreviousCell.X,
                PreviousY = sentry.PreviousCell.Y,
                FacingAngle = sentry.FacingAngle,
                RotationDirection = sentry.RotationDirection,
                AnimationPhase = sentry.AnimationPhase,
                UnsuccessfulScanTime = sentry.UnsuccessfulScanTime,
                RelocationThreshold = sentry.RelocationThreshold,
                FireCooldown = sentry.FireCooldown,
                MuzzleFlash = sentry.MuzzleFlash,
                HasSight = sentry.HasSight,
                Phase = (int)sentry.Phase,
                PhaseTimer = sentry.PhaseTimer,
                TargetPlayerId = sentry.TargetPlayerId
            }).ToArray(),
            Projectiles = _sentryProjectiles.Select(projectile =>
                new OnlineProjectileSnapshot
                {
                    Serial = projectile.Serial,
                    X = projectile.Position.X,
                    Y = projectile.Position.Y,
                    PreviousX = projectile.PreviousPosition.X,
                    PreviousY = projectile.PreviousPosition.Y,
                    VelocityX = projectile.Velocity.X,
                    VelocityY = projectile.Velocity.Y,
                    Lifetime = projectile.Lifetime
                }).ToArray(),
            Cargo = _cargoItems.Select((cargo, index) => new OnlineCargoSnapshot
            {
                Index = index,
                CellX = cargo.Cell.X,
                CellY = cargo.Cell.Y,
                Carried = cargo.Carried,
                Delivered = cargo.Delivered,
                CarrierPlayerId = cargo.CarrierPlayerId
            }).ToArray(),
            Credits = _creditPickups.Select((credit, index) => new OnlineCreditSnapshot
            {
                Index = index,
                CellX = credit.Cell.X,
                CellY = credit.Cell.Y,
                VisualX = credit.VisualCell.X,
                VisualY = credit.VisualCell.Y,
                Collected = credit.Collected,
                MagnetMoving = credit.MagnetMoving,
                TargetX = credit.MagnetTargetCell.X,
                TargetY = credit.MagnetTargetCell.Y,
                MagnetProgress = credit.MagnetProgress
            }).ToArray(),
            Salvage = _roomSalvage.Select((salvage, index) =>
                new OnlineSalvageSnapshot
                {
                    Index = index,
                    Collected = salvage.Collected,
                    Sold = salvage.Sold
                }).ToArray(),
            CircuitSwitches = _circuitSwitches.Select(circuitSwitch =>
                new OnlineCircuitSnapshot
                {
                    Number = circuitSwitch.Number,
                    Activated = circuitSwitch.Activated
                }).ToArray(),
            RevealedRoomIds = _revealedRoomIds.OrderBy(id => id).ToArray(),
            Doors = _roomDoorOpenProgress.Select(entry => new OnlineDoorSnapshot
            {
                RoomId = entry.Key,
                Progress = entry.Value
            }).ToArray(),
            SurvivorStage = _survivorObjective is null ? -1 : (int)_survivorObjective.Stage,
            SurvivorEscortPlayerId = _survivorObjective?.EscortPlayerId,
            ShopStock = _shopStock.Select(item => item.Stock).ToArray()
        };
    }

    private OnlinePlayerSnapshot BuildLocalOnlinePlayerSnapshot()
    {
        var lobbyPlayer = _onlineLobby?.Players.FirstOrDefault(player =>
            player.PlayerId == _onlinePlayerId);
        var warning = _hollows.Any(hollow =>
                          hollow.HasSight && hollow.TargetPlayerId == _onlinePlayerId) ||
                      _sentries.Any(sentry =>
                          sentry.HasSight && sentry.TargetPlayerId == _onlinePlayerId);
        return new OnlinePlayerSnapshot
        {
            PlayerId = _onlinePlayerId ?? string.Empty,
            Username = _onlineUsername ?? "DRONE",
            JoinOrder = lobbyPlayer?.JoinOrder ?? 0,
            Connected = true,
            InShop = _mode == ScreenMode.Shop,
            Defeated = _onlineLocalDefeated,
            Extracted = _mode == ScreenMode.Won,
            Invisible = IsCamouflaged,
            Warning = warning,
            Drone = (int)_drone,
            CoreArgb = _playerColor.ToArgb(),
            FrameArgb = _playerFrameColor.ToArgb(),
            CellX = _playerCell.X,
            CellY = _playerCell.Y,
            VisualX = _visualCell.X,
            VisualY = _visualCell.Y,
            MoveFromX = _moveFrom.X,
            MoveFromY = _moveFrom.Y,
            MoveToX = _moveTo.X,
            MoveToY = _moveTo.Y,
            MoveProgress = _moveProgress,
            Bank = _droneBank,
            Pitch = _dronePitch,
            Damage = _damageTaken,
            TotalDamageSustained = _totalDamageSustained,
            MaximumHealth = GetMaximumHealth(),
            Invulnerability = _invulnerability,
            LastInputSequence = _onlineLastAcceptedLocalSequence,
            EquippedPerks = _settings.Progression.EquippedPerks
                .Select(perk => (int)perk).ToArray(),
            CamouflageTimer = _camouflageTimer,
            CamouflageCooldown = _camouflageCooldown,
            GhostFormTimer = _ghostFormTimer,
            GhostFormCooldown = _ghostFormCooldown,
            HollowKillerCooldown = _hollowKillerCooldown,
            LastDamageWasHollow = true,
            AccountCredits = _settings.TotalCredits,
            ShopRepairReserve = _shopRepairReserve,
            ShopProtectionCharges = _shopProtectionCharges,
            ShopTransactionRevision = _onlineLastAppliedShopRevision
        };
    }

    private OnlinePlayerSnapshot BuildRemoteOnlinePlayerSnapshot(OnlineRemotePlayer player)
    {
        var warning = _hollows.Any(hollow =>
                          hollow.HasSight && hollow.TargetPlayerId == player.PlayerId) ||
                      _sentries.Any(sentry =>
                          sentry.HasSight && sentry.TargetPlayerId == player.PlayerId);
        return new OnlinePlayerSnapshot
        {
            PlayerId = player.PlayerId,
            Username = player.Username,
            JoinOrder = player.JoinOrder,
            Connected = player.Connected,
            InShop = player.InShop,
            Defeated = player.Defeated,
            Extracted = player.Extracted,
            Invisible = player.CamouflageTimer > 0,
            Warning = warning,
            Drone = (int)player.Drone,
            CoreArgb = player.CoreColor.ToArgb(),
            FrameArgb = player.FrameColor.ToArgb(),
            CellX = player.Cell.X,
            CellY = player.Cell.Y,
            VisualX = player.VisualCell.X,
            VisualY = player.VisualCell.Y,
            MoveFromX = player.MoveFrom.X,
            MoveFromY = player.MoveFrom.Y,
            MoveToX = player.MoveTo.X,
            MoveToY = player.MoveTo.Y,
            MoveProgress = player.MoveProgress,
            Bank = player.Bank,
            Pitch = player.Pitch,
            Damage = player.Damage,
            TotalDamageSustained = player.TotalDamageSustained,
            MaximumHealth = player.MaximumHealth,
            Invulnerability = player.Invulnerability,
            LastInputSequence = player.LastInputSequence,
            EquippedPerks = player.EquippedPerks.Select(perk => (int)perk).ToArray(),
            CamouflageTimer = player.CamouflageTimer,
            CamouflageCooldown = player.CamouflageCooldown,
            GhostFormTimer = player.GhostFormTimer,
            GhostFormCooldown = player.GhostFormCooldown,
            HollowKillerCooldown = player.HollowKillerCooldown,
            LastDamageWasHollow = player.LastDamageWasHollow,
            AccountCredits = player.AccountCredits,
            ShopRepairReserve = player.ShopRepairReserve,
            ShopProtectionCharges = player.ShopProtectionCharges,
            ShopTransactionRevision = player.ShopTransactionRevision,
            ShopMessage = player.ShopMessage,
            ShopCue = player.ShopCue
        };
    }

    private void ApplyOnlineSnapshot(OnlineWorldSnapshot snapshot)
    {
        if (snapshot.ProtocolVersion != OnlineGameplayProtocolVersion ||
            snapshot.Seed != _onlineRunSeed ||
            snapshot.WorldRevision <= _onlineWorldRevision ||
            snapshot.HostPlayerId != _onlineLobby?.HostPlayerId)
            return;
        if (_maze is null || _mode == ScreenMode.Loading)
        {
            _onlinePendingSnapshot = snapshot;
            return;
        }

        _onlineWorldRevision = snapshot.WorldRevision;
        _onlineSimulationTick = Math.Max(_onlineSimulationTick, snapshot.Tick);
        _onlineAuthorityRevision = Math.Max(
            _onlineAuthorityRevision, snapshot.AuthorityRevision);
        _fieldCredits = snapshot.FieldCredits;
        _level = Math.Max(1, snapshot.Level);
        if (!snapshot.RunCompleted && !snapshot.RunFailed)
            _startedAt = DateTime.Now -
                         TimeSpan.FromMilliseconds(Math.Max(0, snapshot.ElapsedMilliseconds));
        if (ulong.TryParse(snapshot.RandomState, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var randomState))
            _random.State = randomState;

        var activePlayerIds = (_onlineLobby?.Players ?? [])
            .Select(player => player.PlayerId)
            .ToHashSet(StringComparer.Ordinal);
        if (_onlinePlayerId is { } localPlayerId)
            activePlayerIds.Add(localPlayerId);
        foreach (var playerSnapshot in snapshot.Players)
        {
            if (playerSnapshot.PlayerId == _onlinePlayerId)
                ApplyLocalOnlinePlayerSnapshot(playerSnapshot);
            else if (activePlayerIds.Contains(playerSnapshot.PlayerId))
                ApplyRemoteOnlinePlayerSnapshot(playerSnapshot);
        }
        foreach (var departedId in _onlinePlayers.Keys
                     .Where(id => !activePlayerIds.Contains(id)).ToArray())
            _onlinePlayers.Remove(departedId);
        ApplyOnlineEnemySnapshot(snapshot);
        ApplyOnlineObjectiveSnapshot(snapshot);
        SanitizeOnlineSnapshotMembership(snapshot, activePlayerIds);

        if (snapshot.RunCompleted && !_onlineCompletionApplied)
            ApplyOnlineCompletion(snapshot.ElapsedMilliseconds);
        else if (snapshot.RunFailed && !_onlineFailureApplied)
            ApplyOnlineFailure();
    }

    private void ApplyLocalOnlinePlayerSnapshot(OnlinePlayerSnapshot snapshot)
    {
        var previousTotalDamage = _totalDamageSustained;
        _onlineLocalDefeated = snapshot.Defeated;
        _damageTaken = Math.Clamp(snapshot.Damage, 0, Math.Max(3, snapshot.MaximumHealth));
        _totalDamageSustained = Math.Max(0, snapshot.TotalDamageSustained);
        _invulnerability = Math.Max(0, snapshot.Invulnerability);
        _camouflageTimer = Math.Max(0, snapshot.CamouflageTimer);
        _camouflageCooldown = Math.Max(0, snapshot.CamouflageCooldown);
        _ghostFormTimer = Math.Max(0, snapshot.GhostFormTimer);
        _ghostFormCooldown = Math.Max(0, snapshot.GhostFormCooldown);
        _hollowKillerCooldown = Math.Max(0, snapshot.HollowKillerCooldown);
        if (snapshot.ShopTransactionRevision > _onlineLastAppliedShopRevision)
        {
            _onlineLastAppliedShopRevision = snapshot.ShopTransactionRevision;
            _settings.TotalCredits = Math.Max(0, snapshot.AccountCredits);
            _shopRepairReserve = Math.Max(0, snapshot.ShopRepairReserve);
            _shopProtectionCharges = Math.Max(0, snapshot.ShopProtectionCharges);
            if (_mode == ScreenMode.Shop && !string.IsNullOrWhiteSpace(snapshot.ShopMessage))
                StartShopDialogue(snapshot.ShopMessage);
            else if (!string.IsNullOrWhiteSpace(snapshot.ShopMessage))
            {
                _missionNotice = snapshot.ShopMessage;
                _missionNoticeTimer = 2.2f;
                _impactCell = new Point(
                    (int)MathF.Round(_visualCell.X),
                    (int)MathF.Round(_visualCell.Y));
                _impactPulse = 1;
            }
            if (Enum.IsDefined(typeof(AudioCue), snapshot.ShopCue))
            {
                var cue = (AudioCue)snapshot.ShopCue;
                if (cue is AudioCue.Confirm or AudioCue.Select or AudioCue.Collect)
                    _audio.Play(cue);
            }
            QueueSettingsSave();
        }
        if (_totalDamageSustained > previousTotalDamage)
        {
            RecordHitForAchievements(snapshot.LastDamageWasHollow);
            _hitEffect = 1.16f;
            _teleportDone = true;
            _failurePending = false;
            _pendingWin = false;
            _impactPulse = 1;
            CancelPendingTraversal();
        }

        // Do not rewind a move which has been predicted locally but has not yet
        // appeared in the authority's acknowledgement.
        if (snapshot.LastInputSequence >= _onlineLastLocalMovementSequence)
        {
            _playerPreviousCell = _playerCell;
            _playerCell = new Point(snapshot.CellX, snapshot.CellY);
            _visualCell = new PointF(snapshot.VisualX, snapshot.VisualY);
            _previousVisualCell = _visualCell;
            _moveFrom = new PointF(snapshot.MoveFromX, snapshot.MoveFromY);
            _moveTo = new PointF(snapshot.MoveToX, snapshot.MoveToY);
            _moveProgress = Math.Clamp(snapshot.MoveProgress, 0, 1);
            _droneBank = Math.Clamp(snapshot.Bank, -1, 1);
            _dronePitch = Math.Clamp(snapshot.Pitch, -1, 1);
            CancelPendingTraversal();
            _movementArrivalHandled = true;
        }

        if (snapshot.Warning && !_onlineLocalWarningActive)
        {
            RecordDetectionForAchievements();
            _warningFlash = .82f;
            if (_warningSoundCooldown <= 0)
            {
                _audio.Play(AudioCue.Caught);
                _warningSoundCooldown = .48f;
            }
        }
        _onlineLocalWarningActive = snapshot.Warning;

        if (snapshot.InShop && _mode == ScreenMode.Playing)
            EnterOnlineShopView();
        else if (!snapshot.InShop && _mode == ScreenMode.Shop)
            LeaveOnlineShopView();
    }

    private void ApplyRemoteOnlinePlayerSnapshot(OnlinePlayerSnapshot snapshot)
    {
        if (!_onlinePlayers.TryGetValue(snapshot.PlayerId, out var player))
        {
            var lobbyPlayer = _onlineLobby?.Players.FirstOrDefault(item =>
                item.PlayerId == snapshot.PlayerId) ??
                new OnlineLobbyPlayer(snapshot.PlayerId, snapshot.Username,
                    snapshot.JoinOrder, snapshot.Connected);
            player = EnsureOnlinePlayer(lobbyPlayer);
        }
        player.Username = snapshot.Username;
        player.JoinOrder = snapshot.JoinOrder;
        player.Connected = _onlineLobby?.Players.FirstOrDefault(item =>
            item.PlayerId == snapshot.PlayerId)?.Connected ?? snapshot.Connected;
        player.InShop = snapshot.InShop;
        player.Defeated = snapshot.Defeated;
        player.Extracted = snapshot.Extracted;
        player.Drone = (DroneModel)Math.Clamp(snapshot.Drone, 0, 4);
        player.CoreColor = Color.FromArgb(snapshot.CoreArgb);
        player.FrameColor = Color.FromArgb(snapshot.FrameArgb);
        player.PreviousCell = player.Cell;
        player.Cell = new Point(snapshot.CellX, snapshot.CellY);
        player.PreviousVisualCell = player.VisualCell;
        player.VisualCell = new PointF(snapshot.VisualX, snapshot.VisualY);
        player.MoveFrom = new PointF(snapshot.MoveFromX, snapshot.MoveFromY);
        player.MoveTo = new PointF(snapshot.MoveToX, snapshot.MoveToY);
        player.MoveProgress = Math.Clamp(snapshot.MoveProgress, 0, 1);
        player.Bank = Math.Clamp(snapshot.Bank, -1, 1);
        player.Pitch = Math.Clamp(snapshot.Pitch, -1, 1);
        player.Damage = Math.Max(0, snapshot.Damage);
        player.TotalDamageSustained = Math.Max(0, snapshot.TotalDamageSustained);
        player.MaximumHealth = Math.Clamp(snapshot.MaximumHealth, 3, 5);
        player.Invulnerability = Math.Max(0, snapshot.Invulnerability);
        player.LastInputSequence = snapshot.LastInputSequence;
        player.LastReceivedInputSequence = Math.Max(
            player.LastReceivedInputSequence, snapshot.LastInputSequence);
        player.CamouflageTimer = Math.Max(0, snapshot.CamouflageTimer);
        player.CamouflageCooldown = Math.Max(0, snapshot.CamouflageCooldown);
        player.GhostFormTimer = Math.Max(0, snapshot.GhostFormTimer);
        player.GhostFormCooldown = Math.Max(0, snapshot.GhostFormCooldown);
        player.HollowKillerCooldown = Math.Max(0, snapshot.HollowKillerCooldown);
        player.LastDamageWasHollow = snapshot.LastDamageWasHollow;
        player.AccountCredits = Math.Max(0, snapshot.AccountCredits);
        player.ShopRepairReserve = Math.Max(0, snapshot.ShopRepairReserve);
        player.ShopProtectionCharges = Math.Max(0, snapshot.ShopProtectionCharges);
        player.ShopTransactionRevision = Math.Max(
            player.ShopTransactionRevision, snapshot.ShopTransactionRevision);
        player.ShopMessage = snapshot.ShopMessage;
        player.ShopCue = snapshot.ShopCue;
        player.AppearanceReady = true;
        player.EquippedPerks.Clear();
        foreach (var value in snapshot.EquippedPerks)
            if (Enum.IsDefined(typeof(PerkId), value))
                player.EquippedPerks.Add((PerkId)value);
    }

    private void ApplyOnlineEnemySnapshot(OnlineWorldSnapshot snapshot)
    {
        if (_hollows.Count != snapshot.Hollows.Length ||
            _hollows.Where((hollow, index) =>
                    (int)hollow.Type != snapshot.Hollows[index].Type)
                .Any())
        {
            _hollows.Clear();
            foreach (var value in snapshot.Hollows)
            {
                _hollows.Add(new Hollow
                {
                    Type = (HollowType)Math.Clamp(value.Type, 0, 2),
                    AnimationPhase = value.AnimationPhase,
                    AggressionScale = Math.Clamp(value.AggressionScale, .5f, 3f)
                });
            }
        }
        for (var index = 0; index < _hollows.Count; index++)
        {
            var hollow = _hollows[index];
            var value = snapshot.Hollows[index];
            hollow.State = (HollowState)Math.Clamp(value.State, 0, 2);
            hollow.Cell = new Point(value.CellX, value.CellY);
            hollow.TargetCell = new Point(value.TargetX, value.TargetY);
            hollow.PreviousCell = new Point(value.PreviousX, value.PreviousY);
            hollow.LastSeen = new Point(value.LastSeenX, value.LastSeenY);
            hollow.LastSeenVisual = new PointF(value.LastSeenX, value.LastSeenY);
            hollow.VisualCell = new PointF(value.VisualX, value.VisualY);
            hollow.PreviousVisualCell =
                new PointF(value.PreviousVisualX, value.PreviousVisualY);
            hollow.MoveFrom = new PointF(value.MoveFromX, value.MoveFromY);
            hollow.MoveTo = new PointF(value.MoveToX, value.MoveToY);
            hollow.MoveProgress = Math.Clamp(value.MoveProgress, 0, 1);
            hollow.Cooldown = value.Cooldown;
            hollow.SenseCooldown = value.SenseCooldown;
            hollow.SearchTimer = value.SearchTimer;
            hollow.FacingAngle = value.FacingAngle;
            hollow.DesiredFacingAngle = value.DesiredFacingAngle;
            hollow.LookCooldown = value.LookCooldown;
            hollow.HasSight = value.HasSight;
            hollow.TargetPlayerId = value.TargetPlayerId;
        }

        if (_sentries.Count != snapshot.Sentries.Length)
        {
            _sentries.Clear();
            foreach (var value in snapshot.Sentries)
            {
                _sentries.Add(new Sentry
                {
                    RotationDirection = value.RotationDirection < 0 ? -1 : 1,
                    AnimationPhase = value.AnimationPhase
                });
            }
        }
        for (var index = 0; index < _sentries.Count; index++)
        {
            var sentry = _sentries[index];
            var value = snapshot.Sentries[index];
            sentry.Cell = new Point(value.CellX, value.CellY);
            sentry.PreviousCell = new Point(value.PreviousX, value.PreviousY);
            sentry.FacingAngle = value.FacingAngle;
            sentry.UnsuccessfulScanTime = value.UnsuccessfulScanTime;
            sentry.RelocationThreshold = value.RelocationThreshold;
            sentry.FireCooldown = value.FireCooldown;
            sentry.MuzzleFlash = value.MuzzleFlash;
            sentry.HasSight = value.HasSight;
            sentry.Phase = (SentryPhase)Math.Clamp(value.Phase, 0, 3);
            sentry.PhaseTimer = value.PhaseTimer;
            sentry.TargetPlayerId = value.TargetPlayerId;
        }

        _sentryProjectiles.Clear();
        foreach (var value in snapshot.Projectiles)
        {
            _sentryProjectiles.Add(new SentryProjectile
            {
                Serial = value.Serial,
                Position = new PointF(value.X, value.Y),
                PreviousPosition = new PointF(value.PreviousX, value.PreviousY),
                Velocity = new PointF(value.VelocityX, value.VelocityY),
                Lifetime = value.Lifetime
            });
        }
        _sentryProjectileSerial = Math.Max(
            _sentryProjectileSerial,
            snapshot.Projectiles.Select(item => item.Serial).DefaultIfEmpty().Max());
    }

    private void ApplyOnlineObjectiveSnapshot(OnlineWorldSnapshot snapshot)
    {
        foreach (var value in snapshot.Cargo)
        {
            if (value.Index < 0 || value.Index >= _cargoItems.Count) continue;
            var cargo = _cargoItems[value.Index];
            cargo.Cell = new Point(value.CellX, value.CellY);
            cargo.Carried = value.Carried;
            cargo.Delivered = value.Delivered;
            cargo.CarrierPlayerId = value.CarrierPlayerId;
        }
        var collectedCredit = false;
        foreach (var value in snapshot.Credits)
        {
            if (value.Index < 0 || value.Index >= _creditPickups.Count) continue;
            var credit = _creditPickups[value.Index];
            collectedCredit |= !credit.Collected && value.Collected;
            credit.Cell = new Point(value.CellX, value.CellY);
            credit.VisualCell = new PointF(value.VisualX, value.VisualY);
            credit.Collected = value.Collected;
            credit.MagnetMoving = value.MagnetMoving;
            credit.MagnetTargetCell = new Point(value.TargetX, value.TargetY);
            credit.MagnetProgress = Math.Clamp(value.MagnetProgress, 0, 1);
        }
        if (collectedCredit) _audio.Play(AudioCue.Collect);
        foreach (var value in snapshot.Salvage)
        {
            if (value.Index < 0 || value.Index >= _roomSalvage.Count) continue;
            _roomSalvage[value.Index].Collected = value.Collected;
            _roomSalvage[value.Index].Sold = value.Sold;
        }
        foreach (var value in snapshot.CircuitSwitches)
        {
            var circuitSwitch = _circuitSwitches.FirstOrDefault(item =>
                item.Number == value.Number);
            if (circuitSwitch is not null) circuitSwitch.Activated = value.Activated;
        }
        _revealedRoomIds.Clear();
        foreach (var roomId in snapshot.RevealedRoomIds) _revealedRoomIds.Add(roomId);
        _roomDoorOpenProgress.Clear();
        foreach (var door in snapshot.Doors)
            _roomDoorOpenProgress[door.RoomId] = Math.Clamp(door.Progress, 0, 1);
        if (_survivorObjective is not null && snapshot.SurvivorStage >= 0)
        {
            _survivorObjective.Stage = (SurvivorObjectiveStage)Math.Clamp(
                snapshot.SurvivorStage, 0, 3);
            _survivorObjective.EscortPlayerId = snapshot.SurvivorEscortPlayerId;
            if (_onlineLobby is { } lobby)
                ReleaseUnavailableOnlineEscort(lobby);
        }
        for (var index = 0;
             index < Math.Min(_shopStock.Count, snapshot.ShopStock.Length);
             index++)
            _shopStock[index].Stock = Math.Max(0, snapshot.ShopStock[index]);
    }

    private void SanitizeOnlineSnapshotMembership(
        OnlineWorldSnapshot snapshot,
        IReadOnlySet<string> activePlayerIds)
    {
        var departedCells = snapshot.Players
            .Where(player => !activePlayerIds.Contains(player.PlayerId))
            .GroupBy(player => player.PlayerId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new Point(group.Last().CellX, group.Last().CellY),
                StringComparer.Ordinal);
        foreach (var cargo in _cargoItems.Where(item =>
                     item.CarrierPlayerId is { } carrierId &&
                     !activePlayerIds.Contains(carrierId)))
        {
            if (cargo.CarrierPlayerId is { } departedId &&
                departedCells.TryGetValue(departedId, out var dropCell))
                cargo.Cell = dropCell;
            cargo.Carried = false;
            cargo.CarrierPlayerId = null;
        }

        if (_survivorObjective is
            {
                Stage: SurvivorObjectiveStage.Escorting,
                EscortPlayerId: { } escortId
            } objective &&
            !activePlayerIds.Contains(escortId))
        {
            objective.Stage = SurvivorObjectiveStage.Searching;
            objective.EscortPlayerId = null;
        }
    }

    private void EnterOnlineShopView()
    {
        if (_mode != ScreenMode.Playing) return;
        CloseMissionDossier(playSound: false);
        ResetMissionDossier();
        _shopEnteredAt = DateTime.Now;
        _shopPage = ShopPage.Commands;
        _shopCommandSelection = 0;
        _shopListSelection = 0;
        _mode = ScreenMode.Shop;
        StartShopDialogue(
            "There you are, little drone.\nThe other signals keep moving beyond my counter-light.");
        ResetHover();
    }

    private void LeaveOnlineShopView()
    {
        if (_mode != ScreenMode.Shop) return;
        _mode = ScreenMode.Playing;
        _shopPage = ShopPage.Commands;
        _shopDialogue = string.Empty;
        _shopDialogueVisible = 0;
        ResetHover();
    }

    private void ApplyOnlineCompletion(long elapsedMilliseconds)
    {
        if (_mode == ScreenMode.Won) return;
        _onlineCompletionApplied = true;
        CloseMissionDossier(playSound: false);
        ResetMissionDossier();
        _wonTime = TimeSpan.FromMilliseconds(Math.Max(0, elapsedMilliseconds));
        _againButton = RectangleF.Empty;
        _menuButton = RectangleF.Empty;
        _mode = ScreenMode.Won;
        _transferPulse = 1;
        _pendingWin = false;
        _audio.StopMusic();
        _audio.Play(AudioCue.MazeClear);
        FinishMission();
        RecordAchievementWin();
        ResetHover();
    }

    private void ApplyOnlineFailure()
    {
        if (_mode == ScreenMode.Failed) return;
        _onlineFailureApplied = true;
        EnterFailure();
    }

    private static bool TryProperty(JsonElement node, string name, out JsonElement value)
    {
        if (node.ValueKind == JsonValueKind.Object &&
            node.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }

    private static string StringProperty(JsonElement node, string name) =>
        TryProperty(node, name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long LongProperty(JsonElement node, string name) =>
        TryProperty(node, name, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0;

    private static int IntProperty(JsonElement node, string name, int fallback) =>
        TryProperty(node, name, out var value) && value.TryGetInt32(out var result)
            ? result
            : fallback;
}
