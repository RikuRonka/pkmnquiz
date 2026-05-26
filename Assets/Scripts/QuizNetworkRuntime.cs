using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class QuizNetworkRuntime
{
    private const int MaxRemotePlayers = 31;
    private const int MinimumPlayersToStart = 1;
    private const string ConnectionType = "dtls";
    private const string LobbyCodeKey = "code";
    private const string LobbyRelayKey = "relay";
    private const string LobbyAppKey = "app";
    private const string LobbyAppValue = "pkmnquiz";
    private const string LobbyHostNameKey = "host";
    private const string LobbyActiveQuizKey = "quiz";
    private const string PlayerNameKey = "name";
    private const string PlayerColorKey = "color";
    private const string QuizSelectionMessage = "pkmnquiz_quiz_selection";
    private const int QuizSelectionMessageSize = 256;
    private const string HostProfilePrefix = "pkmn_host";
    private const string ClientProfilePrefix = "pkmn_join";
    private const string DefaultPlayerColor = "#6FEA72";
    public static readonly string[] PlayerColorPalette =
    {
        "#5FB52E",
        "#D6F51F",
        "#FFFF24",
        "#FDBB00",
        "#FF9800",
        "#FF8A1C",
        "#FF66C4",
        "#9900B8",
        "#4300A8",
        "#0754FF",
        "#1599C7",
        "#24D6C8",
    };

    public static string JoinCode { get; private set; }
    public static string RelayJoinCode { get; private set; }
    public static string PlayerNickname { get; private set; } = "Player";
    public static string PlayerColorHex { get; private set; } = DefaultPlayerColor;
    public static string LobbyId { get; private set; }
    public static int ActiveQuizGeneration { get; private set; }
    public static string ActiveQuizTypeFilter { get; private set; }
    public static bool HasActiveQuizSelection { get; private set; }
    public static int MaxPlayerCount => MaxRemotePlayers + 1;
    public static int RequiredPlayerCount => MinimumPlayersToStart;

    public static bool IsMultiplayerActive =>
        GameSettings.IsMultiplayer
        && NetworkManager.Singleton
        && NetworkManager.Singleton.IsListening;

    public static bool IsMultiplayerClientOnly =>
        IsMultiplayerActive
        && NetworkManager.Singleton.IsClient
        && !NetworkManager.Singleton.IsServer;

    public static bool IsMultiplayerServer =>
        IsMultiplayerActive && NetworkManager.Singleton.IsServer;

    public static bool IsHostLobbyReady =>
        IsMultiplayerServer
        && NetworkManager.Singleton.ConnectedClientsIds.Count >= MinimumPlayersToStart;

    public static bool CanReturnToActiveQuiz =>
        SceneManager.GetActiveScene().name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase)
        && (
            (IsMultiplayerClientOnly && HasActiveQuizSelection)
            || (
                IsMultiplayerServer
                && IsHostLobbyReady
                && QuizMultiplayerCoordinator.HasSavedQuizSession
            )
        );

    public static event Action<string> StatusChanged;

    public readonly struct AvailableLobby
    {
        public readonly string Code;
        public readonly string HostName;
        public readonly string ActiveQuizLabel;
        public readonly int PlayerCount;
        public readonly int MaxPlayers;
        public readonly IReadOnlyList<string> TakenNames;
        public readonly IReadOnlyList<string> TakenColors;

        public AvailableLobby(
            string code,
            string hostName,
            int playerCount,
            int maxPlayers,
            string activeQuizLabel = null,
            IReadOnlyList<string> takenNames = null,
            IReadOnlyList<string> takenColors = null
        )
        {
            Code = code ?? string.Empty;
            HostName = NormalizeNickname(hostName);
            ActiveQuizLabel = string.IsNullOrWhiteSpace(activeQuizLabel)
                ? string.Empty
                : activeQuizLabel.Trim();
            PlayerCount = Mathf.Max(0, playerCount);
            MaxPlayers = Mathf.Max(1, maxPlayers);
            TakenNames = takenNames ?? Array.Empty<string>();
            TakenColors = takenColors ?? Array.Empty<string>();
        }
    }

    public readonly struct LobbyMemberInfo
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string ColorHex;
        public readonly bool IsLocalPlayer;

        public LobbyMemberInfo(string id, string name, string colorHex, bool isLocalPlayer)
        {
            Id = id ?? string.Empty;
            Name = NormalizeNickname(name);
            ColorHex = NormalizeColorHex(colorHex);
            IsLocalPlayer = isLocalPlayer;
        }
    }

    public static async Task<string> StartHostLobbyAsync(
        int generation,
        string typeFilter = null,
        string nickname = null,
        string colorHex = null
    )
    {
        var manager = await PrepareNetworkManagerForNewSessionAsync();
        QuizMultiplayerCoordinator.ClearSavedQuizSession();

        PlayerNickname = NormalizeNickname(nickname);
        PlayerColorHex = NormalizeColorHex(colorHex);
        await EnsureServicesReadyAsync(BuildAuthProfile(HostProfilePrefix, PlayerNickname));

        var allocation = await RelayService.Instance.CreateAllocationAsync(MaxRemotePlayers);
        RelayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        JoinCode = await CreateLobbyForRelayAsync(RelayJoinCode, PlayerNickname, PlayerColorHex);

        var transport = manager.GetComponent<UnityTransport>();
        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));

        GameSettings.MultiplayerMode = QuizMultiplayerMode.Host;
        GameSettings.MultiplayerJoinCode = JoinCode;
        GameSettings.MultiplayerNickname = PlayerNickname;
        ApplyQuizSettings(generation, typeFilter);

        if (!manager.StartHost())
            throw new InvalidOperationException("Could not start Netcode host.");

        QuizMultiplayerChatOverlay.Ensure();
        StatusChanged?.Invoke("Co-op lobby | Players: 1");

        return JoinCode;
    }

    public static async Task<string> StartHostAndLoadQuizAsync(
        int generation,
        string typeFilter = null,
        string nickname = null,
        string colorHex = null
    )
    {
        var joinCode = await StartHostLobbyAsync(generation, typeFilter, nickname, colorHex);
        await LoadHostedQuizAsync(generation, typeFilter);
        return joinCode;
    }

    public static async Task<bool> TryHandleMenuQuizSelectionAsync(
        int generation,
        string typeFilter = null
    )
    {
        if (!IsMultiplayerActive)
            return false;

        if (IsMultiplayerClientOnly)
        {
            StatusChanged?.Invoke("Waiting for host to choose a quiz...");
            return true;
        }

        if (!IsMultiplayerServer)
            return true;

        try
        {
            await LoadHostedQuizAsync(generation, typeFilter);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            StatusChanged?.Invoke($"Start failed: {ReadableError(ex)}");
        }

        return true;
    }

    public static async Task LoadHostedQuizAsync(
        int generation = 0,
        string typeFilter = null,
        bool restoreSavedSession = false
    )
    {
        var manager = NetworkManager.Singleton;
        if (!manager || !manager.IsServer)
            throw new InvalidOperationException("Only the co-op host can start the quiz.");

        if (restoreSavedSession)
        {
            if (!QuizMultiplayerCoordinator.QueueSavedQuizSessionRestore(generation, typeFilter))
                throw new InvalidOperationException("No saved co-op quiz is available.");
        }
        else
        {
            QuizMultiplayerCoordinator.QueueSavedQuizSessionRestore(generation, typeFilter);
        }

        ApplyQuizSettings(generation, typeFilter);
        _ = UpdateLobbyActiveQuizAsync(DescribeQuiz(generation, typeFilter));
        BroadcastQuizSelection(manager, generation, typeFilter);
        StatusChanged?.Invoke($"Starting {DescribeQuiz(generation, typeFilter)} co-op...");

        await Task.Yield();
        GameSettings.ArmQuizLaunch();
        if (manager.SceneManager != null)
            manager.SceneManager.LoadScene("Quiz", LoadSceneMode.Single);
        else
            SceneManager.LoadScene("Quiz");
    }

    public static async Task StartClientAsync(
        string joinCode,
        string nickname = null,
        string colorHex = null
    )
    {
        if (string.IsNullOrWhiteSpace(joinCode))
            throw new ArgumentException("Lobby selection is required.", nameof(joinCode));
        if (!IsFourDigitCode(joinCode))
            throw new ArgumentException("Selected lobby is invalid.", nameof(joinCode));

        PlayerNickname = NormalizeNickname(nickname);
        PlayerColorHex = NormalizeColorHex(colorHex);

        var manager = await PrepareNetworkManagerForNewSessionAsync();

        await EnsureServicesReadyAsync(BuildAuthProfile(ClientProfilePrefix, PlayerNickname));

        JoinCode = joinCode.Trim().ToUpperInvariant();
        RelayJoinCode = await JoinLobbyAndGetRelayCodeAsync(
            JoinCode,
            PlayerNickname,
            PlayerColorHex
        );
        var allocation = await RelayService.Instance.JoinAllocationAsync(RelayJoinCode);

        var transport = manager.GetComponent<UnityTransport>();
        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));

        GameSettings.MultiplayerMode = QuizMultiplayerMode.Client;
        GameSettings.MultiplayerJoinCode = JoinCode;
        GameSettings.MultiplayerNickname = PlayerNickname;
        GameSettings.Generation = 0;
        GameSettings.TypeFilter = null;
        GameSettings.TypeBgColor = null;

        if (!manager.StartClient())
            throw new InvalidOperationException("Could not start Netcode client.");

        RegisterQuizSelectionHandler(manager);
        QuizMultiplayerChatOverlay.Ensure();
        StatusChanged?.Invoke("Joined co-op. Waiting for host...");
    }

    public static async Task<List<AvailableLobby>> FindAvailableLobbiesAsync(
        string nickname = null,
        int maxResults = 10
    )
    {
        PlayerNickname = NormalizeNickname(nickname);
        await EnsureServicesReadyAsync(BuildAuthProfile(ClientProfilePrefix, PlayerNickname));

        var response = await QueryOpenPkmnquizLobbiesAsync(Mathf.Clamp(maxResults, 1, 25));
        var results = new List<AvailableLobby>();
        if (response?.Results == null)
            return results;

        foreach (var lobby in response.Results)
        {
            string code = GetLobbyData(lobby, LobbyCodeKey);
            if (!IsFourDigitCode(code))
                continue;

            string hostName = GetLobbyData(lobby, LobbyHostNameKey);
            if (string.IsNullOrWhiteSpace(hostName))
                hostName = GetHostPlayerName(lobby);

            int maxPlayers = lobby.MaxPlayers > 0 ? lobby.MaxPlayers : MaxPlayerCount;
            int playerCount = Mathf.Clamp(maxPlayers - lobby.AvailableSlots, 0, maxPlayers);
            if (playerCount == 0 && lobby.Players != null)
                playerCount = Mathf.Clamp(lobby.Players.Count, 0, maxPlayers);

            GetTakenLobbyPlayerData(lobby, hostName, out var takenNames, out var takenColors);
            string activeQuizLabel = GetLobbyData(lobby, LobbyActiveQuizKey);
            results.Add(
                new AvailableLobby(
                    code,
                    hostName,
                    playerCount,
                    maxPlayers,
                    activeQuizLabel,
                    takenNames,
                    takenColors
                )
            );
        }

        return results;
    }

    public static async Task UpdateCurrentLobbyPlayerAsync(string nickname, string colorHex)
    {
        if (string.IsNullOrEmpty(LobbyId))
            return;

        PlayerNickname = NormalizeNickname(nickname);
        PlayerColorHex = NormalizeColorHex(colorHex);

        string playerId = GetSignedInPlayerId();
        if (string.IsNullOrEmpty(playerId))
            return;

        await LobbyService.Instance.UpdatePlayerAsync(
            LobbyId,
            playerId,
            new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {
                        PlayerNameKey,
                        new PlayerDataObject(
                            PlayerDataObject.VisibilityOptions.Public,
                            PlayerNickname
                        )
                    },
                    {
                        PlayerColorKey,
                        new PlayerDataObject(
                            PlayerDataObject.VisibilityOptions.Public,
                            PlayerColorHex
                        )
                    },
                },
            }
        );
    }

    public static async Task<List<LobbyMemberInfo>> GetCurrentLobbyMembersAsync()
    {
        var members = new List<LobbyMemberInfo>();
        if (string.IsNullOrEmpty(LobbyId))
            return members;

        var lobby = await LobbyService.Instance.GetLobbyAsync(LobbyId);
        if (lobby?.Players == null)
            return members;

        string localPlayerId = GetSignedInPlayerId();
        foreach (var player in lobby.Players)
        {
            if (player == null)
                continue;

            string fallbackName =
                player.Id == lobby.HostId ? GetLobbyData(lobby, LobbyHostNameKey) : "Player";
            string name = GetPlayerName(player, fallbackName);
            string colorHex = GetPlayerColor(player, DefaultPlayerColor);
            bool isLocalPlayer =
                !string.IsNullOrEmpty(localPlayerId)
                && string.Equals(player.Id, localPlayerId, StringComparison.Ordinal);
            members.Add(new LobbyMemberInfo(player.Id, name, colorHex, isLocalPlayer));
        }

        return members;
    }

    public static void Shutdown()
    {
        var manager = NetworkManager.Singleton;
        if (manager && manager.CustomMessagingManager != null)
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(QuizSelectionMessage);

        QuizMultiplayerChatOverlay.ResetSession();
        QuizMultiplayerCoordinator.ClearSavedQuizSession();

        bool wasHost = manager && manager.IsServer;
        string lobbyToDelete = LobbyId;
        string playerId = GetSignedInPlayerId();

        if (manager && manager.IsListening)
            manager.Shutdown();

        if (wasHost && !string.IsNullOrEmpty(lobbyToDelete))
            _ = TryDeleteLobbyAsync(lobbyToDelete);
        else if (!string.IsNullOrEmpty(lobbyToDelete) && !string.IsNullOrEmpty(playerId))
            _ = TryRemovePlayerAsync(lobbyToDelete, playerId);

        QuizLobbyHeartbeat.Clear();
        JoinCode = null;
        RelayJoinCode = null;
        LobbyId = null;
        PlayerNickname = "Player";
        PlayerColorHex = DefaultPlayerColor;
        ActiveQuizGeneration = 0;
        ActiveQuizTypeFilter = null;
        HasActiveQuizSelection = false;
        GameSettings.ClearMultiplayer();
        StatusChanged?.Invoke(null);
    }

    public static void ReturnToLobbyMenu(bool keepActiveQuizSelection = false)
    {
        if (!IsMultiplayerActive)
        {
            Shutdown();
            return;
        }

        GameSettings.Generation = null;
        GameSettings.TypeFilter = null;
        GameSettings.TypeBgColor = null;
        GameSettings.MultiplayerJoinCode = JoinCode ?? GameSettings.MultiplayerJoinCode;
        GameSettings.MultiplayerNickname = PlayerNickname;
        if (IsMultiplayerServer)
            _ = UpdateLobbyActiveQuizAsync(string.Empty);
        if (!keepActiveQuizSelection)
        {
            ActiveQuizGeneration = 0;
            ActiveQuizTypeFilter = null;
            HasActiveQuizSelection = false;
        }
        StatusChanged?.Invoke(CurrentLobbyStatus());
    }

    public static async Task<bool> ReturnToActiveQuizAsync()
    {
        if (!CanReturnToActiveQuiz)
        {
            StatusChanged?.Invoke("No active co-op quiz to return to.");
            return false;
        }

        if (
            IsMultiplayerServer
            && QuizMultiplayerCoordinator.TryGetSavedQuizSelection(
                out int savedGeneration,
                out string savedTypeFilter
            )
        )
        {
            try
            {
                StatusChanged?.Invoke("Returning to saved co-op quiz...");
                await LoadHostedQuizAsync(
                    savedGeneration,
                    savedTypeFilter,
                    restoreSavedSession: true
                );
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                StatusChanged?.Invoke($"Return failed: {ReadableError(ex)}");
                return false;
            }
        }

        if (IsMultiplayerClientOnly && HasActiveQuizSelection)
        {
            ApplyQuizSettings(ActiveQuizGeneration, ActiveQuizTypeFilter);
            StatusChanged?.Invoke("Returning to co-op quiz...");
            GameSettings.ArmQuizLaunch();
            SceneManager.LoadScene("Quiz");
            return true;
        }

        StatusChanged?.Invoke("No active co-op quiz to return to.");
        return false;
    }

    private static async Task UpdateLobbyActiveQuizAsync(string activeQuizLabel)
    {
        if (string.IsNullOrEmpty(LobbyId) || !IsMultiplayerServer)
            return;

        try
        {
            await LobbyService.Instance.UpdateLobbyAsync(
                LobbyId,
                new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        {
                            LobbyActiveQuizKey,
                            new DataObject(
                                DataObject.VisibilityOptions.Public,
                                activeQuizLabel ?? string.Empty
                            )
                        },
                    },
                }
            );
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Co-op] Failed to update lobby quiz label: {ReadableError(ex)}");
        }
    }

    public static bool ReturnToActiveQuiz()
    {
        if (!CanReturnToActiveQuiz)
        {
            StatusChanged?.Invoke("No active co-op quiz to return to.");
            return false;
        }

        _ = ReturnToActiveQuizAsync();
        return true;
    }

    private static async Task<string> CreateLobbyForRelayAsync(
        string relayJoinCode,
        string nickname,
        string colorHex
    )
    {
        PlayerNickname = NormalizeNickname(nickname);
        PlayerColorHex = NormalizeColorHex(colorHex);
        var player = CreateLobbyPlayer(PlayerNickname, PlayerColorHex);

        for (int attempt = 0; attempt < 20; attempt++)
        {
            string visibleCode = UnityEngine.Random.Range(0, 10000).ToString("0000");
            if (await LobbyCodeExistsAsync(visibleCode))
                continue;

            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = player,
                Data = new Dictionary<string, DataObject>
                {
                    {
                        LobbyCodeKey,
                        new DataObject(
                            DataObject.VisibilityOptions.Public,
                            visibleCode,
                            DataObject.IndexOptions.S1
                        )
                    },
                    {
                        LobbyAppKey,
                        new DataObject(
                            DataObject.VisibilityOptions.Public,
                            LobbyAppValue,
                            DataObject.IndexOptions.S2
                        )
                    },
                    {
                        LobbyHostNameKey,
                        new DataObject(DataObject.VisibilityOptions.Public, PlayerNickname)
                    },
                    {
                        LobbyActiveQuizKey,
                        new DataObject(DataObject.VisibilityOptions.Public, string.Empty)
                    },
                    {
                        LobbyRelayKey,
                        new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode)
                    },
                },
            };

            var lobby = await LobbyService.Instance.CreateLobbyAsync(
                $"pkmnquiz-{visibleCode}",
                MaxPlayerCount,
                options
            );

            LobbyId = lobby.Id;
            QuizLobbyHeartbeat.Ensure(LobbyId);
            return visibleCode;
        }

        throw new InvalidOperationException("Could not create an open co-op lobby.");
    }

    private static async Task<string> JoinLobbyAndGetRelayCodeAsync(
        string visibleCode,
        string nickname,
        string colorHex
    )
    {
        PlayerNickname = NormalizeNickname(nickname);
        PlayerColorHex = NormalizeColorHex(colorHex);
        var lobby = await FindLobbyByVisibleCodeAsync(visibleCode);
        if (lobby == null)
            throw new InvalidOperationException("That co-op lobby is no longer available.");

        var player = CreateLobbyPlayer(PlayerNickname, PlayerColorHex);
        try
        {
            lobby = await LobbyService.Instance.JoinLobbyByIdAsync(
                lobby.Id,
                new JoinLobbyByIdOptions { Player = player }
            );
        }
        catch
        {
            if (!await TryRemoveCurrentPlayerFromLobbyAsync(lobby.Id))
                throw;

            lobby = await LobbyService.Instance.JoinLobbyByIdAsync(
                lobby.Id,
                new JoinLobbyByIdOptions { Player = player }
            );
        }

        LobbyId = lobby.Id;

        if (
            lobby.Data == null
            || !lobby.Data.TryGetValue(LobbyRelayKey, out var relayData)
            || string.IsNullOrWhiteSpace(relayData.Value)
        )
        {
            throw new InvalidOperationException("Lobby is missing Relay connection data.");
        }

        return relayData.Value;
    }

    private static async Task<bool> LobbyCodeExistsAsync(string visibleCode)
    {
        return await FindLobbyByVisibleCodeAsync(visibleCode) != null;
    }

    private static async Task<Lobby> FindLobbyByVisibleCodeAsync(string visibleCode)
    {
        var response = await QueryOpenPkmnquizLobbiesAsync(1, visibleCode);

        if (response?.Results == null || response.Results.Count == 0)
            return null;

        return response.Results[0];
    }

    private static Task<QueryResponse> QueryOpenPkmnquizLobbiesAsync(
        int count,
        string visibleCode = null
    )
    {
        var filters = new List<QueryFilter>
        {
            new QueryFilter(QueryFilter.FieldOptions.S2, LobbyAppValue, QueryFilter.OpOptions.EQ),
            new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
        };

        if (!string.IsNullOrWhiteSpace(visibleCode))
            filters.Add(
                new QueryFilter(
                    QueryFilter.FieldOptions.S1,
                    visibleCode.Trim(),
                    QueryFilter.OpOptions.EQ
                )
            );

        return LobbyService.Instance.QueryLobbiesAsync(
            new QueryLobbiesOptions { Count = count, Filters = filters }
        );
    }

    private static string GetLobbyData(Lobby lobby, string key)
    {
        if (lobby?.Data == null || string.IsNullOrEmpty(key))
            return string.Empty;

        return lobby.Data.TryGetValue(key, out var data)
            ? data?.Value ?? string.Empty
            : string.Empty;
    }

    private static string GetHostPlayerName(Lobby lobby)
    {
        if (lobby?.Players == null || lobby.Players.Count == 0)
            return "Host";

        Player host = null;
        if (!string.IsNullOrEmpty(lobby.HostId))
            host = lobby.Players.Find(player => player != null && player.Id == lobby.HostId);
        host ??= lobby.Players[0];

        return GetPlayerName(host, "Host");
    }

    private static void GetTakenLobbyPlayerData(
        Lobby lobby,
        string fallbackHostName,
        out List<string> names,
        out List<string> colors
    )
    {
        names = new List<string>();
        colors = new List<string>();

        if (lobby?.Players == null)
        {
            if (!string.IsNullOrWhiteSpace(fallbackHostName))
                names.Add(NormalizeNickname(fallbackHostName));
            return;
        }

        foreach (var player in lobby.Players)
        {
            if (player == null)
                continue;

            string fallback = player.Id == lobby.HostId ? fallbackHostName : "Player";
            names.Add(GetPlayerName(player, fallback));
            colors.Add(GetPlayerColor(player, DefaultPlayerColor));
        }
    }

    private static string GetPlayerName(Player player, string fallback)
    {
        if (
            player?.Data != null
            && player.Data.TryGetValue(PlayerNameKey, out var nameData)
            && !string.IsNullOrWhiteSpace(nameData?.Value)
        )
        {
            return NormalizeNickname(nameData.Value);
        }

        return NormalizeNickname(fallback);
    }

    private static string GetPlayerColor(Player player, string fallback)
    {
        if (
            player?.Data != null
            && player.Data.TryGetValue(PlayerColorKey, out var colorData)
            && !string.IsNullOrWhiteSpace(colorData?.Value)
        )
        {
            return NormalizeColorHex(colorData.Value);
        }

        return NormalizeColorHex(fallback);
    }

    private static Player CreateLobbyPlayer(string nickname, string colorHex)
    {
        return new Player(
            data: new Dictionary<string, PlayerDataObject>
            {
                {
                    PlayerNameKey,
                    new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, nickname)
                },
                {
                    PlayerColorKey,
                    new PlayerDataObject(
                        PlayerDataObject.VisibilityOptions.Public,
                        NormalizeColorHex(colorHex)
                    )
                },
            }
        );
    }

    public static string NormalizeNickname(string nickname)
    {
        nickname = string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname.Trim();
        if (nickname.Length > 14)
            nickname = nickname[..14];

        return nickname;
    }

    public static string SetPlayerColorHex(string colorHex)
    {
        PlayerColorHex = NormalizeColorHex(colorHex);
        return PlayerColorHex;
    }

    public static string NormalizeColorHex(string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
            return DefaultPlayerColor;

        colorHex = colorHex.Trim();
        if (!colorHex.StartsWith("#", StringComparison.Ordinal))
            colorHex = "#" + colorHex;

        if (!ColorUtility.TryParseHtmlString(colorHex, out _))
            return DefaultPlayerColor;

        return colorHex.Length >= 7
            ? colorHex.Substring(0, 7).ToUpperInvariant()
            : DefaultPlayerColor;
    }

    public static Color ColorFromHex(string colorHex)
    {
        if (ColorUtility.TryParseHtmlString(NormalizeColorHex(colorHex), out var color))
            return color;

        return Color.white;
    }

    public static string DefaultColorForClient(ulong clientId)
    {
        if (PlayerColorPalette.Length == 0)
            return DefaultPlayerColor;

        return PlayerColorPalette[(int)(clientId % (ulong)PlayerColorPalette.Length)];
    }

    private static bool IsFourDigitCode(string code)
    {
        code = code?.Trim();
        if (code == null || code.Length != 4)
            return false;

        for (int i = 0; i < code.Length; i++)
            if (!char.IsDigit(code[i]))
                return false;

        return true;
    }

    private static async Task TryDeleteLobbyAsync(string lobbyId)
    {
        try
        {
            await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
        }
        catch
        {
            // The lobby can already be gone if the service expires it.
        }
    }

    private static async Task TryRemovePlayerAsync(string lobbyId, string playerId)
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
        }
        catch
        {
            // The player can already be gone if the lobby expired or the host deleted it.
        }
    }

    private static async Task<bool> TryRemoveCurrentPlayerFromLobbyAsync(string lobbyId)
    {
        string playerId = GetSignedInPlayerId();
        if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(playerId))
            return false;

        try
        {
            var joinedLobbies = await LobbyService.Instance.GetJoinedLobbiesAsync();
            if (joinedLobbies == null || !joinedLobbies.Contains(lobbyId))
                return false;

            await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetSignedInPlayerId()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            return null;

        try
        {
            return AuthenticationService.Instance.IsSignedIn
                ? AuthenticationService.Instance.PlayerId
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void RegisterQuizSelectionHandler(NetworkManager manager)
    {
        if (!manager || manager.CustomMessagingManager == null)
            return;

        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            QuizSelectionMessage,
            OnQuizSelectionMessage
        );
    }

    private static void BroadcastQuizSelection(
        NetworkManager manager,
        int generation,
        string typeFilter
    )
    {
        if (!manager || manager.CustomMessagingManager == null)
            return;

        using var writer = new FastBufferWriter(QuizSelectionMessageSize, Allocator.Temp);
        writer.WriteValueSafe(generation);
        writer.WriteValueSafe(typeFilter ?? string.Empty);
        manager.CustomMessagingManager.SendNamedMessageToAll(QuizSelectionMessage, writer);
    }

    private static void OnQuizSelectionMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int generation);
        reader.ReadValueSafe(out string typeFilter);
        ApplyQuizSettings(generation, typeFilter);
        StatusChanged?.Invoke($"Host selected {DescribeQuiz(generation, typeFilter)}.");
    }

    private static void ApplyQuizSettings(int generation, string typeFilter)
    {
        ActiveQuizGeneration = generation;
        ActiveQuizTypeFilter = string.IsNullOrWhiteSpace(typeFilter)
            ? null
            : typeFilter.Trim().ToLowerInvariant();
        HasActiveQuizSelection = true;
        GameSettings.ArmQuizLaunch();

        if (string.IsNullOrWhiteSpace(typeFilter))
        {
            GameSettings.Generation = generation;
            GameSettings.TypeFilter = null;
            GameSettings.TypeBgColor = null;
            return;
        }

        GameSettings.Generation = null;
        GameSettings.TypeFilter = new[] { typeFilter.Trim().ToLowerInvariant() };
        GameSettings.TypeBgColor = null;
    }

    private static string DescribeQuiz(int generation, string typeFilter)
    {
        if (!string.IsNullOrWhiteSpace(typeFilter))
            return $"{ToTitleWord(typeFilter)} type quiz";
        if (generation == 0)
            return "full quiz";
        if (generation == 10)
            return "Mega Evolutions quiz";

        return $"Gen {generation} quiz";
    }

    private static string ToTitleWord(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "Type" : value.Trim().ToLowerInvariant();
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    private static string ReadableError(Exception ex)
    {
        if (ex == null)
            return "Unknown error";

        return string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
    }

    private static string CurrentLobbyStatus()
    {
        var manager = NetworkManager.Singleton;

        if (IsMultiplayerServer && manager)
        {
            int players = manager.ConnectedClientsIds.Count;
            return $"Co-op lobby | Players: {players}";
        }

        return "Joined co-op. Waiting for host...";
    }

    private static async Task EnsureServicesReadyAsync(string authProfile)
    {
        while (UnityServices.State == ServicesInitializationState.Initializing)
            await Task.Yield();

        if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync(
                new InitializationOptions().SetProfile(authProfile)
            );

        if (
            AuthenticationService.Instance.IsSignedIn
            && AuthenticationService.Instance.Profile != authProfile
        )
        {
            AuthenticationService.Instance.SignOut();
        }

        if (
            !AuthenticationService.Instance.IsSignedIn
            && AuthenticationService.Instance.Profile != authProfile
        )
        {
            AuthenticationService.Instance.SwitchProfile(authProfile);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private static string BuildAuthProfile(string prefix, string nickname)
    {
        nickname = NormalizeNickname(nickname).ToLowerInvariant();
        System.Text.StringBuilder sb = new(prefix);

        foreach (char ch in nickname)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch == '-' || ch == '_')
                sb.Append(ch);
        }

        if (sb.Length == prefix.Length)
            sb.Append("player");
        if (sb.Length > 30)
            sb.Length = 30;

        return sb.ToString();
    }

    private static NetworkManager EnsureNetworkManager()
    {
        var manager = NetworkManager.Singleton;
        if (!manager)
            manager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();

        if (!manager)
        {
            var go = new GameObject("NetworkManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var newTransport = go.AddComponent<UnityTransport>();
            manager = go.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = newTransport };
        }
        else
        {
            UnityEngine.Object.DontDestroyOnLoad(manager.gameObject);
        }

        var transport = manager.GetComponent<UnityTransport>();
        if (!transport)
            transport = manager.gameObject.AddComponent<UnityTransport>();

        if (manager.NetworkConfig == null)
            manager.NetworkConfig = new NetworkConfig();

        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.EnableSceneManagement = true;
        manager.NetworkConfig.PlayerPrefab = null;

        return manager;
    }

    private static async Task<NetworkManager> PrepareNetworkManagerForNewSessionAsync()
    {
        var manager = EnsureNetworkManager();
        if (manager.IsListening || manager.ShutdownInProgress)
        {
            QuizMultiplayerChatOverlay.ResetSession();
            manager.Shutdown();

            const int maxFrames = 120;
            int frames = 0;
            while (
                manager
                && (manager.IsListening || manager.ShutdownInProgress)
                && frames++ < maxFrames
            )
            {
                await Task.Yield();
            }

            if (manager && (manager.IsListening || manager.ShutdownInProgress))
                throw new InvalidOperationException("Netcode shutdown did not finish. Try again.");

            manager = EnsureNetworkManager();
        }

        ValidateNetworkManager(manager);
        return manager;
    }

    private static void ValidateNetworkManager(NetworkManager manager)
    {
        if (!manager)
            throw new InvalidOperationException("Netcode NetworkManager is missing.");

        if (manager.NetworkConfig == null)
            throw new InvalidOperationException("Netcode NetworkConfig is missing.");

        if (!manager.GetComponent<UnityTransport>())
            throw new InvalidOperationException("Netcode UnityTransport is missing.");
    }
}

public sealed class QuizLobbyHeartbeat : MonoBehaviour
{
    private const float HeartbeatInterval = 15f;
    private static QuizLobbyHeartbeat instance;
    private string lobbyId;
    private float nextHeartbeatTime;

    public static void Ensure(string lobbyId)
    {
        if (string.IsNullOrWhiteSpace(lobbyId))
            return;

        if (!instance)
        {
            var go = new GameObject("Quiz Lobby Heartbeat");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<QuizLobbyHeartbeat>();
        }

        instance.lobbyId = lobbyId;
        instance.nextHeartbeatTime = 0f;
    }

    public static void Clear()
    {
        if (instance)
            instance.lobbyId = null;
    }

    private async void Update()
    {
        if (string.IsNullOrEmpty(lobbyId) || Time.unscaledTime < nextHeartbeatTime)
            return;

        nextHeartbeatTime = Time.unscaledTime + HeartbeatInterval;

        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
        }
        catch
        {
            // The host can keep playing even if heartbeat fails once.
        }
    }
}
