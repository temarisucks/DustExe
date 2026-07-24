using System.Collections.Concurrent;
using System.Text.Json;

namespace Dust;

internal sealed partial class GameForm
{
    private const int VisibleLobbyRows = 6;
    private static readonly TimeSpan OnlineResponseTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan OnlineReconnectWindow = TimeSpan.FromSeconds(13);
    private static readonly TimeSpan OnlineReconnectAttemptTimeout = TimeSpan.FromSeconds(3);
    private static readonly string[] OnlineSettingLabels =
    [
        "MAP SIZE",
        "MAZE STRICTNESS",
        "HOLLOW AMOUNT",
        "SQUARE",
        "DIAMOND",
        "HEX",
        "SENTRY",
        "DIFFICULTY SCALING"
    ];

    private readonly OnlineClient _onlineClient = new();
    private readonly ConcurrentQueue<Action> _onlineUiQueue = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OnlineMessage>>
        _onlineResponseWaiters = new(StringComparer.Ordinal);
    private readonly RectangleF[] _onlineAccountFields = new RectangleF[2];
    private readonly RectangleF[] _onlineAccountButtons = new RectangleF[3];
    private readonly RectangleF[] _onlineLobbyRows = new RectangleF[VisibleLobbyRows];
    private readonly RectangleF[] _onlineBrowserButtons = new RectangleF[4];
    private readonly RectangleF[] _onlineLobbySettingRows = new RectangleF[8];
    private readonly RectangleF[] _onlineLobbyButtons = new RectangleF[2];

    private CancellationTokenSource? _onlineOperationCancellation;
    private IReadOnlyList<OnlineLobbySummary> _onlineLobbies = Array.Empty<OnlineLobbySummary>();
    private string _onlineAccountUsername = string.Empty;
    private string _onlineAccountPassword = string.Empty;
    private string _onlineServerAddress = GameSettings.DefaultOnlineServerUrl;
    private string _onlineSearch = string.Empty;
    private string _onlineStatus = "ENTER ACCOUNT CREDENTIALS";
    private string? _onlinePlayerId;
    private string? _onlineUsername;
    private string? _onlineResumeToken;
    private OnlineLobbyState? _onlineLobby;
    private int _onlineAccountFocus;
    private int _onlineBrowserFocus;
    private int _onlineLobbySelection;
    private int _onlineLobbyListSelection;
    private int _onlineLobbyListOffset;
    private int _onlineHover = -1;
    private int _onlineOperationSerial;
    private int _onlineConnectionSerial;
    private bool _onlineBusy;
    private bool _onlineExpectedDisconnect;
    private bool _onlineReconnecting;
    private bool _onlineMatchActive;
    private int _onlineShutdownStarted;
    private string? _onlineExpectedAuthenticationRequestId;

    private bool IsOnlineLobbyHost =>
        _onlineLobby is not null &&
        !string.IsNullOrWhiteSpace(_onlinePlayerId) &&
        string.Equals(_onlineLobby.HostPlayerId, _onlinePlayerId, StringComparison.OrdinalIgnoreCase);

    private bool CanEditOnlineLobby =>
        IsOnlineLobbyHost && !_onlineReconnecting && _onlineClient.IsConnected;

    private void InitializeOnlineUi()
    {
        _onlineAccountUsername = _settings.LastOnlineUsername;
        _onlineServerAddress = GameSettings.ResolveOnlineServerUrl();
        _onlineClient.MessageReceived += message =>
            _onlineUiQueue.Enqueue(() => HandleOnlineMessage(message));
        _onlineClient.ConnectionClosed += reason =>
            _onlineUiQueue.Enqueue(() => HandleOnlineConnectionClosed(reason));
    }

    private void ProcessOnlineUiQueue()
    {
        // Keep a malformed or flooded peer from monopolizing the WinForms timer.
        var processed = 0;
        while (processed++ < 128 && _onlineUiQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch
            {
                _onlineBusy = false;
                _onlineStatus = "RESPONSE REJECTED";
            }
        }
    }

    private void BeginOnlineShutdown()
    {
        if (Interlocked.Exchange(ref _onlineShutdownStarted, 1) != 0)
            return;

        _onlineExpectedDisconnect = true;
        _onlineOperationCancellation?.Cancel();
        _onlineOperationCancellation?.Dispose();
        _onlineOperationCancellation = null;
        _onlineExpectedAuthenticationRequestId = null;
        ++_onlineOperationSerial;
        ++_onlineConnectionSerial;
        CancelOnlineResponseWaiters();

        var sendLeave = _onlineLobby is not null && _onlineClient.IsConnected;
        try
        {
            // FormClosing cannot be awaited. Run transport cleanup off the UI
            // context and give the leave frame a short, bounded chance to flush
            // before the process exits.
            Task.Run(() => CloseOnlineSessionAsync(sendLeave))
                .Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // The server's disconnect grace is the final cleanup path.
        }
    }

    private void OpenOnlinePlay()
    {
        ResetHover();
        _onlineExpectedDisconnect = false;
        if (_onlineClient.IsConnected && !string.IsNullOrWhiteSpace(_onlinePlayerId))
        {
            if (_onlineLobby is not null)
            {
                _mode = ScreenMode.LobbyRoom;
                _onlineLobbySelection = IsOnlineLobbyHost ? 0 : 9;
            }
            else
            {
                _mode = ScreenMode.LobbyBrowser;
                _onlineBrowserFocus = 0;
                RequestLobbyList();
            }
            return;
        }

        _mode = ScreenMode.OnlineAccount;
        _onlineAccountUsername = _settings.LastOnlineUsername;
        _onlineServerAddress = GameSettings.ResolveOnlineServerUrl();
        _onlineAccountPassword = string.Empty;
        _onlineAccountFocus = string.IsNullOrWhiteSpace(_onlineAccountUsername) ? 0 : 1;
        _onlineStatus = "ENTER ACCOUNT CREDENTIALS";
    }

