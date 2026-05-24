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
    private const int MinimumPlayersToStart = 2;
    private const string ConnectionType = "dtls";
    private const string LobbyCodeKey = "code";
    private const string LobbyRelayKey = "relay";
    private const string LobbyAppKey = "app";
    private const string LobbyAppValue = "pkmnquiz";
    private const string LobbyHostNameKey = "host";
    private const string PlayerNameKey = "name";
    private const string QuizSelectionMessage = "pkmnquiz_quiz_selection";
    private const int QuizSelectionMessageSize = 256;
    private const string HostProfilePrefix = "pkmn_host";
    private const string ClientProfilePrefix = "pkmn_join";

    public static string JoinCode { get; private set; }
    public static string RelayJoinCode { get; private set; }
    public static string PlayerNickname { get; private set; } = "Player";
    public static string LobbyId { get; private set; }
    public static int MaxPlayerCount => MaxRemotePlayers + 1;
    public static int RequiredPlayerCount => MinimumPlayersToStart;

    public static bool IsMultiplayerActive =>
        GameSettings.IsMultiplayer && NetworkManager.Singleton && NetworkManager.Singleton.IsListening;

    public static bool IsMultiplayerClientOnly =>
        IsMultiplayerActive && NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer;

    public static bool IsMultiplayerServer =>
        IsMultiplayerActive && NetworkManager.Singleton.IsServer;

    public static bool IsHostLobbyReady =>
        IsMultiplayerServer
        && NetworkManager.Singleton.ConnectedClientsIds.Count >= MinimumPlayersToStart;

    public static event Action<string> StatusChanged;

    public readonly struct AvailableLobby
    {
        public readonly string Code;
        public readonly string HostName;
        public readonly int PlayerCount;
        public readonly int MaxPlayers;

        public AvailableLobby(string code, string hostName, int playerCount, int maxPlayers)
        {
            Code = code ?? string.Empty;
            HostName = NormalizeNickname(hostName);
            PlayerCount = Mathf.Max(0, playerCount);
            MaxPlayers = Mathf.Max(1, maxPlayers);
        }
    }

    public readonly struct LobbyMemberInfo
    {
        public readonly string Id;
        public readonly string Name;
        public readonly bool IsLocalPlayer;

        public LobbyMemberInfo(string id, string name, bool isLocalPlayer)
        {
            Id = id ?? string.Empty;
            Name = NormalizeNickname(name);
            IsLocalPlayer = isLocalPlayer;
        }
    }

    public static async Task<string> StartHostLobbyAsync(
        int generation,
        string typeFilter = null,
        string nickname = null
    )
    {
        var manager = EnsureNetworkManager();
        if (manager.IsListening)
        {
            QuizMultiplayerChatOverlay.ResetSession();
            manager.Shutdown();
            await Task.Yield();
        }

        PlayerNickname = NormalizeNickname(nickname);
        await EnsureServicesReadyAsync(BuildAuthProfile(HostProfilePrefix, PlayerNickname));

        var allocation = await RelayService.Instance.CreateAllocationAsync(MaxRemotePlayers);
        RelayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        JoinCode = await CreateLobbyForRelayAsync(RelayJoinCode, PlayerNickname);

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
        string nickname = null
    )
    {
        var joinCode = await StartHostLobbyAsync(generation, typeFilter, nickname);
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

        if (!IsHostLobbyReady)
        {
            StatusChanged?.Invoke("Wait for another player, then choose a quiz.");
            return true;
        }

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

    public static async Task LoadHostedQuizAsync(int generation = 0, string typeFilter = null)
    {
        var manager = NetworkManager.Singleton;
        if (!manager || !manager.IsServer)
            throw new InvalidOperationException("Only the co-op host can start the quiz.");
        if (manager.ConnectedClientsIds.Count < MinimumPlayersToStart)
            throw new InvalidOperationException("Wait for another player before choosing a quiz.");

        ApplyQuizSettings(generation, typeFilter);
        BroadcastQuizSelection(manager, generation, typeFilter);
        StatusChanged?.Invoke($"Starting {DescribeQuiz(generation, typeFilter)} co-op...");

        await Task.Yield();
        if (manager.SceneManager != null)
            manager.SceneManager.LoadScene("Quiz", LoadSceneMode.Single);
        else
            SceneManager.LoadScene("Quiz");
    }

    public static async Task StartClientAsync(string joinCode, string nickname = null)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
            throw new ArgumentException("Lobby selection is required.", nameof(joinCode));
        if (!IsFourDigitCode(joinCode))
            throw new ArgumentException("Selected lobby is invalid.", nameof(joinCode));

        PlayerNickname = NormalizeNickname(nickname);

        var manager = EnsureNetworkManager();
        if (manager.IsListening)
        {
            QuizMultiplayerChatOverlay.ResetSession();
            manager.Shutdown();
            await Task.Yield();
        }

        await EnsureServicesReadyAsync(BuildAuthProfile(ClientProfilePrefix, PlayerNickname));

        JoinCode = joinCode.Trim().ToUpperInvariant();
        RelayJoinCode = await JoinLobbyAndGetRelayCodeAsync(JoinCode, PlayerNickname);
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

            results.Add(new AvailableLobby(code, hostName, playerCount, maxPlayers));
        }

        return results;
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
            bool isLocalPlayer =
                !string.IsNullOrEmpty(localPlayerId)
                && string.Equals(player.Id, localPlayerId, StringComparison.Ordinal);
            members.Add(new LobbyMemberInfo(player.Id, name, isLocalPlayer));
        }

        return members;
    }

    public static void Shutdown()
    {
        var manager = NetworkManager.Singleton;
        if (manager && manager.CustomMessagingManager != null)
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(QuizSelectionMessage);

        QuizMultiplayerChatOverlay.ResetSession();

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
        GameSettings.ClearMultiplayer();
        StatusChanged?.Invoke(null);
    }

    public static void ReturnToLobbyMenu()
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
        StatusChanged?.Invoke(CurrentLobbyStatus());
    }

    private static async Task<string> CreateLobbyForRelayAsync(string relayJoinCode, string nickname)
    {
        PlayerNickname = NormalizeNickname(nickname);
        var player = CreateLobbyPlayer(PlayerNickname);

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

    private static async Task<string> JoinLobbyAndGetRelayCodeAsync(string visibleCode, string nickname)
    {
        PlayerNickname = NormalizeNickname(nickname);
        var lobby = await FindLobbyByVisibleCodeAsync(visibleCode);
        if (lobby == null)
            throw new InvalidOperationException("That co-op lobby is no longer available.");

        var player = CreateLobbyPlayer(PlayerNickname);
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

        return lobby.Data.TryGetValue(key, out var data) ? data?.Value ?? string.Empty : string.Empty;
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

    private static Player CreateLobbyPlayer(string nickname)
    {
        return new Player(
            data: new Dictionary<string, PlayerDataObject>
            {
                {
                    PlayerNameKey,
                    new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, nickname)
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
            await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(authProfile));

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