    private void HandleOnlineKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (_mode == ScreenMode.OnlineAccount && _onlineAccountFocus < 2)
        {
            if (char.IsControl(e.KeyChar)) return;
            switch (_onlineAccountFocus)
            {
                case 0 when IsUsernameCharacter(e.KeyChar) && _onlineAccountUsername.Length < 20:
                    _onlineAccountUsername += e.KeyChar;
                    e.Handled = true;
                    break;
                case 1 when e.KeyChar is >= ' ' and <= '~' && _onlineAccountPassword.Length < 128:
                    _onlineAccountPassword += e.KeyChar;
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (_mode == ScreenMode.LobbyBrowser && _onlineBrowserFocus == 0 &&
            !char.IsControl(e.KeyChar) && IsSearchCharacter(e.KeyChar) && _onlineSearch.Length < 40)
        {
            _onlineSearch += e.KeyChar;
            e.Handled = true;
        }
    }

    private void HandleOnlineAccountKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            ExitOnlineToTitle();
        }
        else if (e.KeyCode == Keys.Tab)
        {
            var direction = e.Shift ? -1 : 1;
            _onlineAccountFocus = Wrap(_onlineAccountFocus + direction, 5);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode == Keys.Up ||
                 _onlineAccountFocus >= 2 && e.KeyCode == Keys.W)
        {
            _onlineAccountFocus = Wrap(_onlineAccountFocus - 1, 5);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode == Keys.Down ||
                 _onlineAccountFocus >= 2 && e.KeyCode == Keys.S)
        {
            _onlineAccountFocus = Wrap(_onlineAccountFocus + 1, 5);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode == Keys.Back && _onlineAccountFocus < 2)
        {
            BackspaceOnlineAccountField();
        }
        else if (e.Control && e.KeyCode == Keys.V && _onlineAccountFocus < 2)
        {
            PasteOnlineAccountField();
        }
        else if (e.KeyCode == Keys.Enter ||
                 e.KeyCode == Keys.Space && _onlineAccountFocus >= 2)
        {
            ActivateOnlineAccountSelection();
        }
        else return;
        ConsumeKey(e);
    }

    private void HandleLobbyBrowserKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            ExitOnlineToTitle();
        }
        else if (e.KeyCode == Keys.F5)
        {
            RequestLobbyList();
        }
        else if (e.KeyCode == Keys.Back && _onlineBrowserFocus == 0)
        {
            if (_onlineSearch.Length > 0)
                _onlineSearch = _onlineSearch[..^1];
        }
        else if (e.Control && e.KeyCode == Keys.V && _onlineBrowserFocus == 0)
        {
            PasteOnlineSearch();
        }
        else if (_onlineBrowserFocus == 1 && e.KeyCode is Keys.W or Keys.Up)
        {
            MoveLobbyListSelection(-1);
        }
        else if (_onlineBrowserFocus == 1 && e.KeyCode is Keys.S or Keys.Down)
        {
            MoveLobbyListSelection(1);
        }
        else if (e.KeyCode == Keys.Tab ||
                 _onlineBrowserFocus != 0 && e.KeyCode is Keys.D or Keys.Right or Keys.A or Keys.Left)
        {
            var reverse = e.Shift && e.KeyCode == Keys.Tab ||
                          _onlineBrowserFocus != 0 && e.KeyCode is Keys.A or Keys.Left;
            _onlineBrowserFocus = Wrap(_onlineBrowserFocus + (reverse ? -1 : 1), 6);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode == Keys.Up ||
                 _onlineBrowserFocus != 0 && e.KeyCode == Keys.W)
        {
            _onlineBrowserFocus = Wrap(_onlineBrowserFocus - 1, 6);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode == Keys.Down ||
                 _onlineBrowserFocus != 0 && e.KeyCode == Keys.S)
        {
            _onlineBrowserFocus = Wrap(_onlineBrowserFocus + 1, 6);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode == Keys.Enter ||
                 e.KeyCode == Keys.Space && _onlineBrowserFocus >= 1)
        {
            ActivateLobbyBrowserSelection();
        }
        else return;
        ConsumeKey(e);
    }

    private void HandleLobbyRoomKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            LeaveOnlineLobby();
        }
        else if (!IsOnlineLobbyHost)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
                LeaveOnlineLobby();
            else if (e.KeyCode is not (Keys.Tab or Keys.W or Keys.S or Keys.Up or Keys.Down))
                return;
        }
        else if (e.KeyCode is Keys.W or Keys.Up || e.Shift && e.KeyCode == Keys.Tab)
        {
            _onlineLobbySelection = Wrap(_onlineLobbySelection - 1, 10);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.S or Keys.Down or Keys.Tab)
        {
            _onlineLobbySelection = Wrap(_onlineLobbySelection + 1, 10);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.A or Keys.Left)
        {
            AdjustOnlineLobbySelection(-1);
        }
        else if (e.KeyCode is Keys.D or Keys.Right)
        {
            AdjustOnlineLobbySelection(1);
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            ActivateOnlineLobbySelection();
        }
        else return;

        if (!IsOnlineLobbyHost)
            _onlineLobbySelection = 9;
        ConsumeKey(e);
    }

    private void HandleOnlineMouseMove(PointF hit)
    {
        if (_mode == ScreenMode.OnlineAccount)
        {
            for (var index = 0; index < _onlineAccountFields.Length; index++)
                if (_onlineAccountFields[index].Contains(hit)) _onlineHover = index;
            for (var index = 0; index < _onlineAccountButtons.Length; index++)
                if (_onlineAccountButtons[index].Contains(hit)) _onlineHover = 10 + index;
            return;
        }

        if (_mode == ScreenMode.LobbyBrowser)
        {
            if (_onlineSearchField.Contains(hit)) _onlineHover = 20;
            for (var index = 0; index < _onlineLobbyRows.Length; index++)
                if (_onlineLobbyRows[index].Contains(hit) &&
                    _onlineLobbyListOffset + index < _onlineLobbies.Count)
                    _onlineHover = 30 + index;
            for (var index = 0; index < _onlineBrowserButtons.Length; index++)
                if (_onlineBrowserButtons[index].Contains(hit)) _onlineHover = 40 + index;
            return;
        }

        if (_mode != ScreenMode.LobbyRoom) return;
        if (CanEditOnlineLobby)
        {
            for (var index = 0; index < _onlineLobbySettingRows.Length; index++)
                if (_onlineLobbySettingRows[index].Contains(hit)) _onlineHover = 60 + index;
            if (_onlineLobbyButtons[0].Contains(hit)) _onlineHover = 80;
        }
        if (_onlineLobbyButtons[1].Contains(hit)) _onlineHover = 81;
    }

    private bool HandleOnlineMouseDown(PointF hit)
    {
        if (_mode == ScreenMode.OnlineAccount)
        {
            for (var index = 0; index < _onlineAccountFields.Length; index++)
            {
                if (!_onlineAccountFields[index].Contains(hit)) continue;
                _onlineAccountFocus = index;
                _audio.Play(AudioCue.Select);
                return true;
            }
            for (var index = 0; index < _onlineAccountButtons.Length; index++)
            {
                if (!_onlineAccountButtons[index].Contains(hit)) continue;
                _onlineAccountFocus = 2 + index;
                ActivateOnlineAccountSelection();
                return true;
            }
            return false;
        }

        if (_mode == ScreenMode.LobbyBrowser)
        {
            if (_onlineSearchField.Contains(hit))
            {
                _onlineBrowserFocus = 0;
                _audio.Play(AudioCue.Select);
                return true;
            }
            for (var index = 0; index < _onlineLobbyRows.Length; index++)
            {
                var lobbyIndex = _onlineLobbyListOffset + index;
                if (!_onlineLobbyRows[index].Contains(hit) || lobbyIndex >= _onlineLobbies.Count) continue;
                _onlineBrowserFocus = 1;
                _onlineLobbyListSelection = lobbyIndex;
                _audio.Play(AudioCue.Select);
                return true;
            }
            for (var index = 0; index < _onlineBrowserButtons.Length; index++)
            {
                if (!_onlineBrowserButtons[index].Contains(hit)) continue;
                _onlineBrowserFocus = 2 + index;
                ActivateLobbyBrowserSelection();
                return true;
            }
            return false;
        }

        if (_mode != ScreenMode.LobbyRoom) return false;
        if (CanEditOnlineLobby)
        {
            for (var index = 0; index < _onlineLobbySettingRows.Length; index++)
            {
                if (!_onlineLobbySettingRows[index].Contains(hit)) continue;
                _onlineLobbySelection = index;
                ActivateOnlineLobbySelection();
                return true;
            }
            if (_onlineLobbyButtons[0].Contains(hit))
            {
                _onlineLobbySelection = 8;
                ActivateOnlineLobbySelection();
                return true;
            }
        }
        if (_onlineLobbyButtons[1].Contains(hit))
        {
            _onlineLobbySelection = 9;
            ActivateOnlineLobbySelection();
            return true;
        }
        return false;
    }

    private void ActivateOnlineAccountSelection()
    {
        if (_onlineBusy) return;
        switch (_onlineAccountFocus)
        {
            case 0:
                _onlineAccountFocus = 1;
                _audio.Play(AudioCue.Select);
                break;
            case 1:
                _onlineAccountFocus = 2;
                _audio.Play(AudioCue.Select);
                break;
            case 2:
                _audio.Play(AudioCue.Confirm);
                BeginOnlineAuthentication(signup: true);
                break;
            case 3:
                _audio.Play(AudioCue.Confirm);
                BeginOnlineAuthentication(signup: false);
                break;
            case 4:
                _audio.Play(AudioCue.Confirm);
                ExitOnlineToTitle();
                break;
        }
    }

    private void ActivateLobbyBrowserSelection()
    {
        if (_onlineBusy) return;
        switch (_onlineBrowserFocus)
        {
            case 0:
                RequestLobbyList();
                break;
            case 1:
            case 2:
                JoinSelectedLobby();
                break;
            case 3:
                RequestLobbyList();
                break;
            case 4:
                CreateOnlineLobby();
                break;
            case 5:
                ExitOnlineToTitle();
                break;
        }
    }

    private void ActivateOnlineLobbySelection()
    {
        if (_onlineBusy) return;
        if (_onlineLobbySelection < 8)
        {
            AdjustOnlineLobbySelection(1, toggle: true);
            return;
        }
        if (_onlineLobbySelection == 8 && CanEditOnlineLobby)
        {
            _audio.Play(AudioCue.Confirm);
            _onlineBusy = true;
            _onlineStatus = "START SIGNAL SENT";
            _ = SendOnlineQuietlyAsync("lobby.start", new { }, NextOnlineRequest("start"));
            return;
        }
        if (_onlineLobbySelection == 9)
            LeaveOnlineLobby();
    }

    private void AdjustOnlineLobbySelection(int direction, bool toggle = false)
    {
        if (!CanEditOnlineLobby || _onlineLobby is null || _onlineBusy ||
            _onlineLobbySelection is < 0 or > 7)
            return;

        var settings = _onlineLobby.Settings;
        var updated = settings;
        switch (_onlineLobbySelection)
        {
            case 0:
                updated = settings with
                {
                    MapSize = (RunMapSize)Wrap((int)settings.MapSize + direction, 3)
                };
                break;
            case 1:
                updated = settings with
                {
                    MazeStrictness = (MazeStrictness)Wrap((int)settings.MazeStrictness + direction, 3)
                };
                break;
            case 2:
            {
                var nextAmount = (RunHollowAmount)Wrap((int)settings.HollowAmount + direction, 4);
                updated = settings with
                {
                    HollowAmount = nextAmount,
                    HollowTypes = nextAmount != RunHollowAmount.None &&
                                  settings.HollowTypes == RunHollowTypes.None
                        ? RunHollowTypes.All
                        : settings.HollowTypes
                };
                break;
            }
            case >= 3 and <= 6:
            {
                var flag = (RunHollowTypes)(1 << (_onlineLobbySelection - 3));
                var enabled = toggle ? !settings.HollowTypes.HasFlag(flag) : direction > 0;
                var flags = enabled ? settings.HollowTypes | flag : settings.HollowTypes & ~flag;
                if (flags == RunHollowTypes.None && settings.HollowAmount != RunHollowAmount.None)
                {
                    _onlineStatus = "ONE HOLLOW TYPE MUST REMAIN";
                    _audio.Play(AudioCue.Select);
                    return;
                }
                updated = settings with { HollowTypes = flags };
                break;
            }
            case 7:
                updated = settings with
                {
                    DifficultyScaling = toggle
                        ? !settings.DifficultyScaling
                        : direction > 0
                };
                break;
        }

        _audio.Play(toggle ? AudioCue.Confirm : AudioCue.Select);
        _onlineLobby = _onlineLobby with { Settings = updated };
        _onlineBusy = true;
        _onlineStatus = "WRITING HOST SETTINGS";
        _ = SendOnlineQuietlyAsync(
            "lobby.settings",
            new { settings = updated.ToProtocol() },
            NextOnlineRequest("settings"));
    }

    private void MoveLobbyListSelection(int direction)
    {
        if (_onlineLobbies.Count == 0) return;
        _onlineLobbyListSelection = Wrap(_onlineLobbyListSelection + direction, _onlineLobbies.Count);
        EnsureOnlineLobbySelectionVisible();
        _audio.Play(AudioCue.Select);
    }

    private void EnsureOnlineLobbySelectionVisible()
    {
        if (_onlineLobbies.Count == 0)
        {
            _onlineLobbyListSelection = 0;
            _onlineLobbyListOffset = 0;
            return;
        }

        _onlineLobbyListSelection = Math.Clamp(
            _onlineLobbyListSelection, 0, _onlineLobbies.Count - 1);
        var maximumOffset = Math.Max(0, _onlineLobbies.Count - VisibleLobbyRows);
        if (_onlineLobbyListSelection < _onlineLobbyListOffset)
            _onlineLobbyListOffset = _onlineLobbyListSelection;
        else if (_onlineLobbyListSelection >= _onlineLobbyListOffset + VisibleLobbyRows)
            _onlineLobbyListOffset = _onlineLobbyListSelection - VisibleLobbyRows + 1;
        _onlineLobbyListOffset = Math.Clamp(_onlineLobbyListOffset, 0, maximumOffset);
    }

    private void BeginOnlineAuthentication(bool signup)
    {
        var username = _onlineAccountUsername.Trim();
        var password = _onlineAccountPassword;
        _onlineServerAddress = GameSettings.ResolveOnlineServerUrl();
        var address = _onlineServerAddress;
        if (username.Length is < 3 or > 20 || username.Any(character => !IsUsernameCharacter(character)))
        {
            _onlineStatus = "USERNAME: 3-20 LETTERS NUMBERS _ -";
            return;
        }
        if (password.Length is < 8 or > 128)
        {
            _onlineStatus = "PASSWORD: 8-128 CHARACTERS";
            return;
        }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("ws" or "wss"))
        {
            _onlineStatus = "SERVER MUST USE WS OR WSS";
            return;
        }
        if (!OnlineClient.IsCredentialTransportSecure(endpoint))
        {
            _onlineStatus = "REMOTE ACCOUNTS REQUIRE WSS";
            return;
        }

        _settings.LastOnlineUsername = username;
        _onlineAccountUsername = username;
        QueueSettingsSave();

        _onlineExpectedDisconnect = false;
        _onlineBusy = true;
        _onlineStatus = signup ? "CREATING ACCOUNT" : "VERIFYING ACCOUNT";
        _onlineOperationCancellation?.Cancel();
        _onlineOperationCancellation?.Dispose();
        _onlineOperationCancellation = new CancellationTokenSource(OnlineResponseTimeout);
        var cancellationToken = _onlineOperationCancellation.Token;
        var serial = ++_onlineOperationSerial;
        var operation = signup ? "signup" : "login";
        var requestId = $"{operation}-{serial}";
        _onlineExpectedAuthenticationRequestId = requestId;
        _onlineAccountPassword = string.Empty;
        _ = ConnectAndAuthenticateAsync(
            serial, endpoint.AbsoluteUri, operation, requestId,
            username, password, cancellationToken);
    }

    private async Task ConnectAndAuthenticateAsync(
        int serial,
        string endpoint,
        string operation,
        string requestId,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var response = RegisterOnlineResponseWaiter(requestId);
        try
        {
            await _onlineClient.ConnectAsync(endpoint, cancellationToken);
            await _onlineClient.SendAsync(
                operation,
                new { username, password },
                requestId,
                cancellationToken);
            await response.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await _onlineClient.DisconnectAsync();
            _onlineUiQueue.Enqueue(() =>
            {
                if (serial != _onlineOperationSerial || _onlineExpectedDisconnect) return;
                _onlineExpectedAuthenticationRequestId = null;
                _onlineBusy = false;
                _onlineStatus = "SERVER RESPONSE TIMED OUT";
            });
        }
        catch
        {
            await _onlineClient.DisconnectAsync();
            _onlineUiQueue.Enqueue(() =>
            {
                if (serial != _onlineOperationSerial || _onlineExpectedDisconnect) return;
                _onlineExpectedAuthenticationRequestId = null;
                _onlineBusy = false;
                _onlineStatus = "SERVER UNREACHABLE";
            });
        }
        finally
        {
            _onlineResponseWaiters.TryRemove(requestId, out _);
        }
    }

    private void RequestLobbyList()
    {
        if (_onlineBusy || !_onlineClient.IsConnected) return;
        _audio.Play(AudioCue.Confirm);
        _onlineBusy = true;
        _onlineStatus = string.IsNullOrWhiteSpace(_onlineSearch)
            ? "SCANNING OPEN LOBBIES"
            : $"SEARCHING: {OnlineDisplay(_onlineSearch, 24)}";
        _ = SendOnlineQuietlyAsync(
            "lobby.search",
            new { search = _onlineSearch.Trim() },
            NextOnlineRequest("search"));
    }

    private void JoinSelectedLobby()
    {
        if (_onlineLobbies.Count == 0) return;
        _onlineLobbyListSelection = Math.Clamp(_onlineLobbyListSelection, 0, _onlineLobbies.Count - 1);
        var lobby = _onlineLobbies[_onlineLobbyListSelection];
        _audio.Play(AudioCue.Confirm);
        _onlineBusy = true;
        _onlineStatus = $"JOINING {OnlineDisplay(lobby.Name, 22)}";
        _ = SendOnlineQuietlyAsync(
            "lobby.join",
            new { lobbyId = lobby.LobbyId },
            NextOnlineRequest("join"));
    }

    private void CreateOnlineLobby()
    {
        if (string.IsNullOrWhiteSpace(_onlineUsername)) return;
        _audio.Play(AudioCue.Confirm);
        _onlineBusy = true;
        _onlineStatus = "CREATING LOBBY";
        var name = $"{_onlineUsername} LOBBY";
        _ = SendOnlineQuietlyAsync(
            "lobby.create",
            new
            {
                name,
                maxPlayers = 4,
                settings = OnlineLobbySettings.Default.ToProtocol()
            },
            NextOnlineRequest("create"));
    }

    private void LeaveOnlineLobby()
    {
        if (_onlineBusy) return;
        if (_onlineMatchActive && _mode is ScreenMode.Playing or ScreenMode.Shop)
            RecordAchievementAbandonment();
        _audio.Play(AudioCue.Confirm);
        _onlineBusy = true;
        _onlineStatus = "LEAVING LOBBY";
        _ = SendOnlineQuietlyAsync("lobby.leave", new { }, NextOnlineRequest("leave"));
    }

    private void FinishOnlineRunToLobby()
    {
        if (!_onlineMatchActive || _onlineLobby is null || _onlineBusy) return;
        if (!IsOnlineLobbyHost)
        {
            _onlineStatus = "WAITING FOR HOST TO RELEASE RUN";
            _audio.Play(AudioCue.Select);
            return;
        }

        _audio.Play(AudioCue.Confirm);
        _onlineBusy = true;
        _onlineStatus = "RELEASING RUN TO LOBBY";
        var completed = _mode == ScreenMode.Won ||
                        _onlineRunCompletedAsCasualty;
        _ = SendOnlineQuietlyAsync(
            "lobby.finish",
            new
            {
                completed,
                difficultyPenalty = completed &&
                                    _survivorDifficultyPenaltyPending
            },
            NextOnlineRequest("finish"));
    }

    private void ExitOnlineToTitle()
    {
        _audio.Play(AudioCue.Confirm);
        DisconnectOnlineSessionForTitle();
        EnterTitle();
    }

    private void DisconnectOnlineSessionForTitle()
    {
        if (_onlineExpectedDisconnect && _onlineLobby is null &&
            string.IsNullOrWhiteSpace(_onlinePlayerId))
            return;
        if (!_onlineClient.IsConnected &&
            string.IsNullOrWhiteSpace(_onlinePlayerId) &&
            _onlineLobby is null)
            return;

        _onlineExpectedDisconnect = true;
        _onlineOperationCancellation?.Cancel();
        _onlineExpectedAuthenticationRequestId = null;
        ++_onlineOperationSerial;
        ++_onlineConnectionSerial;
        CancelOnlineResponseWaiters();
        var sendLeave = _onlineLobby is not null && _onlineClient.IsConnected;
        _onlineBusy = false;
        _onlineReconnecting = false;
        _onlineAccountPassword = string.Empty;
        _onlineLobby = null;
        _onlineMatchActive = false;
        _onlinePlayerId = null;
        _onlineUsername = null;
        _onlineResumeToken = null;
        _ = CloseOnlineSessionAsync(sendLeave);
    }

    private async Task CloseOnlineSessionAsync(bool sendLeave)
    {
        if (sendLeave)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _onlineClient.SendAsync(
                    "lobby.leave", new { }, NextOnlineRequest("title-leave"), timeout.Token);
            }
            catch
            {
                // The server's disconnect grace remains the fallback cleanup path.
            }
        }
        await _onlineClient.DisconnectAsync();
    }

    private async Task SendOnlineQuietlyAsync(string type, object payload, string requestId)
    {
        var response = RegisterOnlineResponseWaiter(requestId);
        try
        {
            await _onlineClient.SendAsync(type, payload, requestId);
            await response.Task.WaitAsync(OnlineResponseTimeout);
        }
        catch (TimeoutException)
        {
            _onlineUiQueue.Enqueue(() =>
            {
                if (_onlineExpectedDisconnect) return;
                _onlineBusy = false;
                _onlineStatus = "SERVER RESPONSE TIMED OUT";
            });
        }
        catch (OperationCanceledException)
        {
            // Session shutdown cancels outstanding UI requests.
        }
        catch
        {
            _onlineUiQueue.Enqueue(() =>
            {
                if (_onlineExpectedDisconnect || _onlineReconnecting) return;
                _onlineBusy = false;
                _onlineStatus = "CONNECTION LOST";
            });
        }
        finally
        {
            _onlineResponseWaiters.TryRemove(requestId, out _);
        }
    }

    private TaskCompletionSource<OnlineMessage> RegisterOnlineResponseWaiter(string requestId)
    {
        var response = new TaskCompletionSource<OnlineMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_onlineResponseWaiters.TryAdd(requestId, response))
            throw new InvalidOperationException("An online request already uses that identifier.");
        return response;
    }

    private void CompleteOnlineResponseWaiter(OnlineMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.RequestId)) return;
        if (_onlineResponseWaiters.TryRemove(message.RequestId, out var response))
            response.TrySetResult(message);
    }

    private void CancelOnlineResponseWaiters()
    {
        foreach (var requestId in _onlineResponseWaiters.Keys)
        {
            if (_onlineResponseWaiters.TryRemove(requestId, out var response))
                response.TrySetCanceled();
        }
    }

    private void FailOnlineResponseWaiters(Exception exception)
    {
        foreach (var requestId in _onlineResponseWaiters.Keys)
        {
            if (_onlineResponseWaiters.TryRemove(requestId, out var response))
                response.TrySetException(exception);
        }
    }

    private static bool IsAuthenticationResponse(OnlineMessage message)
    {
        if (message.Type.Equals("auth.ok", StringComparison.Ordinal))
            return true;
        if (!message.Type.Equals("error", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(message.RequestId))
            return false;
        return message.RequestId.StartsWith("signup-", StringComparison.Ordinal) ||
               message.RequestId.StartsWith("login-", StringComparison.Ordinal) ||
               message.RequestId.StartsWith("resume-", StringComparison.Ordinal);
    }

    private void HandleOnlineMessage(OnlineMessage message)
    {
        if (_onlineExpectedDisconnect) return;
        if (IsAuthenticationResponse(message) &&
            !string.Equals(
                message.RequestId,
                _onlineExpectedAuthenticationRequestId,
                StringComparison.Ordinal))
            return;

        CompleteOnlineResponseWaiter(message);
        switch (message.Type)
        {
            case "auth.ok":
                HandleOnlineAuthenticated(message.Data);
                break;
            case "error":
                HandleOnlineError(message.RequestId, message.Data);
                break;
            case "lobby.list":
                HandleLobbyList(message.Data);
                break;
            case "lobby.refresh":
                if (_mode == ScreenMode.LobbyBrowser)
                    HandleLobbyList(message.Data);
                break;
            case "lobby.state":
                HandleLobbyState(ParseLobbyState(message.Data, _onlineLobby));
                break;
            case "lobby.left":
                _onlineLobby = null;
                _onlineMatchActive = false;
                _onlineBusy = false;
                _onlineStatus = "LOBBY RELEASED";
                _mode = ScreenMode.LobbyBrowser;
                _onlineBrowserFocus = 1;
                _audio.StopMusic();
                RequestMenuMusic();
                RequestLobbyList();
                break;
            case "lobby.started":
            {
                var started = ParseLobbyState(message.Data, _onlineLobby);
                if (_onlineLobby is { } current &&
                    string.Equals(current.LobbyId, started.LobbyId,
                        StringComparison.OrdinalIgnoreCase) &&
                    started.Revision < current.Revision)
                    break;
                _onlineLobby = started;
                if (_onlineMatchActive)
                    break;
                _onlineMatchActive = true;
                _onlineBusy = false;
                _onlineStatus = "RUN STARTED";
                BeginOnlineRun(started);
                break;
            }
            case "game.event":
            case "game.checkpoint":
                QueueOnlineGameplayMessage(message);
                break;
        }
    }

    private void HandleOnlineAuthenticated(JsonElement data)
    {
        _onlineExpectedAuthenticationRequestId = null;
        _onlinePlayerId = ReadString(data, "playerId");
        _onlineUsername = ReadString(data, "username");
        _onlineResumeToken = ReadString(data, "resumeToken");
        _onlineBusy = false;
        var wasReconnecting = _onlineReconnecting;
        _onlineReconnecting = false;
        _onlineStatus = wasReconnecting ? "LINK RESTORED" : $"SIGNED IN: {_onlineUsername}";
        if (!string.IsNullOrWhiteSpace(_onlineUsername))
        {
            _settings.LastOnlineUsername = _onlineUsername;
            QueueSettingsSave();
        }

        if (wasReconnecting)
        {
            if (_onlineMatchActive)
                ReconcileOnlinePredictionAfterReconnect();
            if (_mode == ScreenMode.LobbyBrowser)
                RequestLobbyList();
            return;
        }

        _mode = ScreenMode.LobbyBrowser;
        _onlineBrowserFocus = 1;
        _onlineLobbyListSelection = 0;
        _onlineLobbyListOffset = 0;
        RequestLobbyList();
    }

    private void HandleOnlineError(string? requestId, JsonElement data)
    {
        var isResume = requestId?.StartsWith("resume-", StringComparison.Ordinal) == true;
        var code = ReadString(data, "code");
        if (isResume && _onlineReconnecting &&
            code.Equals("ALREADY_ONLINE", StringComparison.OrdinalIgnoreCase))
        {
            _onlineExpectedAuthenticationRequestId = null;
            _onlineStatus = "PRIOR LINK CLOSING / RETRYING";
            return;
        }

        if (requestId?.StartsWith("signup-", StringComparison.Ordinal) == true ||
            requestId?.StartsWith("login-", StringComparison.Ordinal) == true ||
            isResume)
            _onlineExpectedAuthenticationRequestId = null;
        _onlineBusy = false;
        var message = ReadString(data, "message");
        _onlineStatus = OnlineDisplay(
            string.IsNullOrWhiteSpace(message) ? "SERVER REJECTED REQUEST" : message,
            52);
        if (isResume)
        {
            _onlineReconnecting = false;
            _onlinePlayerId = null;
            _onlineUsername = null;
            _onlineResumeToken = null;
            _onlineLobby = null;
            _onlineMatchActive = false;
            _mode = ScreenMode.OnlineAccount;
        }
    }

    private void HandleLobbyList(JsonElement data)
    {
        if (!data.TryGetProperty("lobbies", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
            return;
        var selectedLobbyId = _onlineLobbyListSelection >= 0 &&
                              _onlineLobbyListSelection < _onlineLobbies.Count
            ? _onlineLobbies[_onlineLobbyListSelection].LobbyId
            : null;
        var lobbies = new List<OnlineLobbySummary>();
        foreach (var entry in entries.EnumerateArray())
        {
            var id = ReadString(entry, "lobbyId");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var summary = new OnlineLobbySummary(
                id,
                ReadString(entry, "name"),
                ReadString(entry, "hostUsername"),
                ReadInt(entry, "connectedPlayerCount", ReadInt(entry, "playerCount", 0)),
                ReadInt(entry, "maxPlayers", 4),
                "waiting");
            var search = _onlineSearch.Trim();
            if (search.Length > 0 &&
                !summary.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !summary.HostUsername.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !summary.LobbyId.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            lobbies.Add(summary);
        }
        _onlineLobbies = lobbies;
        var preservedSelection = selectedLobbyId is null
            ? -1
            : lobbies.FindIndex(lobby =>
                string.Equals(lobby.LobbyId, selectedLobbyId, StringComparison.OrdinalIgnoreCase));
        if (preservedSelection >= 0)
            _onlineLobbyListSelection = preservedSelection;
        EnsureOnlineLobbySelectionVisible();
        _onlineBusy = false;
        _onlineStatus = lobbies.Count == 0
            ? "NO OPEN LOBBIES FOUND"
            : $"{lobbies.Count:00} OPEN LOBBIES";
    }

    private void HandleLobbyState(OnlineLobbyState state)
    {
        if (_onlineLobby is { } current &&
            string.Equals(current.LobbyId, state.LobbyId, StringComparison.OrdinalIgnoreCase) &&
            state.Revision < current.Revision)
            return;
        var previousHost = _onlineLobby?.HostPlayerId;
        _onlineLobby = state;
        _onlineBusy = false;
        NotifyOnlineLobbyStateChanged(state);
        if (!string.Equals(previousHost, state.HostPlayerId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(previousHost))
            _onlineStatus = IsOnlineLobbyHost ? "HOST CONTROL TRANSFERRED TO YOU" : "HOST CONTROL TRANSFERRED";
        else
            _onlineStatus = IsOnlineLobbyHost ? "HOST CONTROL ACTIVE" : "WAITING FOR HOST";

        if (state.Status.Equals("inGame", StringComparison.OrdinalIgnoreCase) &&
            state.Seed is not null)
        {
            if (!_onlineMatchActive)
            {
                _onlineMatchActive = true;
                _onlineStatus = "RUN RECOVERED";
                BeginOnlineRun(state);
            }
            return;
        }

        if (state.Status.Equals("waiting", StringComparison.OrdinalIgnoreCase))
        {
            var returnedFromRun = _onlineMatchActive;
            _onlineMatchActive = false;
            _mode = ScreenMode.LobbyRoom;
            _onlineLobbySelection = IsOnlineLobbyHost ? 0 : 9;
            ResetHover();
            if (returnedFromRun)
            {
                _audio.StopMusic();
                RequestMenuMusic();
            }
        }
    }

    private void HandleOnlineConnectionClosed(string reason)
    {
        if (_onlineExpectedDisconnect) return;
        _onlineExpectedAuthenticationRequestId = null;
        FailOnlineResponseWaiters(new IOException(reason));
        _onlineBusy = false;
        _onlineStatus = OnlineDisplay(reason, 44);
        if (string.IsNullOrWhiteSpace(_onlineResumeToken) ||
            string.IsNullOrWhiteSpace(_onlineServerAddress))
        {
            _onlinePlayerId = null;
            _onlineUsername = null;
            _mode = ScreenMode.OnlineAccount;
            return;
        }

        BeginOnlineReconnect();
    }

    private void BeginOnlineReconnect()
    {
        if (_onlineReconnecting) return;
        _onlineReconnecting = true;
        _onlineStatus = "LINK LOST / RECONNECTING";
        _onlineOperationCancellation?.Cancel();
        _onlineOperationCancellation?.Dispose();
        _onlineOperationCancellation = new CancellationTokenSource();
        var serial = ++_onlineConnectionSerial;
        _ = ReconnectOnlineAsync(serial, _onlineOperationCancellation.Token);
    }

    private async Task ReconnectOnlineAsync(int serial, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + OnlineReconnectWindow;
        for (var attempt = 1;
             attempt <= 5 &&
             !cancellationToken.IsCancellationRequested &&
             DateTimeOffset.UtcNow < deadline;
             attempt++)
        {
            var requestId = $"resume-{serial}-{attempt}";
            TaskCompletionSource<OnlineMessage>? response = null;
            try
            {
                if (attempt > 1)
                {
                    var retryDelay = TimeSpan.FromMilliseconds(500 * (attempt - 1));
                    var remainingBeforeDelay = deadline - DateTimeOffset.UtcNow;
                    if (remainingBeforeDelay <= TimeSpan.Zero) break;
                    await Task.Delay(
                        retryDelay < remainingBeforeDelay ? retryDelay : remainingBeforeDelay,
                        cancellationToken);
                }
                _onlineUiQueue.Enqueue(() =>
                {
                    if (serial == _onlineConnectionSerial)
                        _onlineStatus = $"RECONNECT ATTEMPT {attempt}/5";
                });

                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                using var attemptCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCancellation.CancelAfter(
                    remaining < OnlineReconnectAttemptTimeout
                        ? remaining
                        : OnlineReconnectAttemptTimeout);

                response = RegisterOnlineResponseWaiter(requestId);
                _onlineExpectedAuthenticationRequestId = requestId;
                await _onlineClient.ConnectAsync(
                    _onlineServerAddress, attemptCancellation.Token);
                await _onlineClient.SendAsync(
                    "resume",
                    new { token = _onlineResumeToken },
                    requestId,
                    attemptCancellation.Token);
                var reply = await response.Task.WaitAsync(attemptCancellation.Token);
                if (reply.Type.Equals("auth.ok", StringComparison.Ordinal))
                    return;
                if (!reply.Type.Equals("error", StringComparison.Ordinal) ||
                    !ReadString(reply.Data, "code")
                        .Equals("ALREADY_ONLINE", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Continue through the bounded retry schedule.
            }
            finally
            {
                _onlineExpectedAuthenticationRequestId = null;
                if (response is not null)
                    _onlineResponseWaiters.TryRemove(requestId, out _);
            }

            await _onlineClient.DisconnectAsync();
        }

        _onlineUiQueue.Enqueue(() =>
        {
            if (serial != _onlineConnectionSerial) return;
            _onlineReconnecting = false;
            _onlineBusy = false;
            _onlineStatus = "RECONNECT FAILED / LOG IN AGAIN";
            _onlinePlayerId = null;
            _onlineUsername = null;
            _onlineResumeToken = null;
            _onlineLobby = null;
            _onlineMatchActive = false;
            _mode = ScreenMode.OnlineAccount;
        });
    }

    private static OnlineLobbyState ParseLobbyState(JsonElement data, OnlineLobbyState? fallback)
    {
        var settings = data.TryGetProperty("settings", out var settingsNode)
            ? ParseOnlineSettings(settingsNode)
            : fallback?.Settings ?? OnlineLobbySettings.Default;
        var players = new List<OnlineLobbyPlayer>();
        if (data.TryGetProperty("players", out var playersNode) &&
            playersNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var player in playersNode.EnumerateArray())
            {
                players.Add(new OnlineLobbyPlayer(
                    ReadString(player, "playerId"),
                    ReadString(player, "username"),
                    ReadInt(player, "joinOrder", players.Count),
                    ReadBool(player, "connected", true)));
            }
        }
        else if (fallback is not null)
        {
            players.AddRange(fallback.Players);
        }

        var runStartPlayers = new List<OnlineRunPlayer>();
        if (data.TryGetProperty("runStartPlayers", out var runStartPlayersNode) &&
            runStartPlayersNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var player in runStartPlayersNode.EnumerateArray())
            {
                runStartPlayers.Add(new OnlineRunPlayer(
                    ReadString(player, "playerId"),
                    ReadString(player, "username"),
                    ReadInt(player, "joinOrder", runStartPlayers.Count)));
            }
        }
        else if (fallback is not null)
        {
            runStartPlayers.AddRange(fallback.RunStartPlayers);
        }

        long? seed = fallback?.Seed;
        if (data.TryGetProperty("seed", out var seedNode))
            seed = seedNode.ValueKind == JsonValueKind.Number && seedNode.TryGetInt64(out var value)
                ? value
                : null;
        if (seed is not null && runStartPlayers.Count == 0)
        {
            // Compatibility with servers which predate the immutable run roster.
            // Capture the first in-game player list instead of allowing later
            // disconnect state to change deterministic objective generation.
            runStartPlayers.AddRange(players.Select(player => new OnlineRunPlayer(
                player.PlayerId, player.Username, player.JoinOrder)));
        }
        return new OnlineLobbyState(
            ReadString(data, "lobbyId", fallback?.LobbyId ?? string.Empty),
            ReadString(data, "name", fallback?.Name ?? "ONLINE LOBBY"),
            ReadString(data, "hostPlayerId", fallback?.HostPlayerId ?? string.Empty),
            ReadInt(data, "maxPlayers", fallback?.MaxPlayers ?? 4),
            ReadString(data, "status", fallback?.Status ?? "waiting"),
            ReadLong(data, "revision", fallback?.Revision ?? 0),
            ReadLong(data, "authorityEpoch", fallback?.AuthorityEpoch ?? 0),
            Math.Clamp(ReadInt(data, "runLevel", fallback?.RunLevel ?? 1), 1, 1000),
            settings,
            players,
            seed)
        {
            RunStartPlayers = runStartPlayers
        };
    }

    private static OnlineLobbySettings ParseOnlineSettings(JsonElement data)
    {
        var map = ParseEnum(ReadString(data, "mapSize"), RunMapSize.Medium);
        var strictness = ParseEnum(ReadString(data, "mazeStrictness"), MazeStrictness.Normal);
        var amount = ParseEnum(ReadString(data, "hollowAmount"), RunHollowAmount.Normal);
        var types = RunHollowTypes.None;
        if (data.TryGetProperty("hollowTypes", out var typeNode) &&
            typeNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var type in typeNode.EnumerateArray())
            {
                if (type.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<RunHollowTypes>(type.GetString(), true, out var parsed))
                    types |= parsed;
            }
        }
        if (types == RunHollowTypes.None && amount != RunHollowAmount.None)
            types = RunHollowTypes.All;
        return new OnlineLobbySettings(
            map,
            strictness,
            amount,
            types,
            ReadBool(data, "difficultyScaling", true));
    }

    private void DrawOnlineAccountConsole(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell, "ONLINE ACCOUNT");
        LabFont.Draw(g, "ONLINE PLAY", 72, 74, 3, C.Bone);

        var identityBay = new RectangleF(72, 132, 374, 496);
        DrawCutPanel(g, identityBay, Color.FromArgb(8, 15, 15), Color.FromArgb(72, 86, 73), 16, 4);
        DrawPanelBolts(g, identityBay, C.Steel);
        LabFont.Draw(g, "REMOTE SUBJECT", identityBay.X + 26, identityBay.Y + 26, 2, C.Signal);
        LabFont.Draw(g, "ACCOUNT TERMINAL", identityBay.X + 26, identityBay.Y + 58, 1, C.Steel);
        var center = new PointF(identityBay.X + identityBay.Width / 2, identityBay.Y + 238);
        DrawReticle(g, center, 102, Color.FromArgb(76, C.Steel));
        DrawDrone(g, _drone, _playerColor, _playerFrameColor, center, 72, 255, true, false);
        LabFont.Draw(g, "NO EMAIL REQUIRED", identityBay.X + identityBay.Width / 2,
            identityBay.Bottom - 92, 1, C.Sick, LabTextAlign.Center);
        LabFont.Draw(g, "PASSWORDS ARE NEVER SAVED", identityBay.X + identityBay.Width / 2,
            identityBay.Bottom - 61, 1, C.Oxide, LabTextAlign.Center);

        var form = new RectangleF(478, 132, 710, 496);
        DrawCutPanel(g, form, Color.FromArgb(13, 21, 20), Color.FromArgb(72, 86, 73), 16, 4);
        DrawPanelBolts(g, form, C.Steel);
        LabFont.Draw(g, "IDENTITY ENTRY", form.X + 28, form.Y + 24, 2, C.Signal);

        _onlineAccountFields[0] = new RectangleF(form.X + 28, form.Y + 82, form.Width - 56, 96);
        _onlineAccountFields[1] = new RectangleF(form.X + 28, form.Y + 210, form.Width - 56, 96);
        DrawOnlineTextField(g, _onlineAccountFields[0], "USERNAME",
            OnlineDisplay(_onlineAccountUsername, 34), _onlineAccountFocus == 0, _onlineHover == 0);
        DrawOnlineTextField(g, _onlineAccountFields[1], "PASSWORD",
            new string('*', Math.Min(40, _onlineAccountPassword.Length)),
            _onlineAccountFocus == 1, _onlineHover == 1);

        LabFont.Draw(g, OnlineDisplay(_onlineStatus, 52), form.X + 28, form.Bottom - 86, 1,
            _onlineBusy || _onlineReconnecting ? C.Signal : C.Sick);
        if (_onlineBusy)
            DrawOnlineActivity(g, new RectangleF(form.Right - 150, form.Bottom - 91, 116, 20));

        _onlineAccountButtons[0] = new RectangleF(478, 650, 260, 62);
        _onlineAccountButtons[1] = new RectangleF(754, 650, 238, 62);
        _onlineAccountButtons[2] = new RectangleF(1008, 650, 180, 62);
        DrawOnlineButton(g, _onlineAccountButtons[0], "SIGN UP", _onlineAccountFocus == 2, _onlineHover == 10);
        DrawOnlineButton(g, _onlineAccountButtons[1], "LOG IN", _onlineAccountFocus == 3, _onlineHover == 11);
        DrawAbortButton(g, _onlineAccountButtons[2], "BACK", _onlineAccountFocus == 4 || _onlineHover == 12);
        if (_onlineAccountFocus == 4) DrawKeyboardFocusMarker(g, _onlineAccountButtons[2]);
    }

    private RectangleF _onlineSearchField;

    private void DrawLobbyBrowserConsole(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell,
            $"SIGNED IN / {OnlineDisplay(_onlineUsername ?? string.Empty, 20)}");
        LabFont.Draw(g, "ONLINE LOBBIES", 72, 74, 3, C.Bone);

        var directory = new RectangleF(72, 130, 772, 498);
        DrawCutPanel(g, directory, Color.FromArgb(8, 15, 15), Color.FromArgb(72, 86, 73), 16, 4);
        DrawPanelBolts(g, directory, C.Steel);
        _onlineSearchField = new RectangleF(directory.X + 24, directory.Y + 24, directory.Width - 48, 62);
        DrawOnlineTextField(g, _onlineSearchField, "SEARCH NAME HOST OR ID",
            OnlineDisplay(_onlineSearch, 44), _onlineBrowserFocus == 0, _onlineHover == 20);

        LabFont.Draw(g, "LOBBY", directory.X + 30, directory.Y + 108, 1, C.Steel);
        LabFont.Draw(g, "HOST", directory.X + 434, directory.Y + 108, 1, C.Steel);
        LabFont.Draw(g, "LINKS", directory.Right - 28, directory.Y + 108, 1, C.Steel, LabTextAlign.Right);
        for (var index = 0; index < _onlineLobbyRows.Length; index++)
        {
            var rect = new RectangleF(directory.X + 22, directory.Y + 132 + index * 54,
                directory.Width - 44, 46);
            _onlineLobbyRows[index] = rect;
            var lobbyIndex = _onlineLobbyListOffset + index;
            if (lobbyIndex >= _onlineLobbies.Count)
            {
                using var emptyEdge = new Pen(Color.FromArgb(44, C.Steel), 1);
                g.DrawRectangle(emptyEdge, rect.X, rect.Y, rect.Width, rect.Height);
                continue;
            }

            var lobby = _onlineLobbies[lobbyIndex];
            var focused = _onlineBrowserFocus == 1 && _onlineLobbyListSelection == lobbyIndex;
            var hovered = _onlineHover == 30 + index;
            DrawCutPanel(g, rect,
                focused ? Color.FromArgb(45, 53, 43) : Color.FromArgb(17, 25, 24),
                focused || hovered ? C.Signal : C.Steel, 7, focused ? 3 : 2);
            LabFont.Draw(g, OnlineDisplay(lobby.Name, 25), rect.X + 14, rect.Y + 15, 1,
                focused || hovered ? C.Bone : C.Sick);
            LabFont.Draw(g, OnlineDisplay(lobby.HostUsername, 16), rect.X + 412, rect.Y + 15, 1,
                focused || hovered ? C.Signal : C.Steel);
            LabFont.Draw(g, $"{lobby.PlayerCount}/{lobby.MaxPlayers}", rect.Right - 14, rect.Y + 15, 1,
                lobby.PlayerCount >= lobby.MaxPlayers ? C.Oxide : C.Sick, LabTextAlign.Right);
            if (focused) DrawKeyboardFocusMarker(g, rect);
        }
        if (_onlineLobbies.Count == 0)
            LabFont.Draw(g, "NO OPEN LOBBIES", directory.X + directory.Width / 2,
                directory.Y + 286, 2, C.Oxide, LabTextAlign.Center);
        else if (_onlineLobbies.Count > VisibleLobbyRows)
        {
            var firstVisible = _onlineLobbyListOffset + 1;
            var lastVisible = Math.Min(
                _onlineLobbyListOffset + VisibleLobbyRows, _onlineLobbies.Count);
            LabFont.Draw(g, $"{firstVisible:00}-{lastVisible:00} / {_onlineLobbies.Count:00}",
                directory.Right - 26, directory.Bottom - 20, 1, C.Steel, LabTextAlign.Right);
        }

        var actions = new RectangleF(874, 130, 314, 498);
        DrawCutPanel(g, actions, Color.FromArgb(13, 21, 20), Color.FromArgb(72, 86, 73), 16, 4);
        DrawPanelBolts(g, actions, C.Steel);
        LabFont.Draw(g, "DIRECTORY ACTIONS", actions.X + 24, actions.Y + 24, 2, C.Signal);
        var labels = new[] { "JOIN SELECTED", "SEARCH", "CREATE LOBBY", "BACK" };
        for (var index = 0; index < _onlineBrowserButtons.Length; index++)
        {
            var rect = new RectangleF(actions.X + 24, actions.Y + 80 + index * 91,
                actions.Width - 48, 66);
            _onlineBrowserButtons[index] = rect;
            if (index == 3)
            {
                DrawAbortButton(g, rect, labels[index],
                    _onlineBrowserFocus == 2 + index || _onlineHover == 40 + index);
                if (_onlineBrowserFocus == 2 + index) DrawKeyboardFocusMarker(g, rect);
            }
            else
            {
                DrawOnlineButton(g, rect, labels[index],
                    _onlineBrowserFocus == 2 + index, _onlineHover == 40 + index,
                    enabled: index != 0 || _onlineLobbies.Count > 0);
            }
        }
        LabFont.Draw(g, OnlineDisplay(_onlineStatus, 44), 72, 671, 1,
            _onlineBusy || _onlineReconnecting ? C.Signal : C.Sick);
        LabFont.Draw(g, "TAB CHANGES CONTROL   ENTER CONFIRMS", 1188, 694, 1,
            C.Steel, LabTextAlign.Right);
    }

    private void DrawLobbyRoomConsole(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell,
            _onlineLobby is null ? "ONLINE WAITING ROOM" : $"LOBBY ID / {_onlineLobby.LobbyId}");
        var lobbyName = _onlineLobby?.Name ?? "LOBBY";
        LabFont.Draw(g, OnlineDisplay(lobbyName, 30), 72, 74, 3, C.Bone);

        var roster = new RectangleF(72, 130, 408, 498);
        DrawCutPanel(g, roster, Color.FromArgb(8, 15, 15), Color.FromArgb(72, 86, 73), 16, 4);
        DrawPanelBolts(g, roster, C.Steel);
        LabFont.Draw(g, "CONNECTED DRONES", roster.X + 24, roster.Y + 24, 2, C.Signal);
        var players = _onlineLobby?.Players ?? Array.Empty<OnlineLobbyPlayer>();
        for (var index = 0; index < Math.Min(8, players.Count); index++)
        {
            var player = players[index];
            var row = new RectangleF(roster.X + 22, roster.Y + 72 + index * 48,
                roster.Width - 44, 40);
            var isHost = string.Equals(player.PlayerId, _onlineLobby?.HostPlayerId,
                StringComparison.OrdinalIgnoreCase);
            var isSelf = string.Equals(player.PlayerId, _onlinePlayerId,
                StringComparison.OrdinalIgnoreCase);
            DrawCutPanel(g, row, Color.FromArgb(17, 25, 24),
                player.Connected ? C.Steel : C.Oxide, 7, 2);
            LabFont.Draw(g, OnlineDisplay(player.Username, 18), row.X + 13, row.Y + 12, 1,
                player.Connected ? (isSelf ? C.Signal : C.Sick) : C.Oxide);
            var state = !player.Connected ? "LINK LOST" : isHost ? "HOST" : isSelf ? "YOU" : "CONNECTED";
            LabFont.Draw(g, state, row.Right - 13, row.Y + 12, 1,
                isHost ? C.Signal : player.Connected ? C.Steel : C.Oxide, LabTextAlign.Right);
        }
        if (players.Count == 0)
            LabFont.Draw(g, "AWAITING ROSTER", roster.X + roster.Width / 2,
                roster.Y + 260, 2, C.Oxide, LabTextAlign.Center);

        var settingsBay = new RectangleF(508, 130, 680, 498);
        DrawCutPanel(g, settingsBay, Color.FromArgb(13, 21, 20), Color.FromArgb(72, 86, 73), 16, 4);
        DrawPanelBolts(g, settingsBay, C.Steel);
        var settingsHeader = _onlineReconnecting
            ? "HOST RUN SETTINGS / LINK LOST"
            : IsOnlineLobbyHost ? "HOST RUN SETTINGS" : "HOST RUN SETTINGS / READ ONLY";
        LabFont.Draw(g, settingsHeader, settingsBay.X + 24, settingsBay.Y + 24, 2,
            CanEditOnlineLobby ? C.Signal : C.Steel);
        var settings = _onlineLobby?.Settings ?? OnlineLobbySettings.Default;
        for (var index = 0; index < _onlineLobbySettingRows.Length; index++)
        {
            var rect = new RectangleF(settingsBay.X + 22, settingsBay.Y + 68 + index * 49,
                settingsBay.Width - 44, 42);
            _onlineLobbySettingRows[index] = rect;
            var focused = CanEditOnlineLobby && _onlineLobbySelection == index;
            var hovered = CanEditOnlineLobby && _onlineHover == 60 + index;
            DrawCutPanel(g, rect,
                focused ? Color.FromArgb(44, 52, 43) : Color.FromArgb(18, 27, 25),
                focused || hovered ? C.Signal : C.Steel, 7, focused ? 3 : 2);
            LabFont.Draw(g, OnlineSettingLabels[index], rect.X + 14, rect.Y + 13, 1,
                focused || hovered ? C.Bone : C.Sick);
            LabFont.Draw(g, OnlineSettingValue(settings, index), rect.Right - 14, rect.Y + 13, 1,
                focused || hovered ? C.Signal : C.Steel, LabTextAlign.Right);
            if (focused) DrawKeyboardFocusMarker(g, rect);
        }

        _onlineLobbyButtons[0] = new RectangleF(642, 650, 330, 62);
        _onlineLobbyButtons[1] = new RectangleF(988, 650, 200, 62);
        if (CanEditOnlineLobby)
            DrawOnlineButton(g, _onlineLobbyButtons[0], "START RUN",
                _onlineLobbySelection == 8, _onlineHover == 80);
        else if (IsOnlineLobbyHost)
            DrawOnlineButton(g, _onlineLobbyButtons[0], "RECONNECTING",
                false, false, enabled: false);
        else
            DrawOnlineButton(g, _onlineLobbyButtons[0], "WAITING FOR HOST",
                false, false, enabled: false);
        DrawAbortButton(g, _onlineLobbyButtons[1], "LEAVE",
            _onlineLobbySelection == 9 || _onlineHover == 81);
        if (_onlineLobbySelection == 9) DrawKeyboardFocusMarker(g, _onlineLobbyButtons[1]);
        LabFont.Draw(g, OnlineDisplay(_onlineStatus, 44), 72, 671, 1,
            _onlineBusy || _onlineReconnecting ? C.Signal : C.Sick);
        if (CanEditOnlineLobby)
            LabFont.Draw(g, "ARROWS ADJUST   ENTER APPLY", 72, 697, 1, C.Steel);
    }

    private void DrawOnlineTextField(
        Graphics g,
        RectangleF rect,
        string label,
        string value,
        bool focused,
        bool hovered)
    {
        var active = focused || hovered;
        DrawCutPanel(g, rect,
            focused ? Color.FromArgb(38, 49, 43) : Color.FromArgb(5, 12, 12),
            active ? C.Signal : C.Steel, 8, focused ? 4 : 2);
        LabFont.Draw(g, label, rect.X + 18, rect.Y + 13, 1, active ? C.Signal : C.Steel);
        LabFont.Draw(g, string.IsNullOrEmpty(value) ? " " : value, rect.X + 18,
            rect.Y + (rect.Height >= 82 ? 47 : 39), 1, active ? C.Bone : C.Sick);
        if (focused)
        {
            DrawKeyboardFocusMarker(g, rect);
            if ((int)(_time * 2.2f) % 2 == 0)
            {
                var width = Math.Min(rect.Width - 46, LabFont.Measure(value, 1).Width);
                using var caret = new SolidBrush(C.Signal);
                g.FillRectangle(caret, rect.X + 20 + width,
                    rect.Y + (rect.Height >= 82 ? 45 : 37), 4, 18);
            }
        }
    }

    private void DrawOnlineButton(
        Graphics g,
        RectangleF rect,
        string label,
        bool focused,
        bool hovered,
        bool enabled = true)
    {
        var active = enabled && (focused || hovered);
        DrawLatchButton(g, rect, label, active, showState: false);
        if (!enabled)
        {
            using var disabled = new SolidBrush(Color.FromArgb(148, C.Void));
            g.FillRectangle(disabled, rect);
            using var edge = new Pen(C.Steel, 2);
            g.DrawRectangle(edge, rect.X, rect.Y, rect.Width, rect.Height);
        }
        if (focused && enabled) DrawKeyboardFocusMarker(g, rect);
    }

    private void DrawOnlineActivity(Graphics g, RectangleF rect)
    {
        using var off = new SolidBrush(Color.FromArgb(45, C.Steel));
        using var on = new SolidBrush(C.Signal);
        const int segments = 8;
        var active = (int)(_time * 8) % segments;
        var width = (rect.Width - (segments - 1) * 4) / segments;
        for (var index = 0; index < segments; index++)
            g.FillRectangle(index == active ? on : off, rect.X + index * (width + 4),
                rect.Y, width, rect.Height);
    }

    private void BackspaceOnlineAccountField()
    {
        switch (_onlineAccountFocus)
        {
            case 0 when _onlineAccountUsername.Length > 0:
                _onlineAccountUsername = _onlineAccountUsername[..^1];
                break;
            case 1 when _onlineAccountPassword.Length > 0:
                _onlineAccountPassword = _onlineAccountPassword[..^1];
                break;
        }
    }

    private void PasteOnlineAccountField()
    {
        try
        {
            var text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text)) return;
            switch (_onlineAccountFocus)
            {
                case 0:
                    _onlineAccountUsername = new string(
                        (_onlineAccountUsername + text)
                        .Where(IsUsernameCharacter).Take(20).ToArray());
                    break;
                case 1:
                    _onlineAccountPassword = new string(
                        (_onlineAccountPassword + text)
                        .Where(character => character is >= ' ' and <= '~').Take(128).ToArray());
                    break;
            }
        }
        catch
        {
            _onlineStatus = "CLIPBOARD UNAVAILABLE";
        }
    }

    private void PasteOnlineSearch()
    {
        try
        {
            _onlineSearch = new string(
                (_onlineSearch + Clipboard.GetText())
                .Where(IsSearchCharacter).Take(40).ToArray());
        }
        catch
        {
            _onlineStatus = "CLIPBOARD UNAVAILABLE";
        }
    }

    private string NextOnlineRequest(string kind) => $"{kind}-{++_onlineOperationSerial}";

    private static bool IsUsernameCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '_' or '-';

    private static bool IsSearchCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is ' ' or '_' or '-' or '#';

    private static string OnlineSettingValue(OnlineLobbySettings settings, int index) => index switch
    {
        0 => settings.MapSize.ToString().ToUpperInvariant(),
        1 => settings.MazeStrictness.ToString().ToUpperInvariant(),
        2 => settings.HollowAmount.ToString().ToUpperInvariant(),
        3 => settings.HollowTypes.HasFlag(RunHollowTypes.Square) ? "ON" : "OFF",
        4 => settings.HollowTypes.HasFlag(RunHollowTypes.Diamond) ? "ON" : "OFF",
        5 => settings.HollowTypes.HasFlag(RunHollowTypes.Hex) ? "ON" : "OFF",
        6 => settings.HollowTypes.HasFlag(RunHollowTypes.Sentry) ? "ON" : "OFF",
        7 => settings.DifficultyScaling ? "ON" : "OFF",
        _ => string.Empty
    };

    private static string OnlineDisplay(string value, int maximum)
    {
        var cleaned = new string(value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Where(character => character is >= ' ' and <= '~')
            .ToArray());
        return cleaned.Length <= maximum ? cleaned : cleaned[..Math.Max(0, maximum - 3)] + "...";
    }

    private static string OnlineDisplayTail(string value, int maximum) =>
        value.Length <= maximum ? value : "..." + value[^(maximum - 3)..];

    private static string ReadString(JsonElement element, string name, string fallback = "")
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static int ReadInt(JsonElement element, string name, int fallback)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.TryGetInt32(out var result)
            ? result
            : fallback;
    }

    private static long ReadLong(JsonElement element, string name, long fallback)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.TryGetInt64(out var result)
            ? result
            : fallback;
    }

    private static bool ReadBool(JsonElement element, string name, bool fallback)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
}
