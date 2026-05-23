using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class QuizMultiplayerCoordinator : MonoBehaviour
{
    private const string GuessMessage = "pkmnquiz_guess";
    private const string SolvedMessage = "pkmnquiz_solved";
    private const string ActionRequestMessage = "pkmnquiz_action_request";
    private const string ActionMessage = "pkmnquiz_action";
    private const string NicknameMessage = "pkmnquiz_nickname";
    private const string ScoreboardMessage = "pkmnquiz_scoreboard";
    private const string StateRequestMessage = "pkmnquiz_state_request";
    private const string StateMessage = "pkmnquiz_state";
    private const string TimerMessage = "pkmnquiz_timer";
    private const int MessageSize = 16384;
    private const float TimerSyncInterval = 0.25f;
    private const string HostPlayerColor = "#6FEA72";
    private const string RemotePlayerColor = "#FFD84D";
    private static readonly Color HostEndStateColor = new(0f, 1f, 0f, 1f);
    private static readonly Color RemoteEndStateColor = new(1f, 0.85f, 0f, 1f);
    private static readonly Color MissedEndStateColor = new(1f, 0f, 0f, 1f);

    private static QuizMultiplayerCoordinator instance;
    private static List<PlayerScore> latestScoreboard = new();

    private QuizManager quiz;
    private NetworkManager manager;
    private bool registered;
    private bool stateReceived;
    private bool returningToMenu;
    private float nextStateRequest;
    private float nextTimerSync;
    private readonly Dictionary<ulong, string> playerNames = new();
    private readonly Dictionary<ulong, int> playerScores = new();
    private readonly Dictionary<ulong, int> playerTypeHints = new();
    private readonly Dictionary<ulong, int> playerShadows = new();
    private readonly Dictionary<int, ulong> solvedByClientId = new();

    public static bool IsActive => QuizNetworkRuntime.IsMultiplayerActive;
    public static bool IsClientOnly => QuizNetworkRuntime.IsMultiplayerClientOnly;

    public static Color GetEndStateColorForPokemon(int pokemonId, bool guessed)
    {
        if (!guessed)
            return MissedEndStateColor;

        if (
            QuizNetworkRuntime.IsMultiplayerActive
            && instance
            && instance.solvedByClientId.TryGetValue(pokemonId, out var solverClientId)
        )
        {
            return solverClientId == 0UL ? HostEndStateColor : RemoteEndStateColor;
        }

        return HostEndStateColor;
    }

    public static void Attach(QuizManager quizManager)
    {
        if (!quizManager || !QuizNetworkRuntime.IsMultiplayerActive)
            return;

        if (!instance)
        {
            var go = new GameObject("Quiz Multiplayer Coordinator");
            instance = go.AddComponent<QuizMultiplayerCoordinator>();
        }

        instance.SetQuiz(quizManager);
        QuizMultiplayerStatusOverlay.Ensure();
        QuizMultiplayerChatOverlay.Ensure();
    }

    public static void SubmitGuess(string currentText)
    {
        if (!instance || !QuizNetworkRuntime.IsMultiplayerActive)
            return;

        instance.SubmitGuessInternal(currentText);
    }

    public static bool RequestPause(bool paused) =>
        RequestAction(paused ? CoopAction.Pause : CoopAction.Resume);

    public static bool RequestRevealShadow() => RequestAction(CoopAction.RevealShadow);

    public static bool RequestRevealType() => RequestAction(CoopAction.RevealType);

    public static bool RequestReset() => RequestAction(CoopAction.Reset);

    public static bool RequestGiveUp() => RequestAction(CoopAction.GiveUp);

    public static bool RequestReturnToMenu() => RequestAction(CoopAction.ReturnToMenu);

    private static bool RequestAction(CoopAction action)
    {
        if (!QuizNetworkRuntime.IsMultiplayerActive)
            return false;

        if (!instance)
            Attach(FindFirstObjectByType<QuizManager>());

        if (!instance)
            return true;

        instance.RequestActionInternal(action);
        return true;
    }

    private void SetQuiz(QuizManager quizManager)
    {
        quiz = quizManager;
        manager = NetworkManager.Singleton;
        RegisterHandlers();

        if (manager && manager.IsServer)
        {
            EnsurePlayer(manager.LocalClientId, QuizNetworkRuntime.PlayerNickname);
            BroadcastScoreboard();
        }
        else if (manager && manager.IsClient)
        {
            SendNickname();
            SendStateRequest();
        }
    }

    private void RegisterHandlers()
    {
        if (registered || !manager || manager.CustomMessagingManager == null)
            return;

        if (manager.IsServer)
        {
            manager.CustomMessagingManager.RegisterNamedMessageHandler(
                GuessMessage,
                OnGuessMessage
            );
            manager.CustomMessagingManager.RegisterNamedMessageHandler(
                ActionRequestMessage,
                OnActionRequestMessage
            );
            manager.CustomMessagingManager.RegisterNamedMessageHandler(
                StateRequestMessage,
                OnStateRequestMessage
            );
            manager.CustomMessagingManager.RegisterNamedMessageHandler(
                NicknameMessage,
                OnNicknameMessage
            );
            manager.OnClientConnectedCallback += OnClientConnected;
        }

        manager.OnClientDisconnectCallback += OnClientDisconnected;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(ActionMessage, OnActionMessage);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(SolvedMessage, OnSolvedMessage);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            ScoreboardMessage,
            OnScoreboardMessage
        );
        manager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, OnStateMessage);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(TimerMessage, OnTimerMessage);
        registered = true;
    }

    private void OnDestroy()
    {
        if (registered && manager && manager.CustomMessagingManager != null)
        {
            if (manager.IsServer)
            {
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(GuessMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(ActionRequestMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(StateRequestMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(NicknameMessage);
                manager.OnClientConnectedCallback -= OnClientConnected;
            }

            manager.OnClientDisconnectCallback -= OnClientDisconnected;
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(ActionMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(SolvedMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(ScoreboardMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(TimerMessage);
        }

        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        if (!quiz || !manager || !manager.IsListening)
            return;

        if (manager.IsClient && !manager.IsServer && !stateReceived)
        {
            if (Time.unscaledTime >= nextStateRequest)
            {
                SendNickname();
                SendStateRequest();
                nextStateRequest = Time.unscaledTime + 1f;
            }
        }

        if (!manager.IsServer || Time.unscaledTime < nextTimerSync)
            return;

        BroadcastTimer();
        nextTimerSync = Time.unscaledTime + TimerSyncInterval;
    }

    private void SubmitGuessInternal(string currentText)
    {
        if (string.IsNullOrWhiteSpace(currentText) || !manager || !manager.IsListening)
            return;

        if (manager.IsServer)
        {
            HandleServerGuess(manager.LocalClientId, currentText, suppressLocalInput: false);
            return;
        }

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        writer.WriteValueSafe(currentText);
        manager.CustomMessagingManager.SendNamedMessage(GuessMessage, 0UL, writer);
    }

    private void RequestActionInternal(CoopAction action)
    {
        if (!manager || !manager.IsListening)
            return;

        if (manager.IsServer)
        {
            HandleServerAction(manager.LocalClientId, action);
            return;
        }

        using var writer = new FastBufferWriter(16, Allocator.Temp);
        writer.WriteValueSafe((int)action);
        manager.CustomMessagingManager.SendNamedMessage(ActionRequestMessage, 0UL, writer);
    }

    private void OnGuessMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string currentText);
        HandleServerGuess(senderClientId, currentText, suppressLocalInput: true);
    }

    private void HandleServerGuess(
        ulong senderClientId,
        string currentText,
        bool suppressLocalInput
    )
    {
        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (!quiz)
            return;

        var solvedIds = quiz.AcceptNetworkGuessOnServer(currentText, suppressLocalInput);
        if (solvedIds.Count == 0)
            return;

        EnsurePlayer(senderClientId, null);
        playerScores[senderClientId] += solvedIds.Count;
        RecordSolvedBy(solvedIds, senderClientId);

        BroadcastSolved(solvedIds, senderClientId);
        BroadcastScoreboard();
        quiz.RefreshMultiplayerFinishedDialog(gaveUp: false);
    }

    private void OnActionRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int actionValue);
        HandleServerAction(senderClientId, (CoopAction)actionValue);
    }

    private void HandleServerAction(ulong senderClientId, CoopAction action)
    {
        if (!manager || !manager.IsServer)
            return;

        if (action == CoopAction.ReturnToMenu)
        {
            BroadcastAction(action, 0);
            BeginReturnToMenu(0.15f);
            return;
        }

        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (!quiz)
            return;

        int payload = 0;
        switch (action)
        {
            case CoopAction.Pause:
                quiz.ApplyNetworkPause(paused: true, quiz.ElapsedSeconds);
                break;
            case CoopAction.Resume:
                quiz.ApplyNetworkPause(paused: false, quiz.ElapsedSeconds);
                break;
            case CoopAction.RevealShadow:
                payload = quiz.ApplyNetworkRevealShadow();
                if (payload == 0)
                    return;
                EnsurePlayer(senderClientId, null);
                playerShadows[senderClientId]++;
                break;
            case CoopAction.RevealType:
                payload = quiz.ApplyNetworkRevealType();
                if (payload == 0)
                    return;
                EnsurePlayer(senderClientId, null);
                playerTypeHints[senderClientId]++;
                break;
            case CoopAction.Reset:
                quiz.ApplyNetworkReset();
                ResetScores();
                solvedByClientId.Clear();
                break;
            case CoopAction.GiveUp:
                quiz.ApplyNetworkGiveUp();
                break;
            default:
                return;
        }

        BroadcastAction(action, payload);

        if (action == CoopAction.Reset)
            BroadcastScoreboard();
        else if (action == CoopAction.GiveUp)
            quiz.RefreshMultiplayerFinishedDialog(gaveUp: true);
    }

    private void BroadcastAction(CoopAction action, int payload)
    {
        var scoreboard = BuildScoreboard();
        ApplyScoreboard(scoreboard);

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        writer.WriteValueSafe((int)action);
        writer.WriteValueSafe(payload);
        writer.WriteValueSafe(quiz ? quiz.ElapsedSeconds : 0f);
        writer.WriteValueSafe(quiz && quiz.IsQuizRunning);
        WriteScoreboard(writer, scoreboard);
        manager.CustomMessagingManager.SendNamedMessageToAll(ActionMessage, writer);
    }

    private void OnActionMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (manager && manager.IsServer)
            return;

        reader.ReadValueSafe(out int actionValue);
        reader.ReadValueSafe(out int payload);
        reader.ReadValueSafe(out float elapsed);
        reader.ReadValueSafe(out bool isRunning);
        var scoreboard = ReadScoreboard(reader);

        ApplyScoreboard(scoreboard);
        ApplyAction((CoopAction)actionValue, payload, elapsed, isRunning);
    }

    private void ApplyAction(CoopAction action, int payload, float elapsed, bool isRunning)
    {
        if (action == CoopAction.ReturnToMenu)
        {
            BeginReturnToMenu(0f);
            return;
        }

        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (!quiz)
            return;

        switch (action)
        {
            case CoopAction.Pause:
                quiz.ApplyNetworkPause(paused: true, elapsed);
                break;
            case CoopAction.Resume:
                quiz.ApplyNetworkPause(paused: false, elapsed);
                break;
            case CoopAction.RevealShadow:
                quiz.ApplyNetworkShadow(payload);
                break;
            case CoopAction.RevealType:
                quiz.ApplyNetworkTypeHint(payload);
                break;
            case CoopAction.Reset:
                solvedByClientId.Clear();
                quiz.ApplyNetworkReset();
                break;
            case CoopAction.GiveUp:
                quiz.ApplyNetworkGiveUp();
                break;
        }
    }

    private void BroadcastSolved(IReadOnlyCollection<int> solvedIds, ulong solverClientId)
    {
        var scoreboard = BuildScoreboard();
        ApplyScoreboard(scoreboard);

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        WriteSolvedPayload(writer, solvedIds, solverClientId, scoreboard);
        manager.CustomMessagingManager.SendNamedMessageToAll(SolvedMessage, writer);
    }

    private void OnSolvedMessage(ulong senderClientId, FastBufferReader reader)
    {
        var solvedIds = ReadSolvedPayload(reader, out var solverClientId, out var scoreboard);
        ApplyScoreboard(scoreboard);
        RecordSolvedBy(solvedIds, solverClientId);

        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (!quiz)
            return;

        bool solvedByLocalPlayer = manager && solverClientId == manager.LocalClientId;
        quiz.ApplyNetworkSolvedIds(solvedIds, clearInput: solvedByLocalPlayer, playSound: true);
    }

    private void RecordSolvedBy(IReadOnlyCollection<int> solvedIds, ulong solverClientId)
    {
        if (solvedIds == null)
            return;

        foreach (var id in solvedIds)
            solvedByClientId[id] = solverClientId;
    }

    private void SendStateRequest()
    {
        using var writer = new FastBufferWriter(1, Allocator.Temp);
        manager.CustomMessagingManager.SendNamedMessage(StateRequestMessage, 0UL, writer);
    }

    private void SendNickname()
    {
        if (!manager || !manager.IsClient || manager.IsServer)
            return;

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        writer.WriteValueSafe(QuizNetworkRuntime.PlayerNickname);
        manager.CustomMessagingManager.SendNamedMessage(NicknameMessage, 0UL, writer);
    }

    private void OnNicknameMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string nickname);
        EnsurePlayer(senderClientId, nickname);
        BroadcastScoreboard();
    }

    private void OnStateRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        SendStateToClient(senderClientId);
    }

    private void OnClientConnected(ulong clientId)
    {
        EnsurePlayer(clientId, null);
        BroadcastScoreboard();

        if (clientId != manager.LocalClientId)
            SendStateToClient(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!manager)
            return;

        if (manager.IsServer)
        {
            playerNames.Remove(clientId);
            playerScores.Remove(clientId);
            playerTypeHints.Remove(clientId);
            playerShadows.Remove(clientId);
            BroadcastScoreboard();
            return;
        }

        if (clientId == 0UL)
            BeginReturnToMenu(0f);
    }

    private void SendStateToClient(ulong clientId)
    {
        if (!quiz)
            return;

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        writer.WriteValueSafe(quiz.CurrentQuizGeneration);
        writer.WriteValueSafe(quiz.CurrentTypeFilter ?? string.Empty);
        writer.WriteValueSafe(quiz.ElapsedSeconds);
        writer.WriteValueSafe(quiz.IsQuizRunning);
        WriteIds(writer, quiz.SolvedIds);
        WriteScoreboard(writer, BuildScoreboard());
        WriteSolvedOwners(writer);
        manager.CustomMessagingManager.SendNamedMessage(StateMessage, clientId, writer);
    }

    private void OnStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int generation);
        reader.ReadValueSafe(out string typeFilter);
        reader.ReadValueSafe(out float elapsed);
        reader.ReadValueSafe(out bool isRunning);
        var solvedIds = ReadIds(reader);
        var scoreboard = ReadScoreboard(reader);
        var solvedOwners = ReadSolvedOwners(reader);
        stateReceived = true;
        solvedByClientId.Clear();
        foreach (var kv in solvedOwners)
            solvedByClientId[kv.Key] = kv.Value;

        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (!quiz)
            return;

        quiz.ApplyNetworkState(generation, typeFilter, solvedIds, elapsed, isRunning);
        ApplyScoreboard(scoreboard);
    }

    private void BroadcastTimer()
    {
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(quiz.ElapsedSeconds);
        writer.WriteValueSafe(quiz.IsQuizRunning);
        manager.CustomMessagingManager.SendNamedMessageToAll(TimerMessage, writer);
    }

    private void OnTimerMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!QuizNetworkRuntime.IsMultiplayerClientOnly)
            return;

        reader.ReadValueSafe(out float elapsed);
        reader.ReadValueSafe(out bool isRunning);

        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (quiz)
            quiz.ApplyNetworkTimer(elapsed, isRunning);
    }

    private void BroadcastScoreboard()
    {
        var scoreboard = BuildScoreboard();
        ApplyScoreboard(scoreboard);

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        WriteScoreboard(writer, scoreboard);
        manager.CustomMessagingManager.SendNamedMessageToAll(ScoreboardMessage, writer);
    }

    private void ResetScores()
    {
        var clientIds = new List<ulong>(playerScores.Keys);
        foreach (var clientId in clientIds)
            playerScores[clientId] = 0;

        clientIds = new List<ulong>(playerTypeHints.Keys);
        foreach (var clientId in clientIds)
            playerTypeHints[clientId] = 0;

        clientIds = new List<ulong>(playerShadows.Keys);
        foreach (var clientId in clientIds)
            playerShadows[clientId] = 0;
    }

    private void OnScoreboardMessage(ulong senderClientId, FastBufferReader reader)
    {
        ApplyScoreboard(ReadScoreboard(reader));
    }

    private void EnsurePlayer(ulong clientId, string nickname)
    {
        if (!playerNames.ContainsKey(clientId))
            playerNames[clientId] = DefaultPlayerName(clientId);

        if (!string.IsNullOrWhiteSpace(nickname))
            playerNames[clientId] = QuizNetworkRuntime.NormalizeNickname(nickname);

        if (!playerScores.ContainsKey(clientId))
            playerScores[clientId] = 0;
        if (!playerTypeHints.ContainsKey(clientId))
            playerTypeHints[clientId] = 0;
        if (!playerShadows.ContainsKey(clientId))
            playerShadows[clientId] = 0;
    }

    private List<PlayerScore> BuildScoreboard()
    {
        var scores = new List<PlayerScore>();
        var clientIds = new List<ulong>(playerNames.Keys);
        clientIds.Sort();

        foreach (var clientId in clientIds)
        {
            var score = playerScores.TryGetValue(clientId, out var count) ? count : 0;
            var typeHints = playerTypeHints.TryGetValue(clientId, out var hints) ? hints : 0;
            var shadows = playerShadows.TryGetValue(clientId, out var shadowCount)
                ? shadowCount
                : 0;
            scores.Add(new PlayerScore(clientId, playerNames[clientId], score, typeHints, shadows));
        }

        return scores;
    }

    private static void ApplyScoreboard(List<PlayerScore> scoreboard)
    {
        latestScoreboard = scoreboard == null
            ? new List<PlayerScore>()
            : new List<PlayerScore>(scoreboard);
        QuizMultiplayerStatusOverlay.SetScoreboard(FormatScoreboard(scoreboard));
    }

    public static string GetFinishedStatsText()
    {
        if (
            !QuizNetworkRuntime.IsMultiplayerActive
            || latestScoreboard == null
            || latestScoreboard.Count == 0
        )
        {
            return string.Empty;
        }

        System.Text.StringBuilder sb = new();
        sb.Append("Co-op stats:");
        for (int i = 0; i < latestScoreboard.Count; i++)
        {
            var score = latestScoreboard[i];
            sb.Append('\n');
            sb.Append(FormatColoredPlayerName(score.ClientId, score.Name));
            sb.Append(": ");
            sb.Append(score.Count);
            sb.Append(" guessed, ");
            sb.Append(score.TypeHints);
            sb.Append(" type hints, ");
            sb.Append(score.Shadows);
            sb.Append(" shadows");
        }

        return sb.ToString();
    }

    private static string FormatScoreboard(List<PlayerScore> scoreboard)
    {
        if (scoreboard == null || scoreboard.Count == 0)
            return string.Empty;

        System.Text.StringBuilder sb = new();
        for (int i = 0; i < scoreboard.Count; i++)
        {
            if (i > 0)
                sb.Append(" | ");

            sb.Append(FormatColoredPlayerName(scoreboard[i].ClientId, scoreboard[i].Name));
            sb.Append(": ");
            sb.Append(scoreboard[i].Count);
        }

        return sb.ToString();
    }

    public static string FormatColoredPlayerName(ulong clientId, string name)
    {
        return $"<color={PlayerNameColor(clientId)}>{EscapeRichText(name)}</color>";
    }

    public static string EscapeRichText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string PlayerNameColor(ulong clientId)
    {
        return clientId == 0UL ? HostPlayerColor : RemotePlayerColor;
    }

    private static string DefaultPlayerName(ulong clientId)
    {
        return clientId == 0UL ? "Player 1" : "Player 2";
    }

    private static void WriteSolvedPayload(
        FastBufferWriter writer,
        IReadOnlyCollection<int> solvedIds,
        ulong solverClientId,
        IReadOnlyList<PlayerScore> scoreboard
    )
    {
        writer.WriteValueSafe(solverClientId);
        WriteIds(writer, solvedIds);
        WriteScoreboard(writer, scoreboard);
    }

    private static List<int> ReadSolvedPayload(
        FastBufferReader reader,
        out ulong solverClientId,
        out List<PlayerScore> scoreboard
    )
    {
        reader.ReadValueSafe(out solverClientId);
        var ids = ReadIds(reader);
        scoreboard = ReadScoreboard(reader);
        return ids;
    }

    private static void WriteIds(FastBufferWriter writer, IReadOnlyCollection<int> ids)
    {
        writer.WriteValueSafe(ids.Count);
        foreach (var id in ids)
            writer.WriteValueSafe(id);
    }

    private static void WriteScoreboard(FastBufferWriter writer, IReadOnlyList<PlayerScore> scores)
    {
        int count = scores?.Count ?? 0;
        writer.WriteValueSafe(count);
        for (int i = 0; i < count; i++)
        {
            writer.WriteValueSafe(scores[i].ClientId);
            writer.WriteValueSafe(scores[i].Name);
            writer.WriteValueSafe(scores[i].Count);
            writer.WriteValueSafe(scores[i].TypeHints);
            writer.WriteValueSafe(scores[i].Shadows);
        }
    }

    private static List<PlayerScore> ReadScoreboard(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        var scores = new List<PlayerScore>(Mathf.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong clientId);
            reader.ReadValueSafe(out string name);
            reader.ReadValueSafe(out int score);
            reader.ReadValueSafe(out int typeHints);
            reader.ReadValueSafe(out int shadows);
            scores.Add(new PlayerScore(clientId, name, score, typeHints, shadows));
        }

        return scores;
    }

    private void WriteSolvedOwners(FastBufferWriter writer)
    {
        writer.WriteValueSafe(solvedByClientId.Count);
        foreach (var kv in solvedByClientId)
        {
            writer.WriteValueSafe(kv.Key);
            writer.WriteValueSafe(kv.Value);
        }
    }

    private static Dictionary<int, ulong> ReadSolvedOwners(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        var owners = new Dictionary<int, ulong>(Mathf.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out int pokemonId);
            reader.ReadValueSafe(out ulong solverClientId);
            owners[pokemonId] = solverClientId;
        }

        return owners;
    }

    private static List<int> ReadIds(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        var ids = new List<int>(Mathf.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out int id);
            ids.Add(id);
        }

        return ids;
    }

    private readonly struct PlayerScore
    {
        public readonly ulong ClientId;
        public readonly string Name;
        public readonly int Count;
        public readonly int TypeHints;
        public readonly int Shadows;

        public PlayerScore(ulong clientId, string name, int count, int typeHints, int shadows)
        {
            ClientId = clientId;
            Name = string.IsNullOrWhiteSpace(name) ? "Player" : name;
            Count = count;
            TypeHints = Mathf.Max(0, typeHints);
            Shadows = Mathf.Max(0, shadows);
        }
    }

    private void BeginReturnToMenu(float delay)
    {
        if (returningToMenu)
            return;

        returningToMenu = true;
        StartCoroutine(CoReturnToMenu(delay));
    }

    private System.Collections.IEnumerator CoReturnToMenu(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        LoadingManager.Instance?.CancelLoad();
        QuizNetworkRuntime.Shutdown();
        SceneManager.LoadScene("MainMenu");
    }

    private enum CoopAction
    {
        Pause = 1,
        Resume = 2,
        RevealShadow = 3,
        RevealType = 4,
        Reset = 5,
        GiveUp = 6,
        ReturnToMenu = 7,
    }
}

public sealed class QuizMultiplayerStatusOverlay : MonoBehaviour
{
    private static string scoreboardText = string.Empty;
    private TMP_Text label;
    private float nextRefresh;

    public static void SetScoreboard(string text)
    {
        scoreboardText = text ?? string.Empty;

        var overlay = FindFirstObjectByType<QuizMultiplayerStatusOverlay>();
        if (overlay)
            overlay.Refresh();
    }

    public static void Ensure()
    {
        if (FindFirstObjectByType<QuizMultiplayerStatusOverlay>())
            return;

        var canvas = CreateCanvas();

        var go = new GameObject("Multiplayer Status", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.AddComponent<QuizMultiplayerStatusOverlay>();
    }

    private static Canvas CreateCanvas()
    {
        var existing = GameObject.Find("Quiz Multiplayer Overlay Canvas");
        if (existing && existing.TryGetComponent(out Canvas existingCanvas))
        {
            ConfigureCanvas(existingCanvas);
            return existingCanvas;
        }

        var go = new GameObject("Quiz Multiplayer Overlay Canvas", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        ConfigureCanvas(canvas);
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static void ConfigureCanvas(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (!scaler)
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
    }

    private void Awake()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-34f, -112f);
        rt.sizeDelta = new Vector2(430f, 54f);

        var background = gameObject.AddComponent<Image>();
        background.color = new Color(0.12f, 0.14f, 0.17f, 0.78f);

        label = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        label.transform.SetParent(transform, false);
        var labelRt = (RectTransform)label.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(10f, 0f);
        labelRt.offsetMax = new Vector2(-10f, 0f);
        label.fontSize = 13f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = Color.white;
        label.richText = true;
        label.raycastTarget = false;

        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh)
            return;

        Refresh();
        nextRefresh = Time.unscaledTime + 0.5f;
    }

    private void Refresh()
    {
        if (!label)
            return;

        var code = QuizNetworkRuntime.JoinCode ?? GameSettings.MultiplayerJoinCode;
        string scores = string.IsNullOrEmpty(scoreboardText) ? "" : $"\n{scoreboardText}";
        if (NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
        {
            int players = NetworkManager.Singleton.ConnectedClientsIds.Count;
            label.text = string.IsNullOrEmpty(code)
                ? $"Co-op host | Players {players}/2{scores}"
                : $"Co-op code {code} | Players {players}/2{scores}";
            return;
        }

        label.text = string.IsNullOrEmpty(code) ? $"Co-op client{scores}" : $"Co-op joined | {code}{scores}";
    }
}

public sealed class QuizMultiplayerChatOverlay : MonoBehaviour
{
    private const string ChatRequestMessage = "pkmnquiz_chat_request";
    private const string ChatBroadcastMessage = "pkmnquiz_chat";
    private const int MessageSize = 1024;
    private const int MaxChatLines = 64;
    private const int MaxMessageLength = 180;

    private static QuizMultiplayerChatOverlay instance;

    private readonly List<GameObject> lineObjects = new();
    private Canvas rootCanvas;
    private NetworkManager manager;
    private bool registered;
    private bool registeredAsServer;
    private string appliedLayoutScene;
    private RectTransform lineContainer;
    private LayoutElement messageListLayout;
    private ScrollRect scrollRect;
    private TMP_InputField inputField;
    private Button sendButton;

    public static void Ensure()
    {
        if (!QuizNetworkRuntime.IsMultiplayerActive)
            return;

        if (!instance)
        {
            var canvas = CreateCanvas();
            DontDestroyOnLoad(canvas.gameObject);

            var go = new GameObject("Multiplayer Chat", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            instance = go.AddComponent<QuizMultiplayerChatOverlay>();
            instance.rootCanvas = canvas;
        }

        instance.gameObject.SetActive(true);
        instance.RegisterHandlers();
        instance.ApplyScenePlacement();
    }

    public static void ResetSession()
    {
        if (!instance)
            return;

        instance.UnregisterHandlers();
        var root = instance.rootCanvas ? instance.rootCanvas.gameObject : instance.gameObject;
        Destroy(root);
        instance = null;
    }

    private static Canvas CreateCanvas()
    {
        var existing = GameObject.Find("Quiz Chat Canvas");
        if (existing && existing.TryGetComponent(out Canvas existingCanvas))
        {
            ConfigureCanvas(existingCanvas);
            return existingCanvas;
        }

        var go = new GameObject("Quiz Chat Canvas", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        ConfigureCanvas(canvas);
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static void ConfigureCanvas(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 620;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (!scaler)
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
    }

    private void Awake()
    {
        rootCanvas = GetComponentInParent<Canvas>();
        BuildUi();
    }

    private void OnDestroy()
    {
        UnregisterHandlers();

        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        if (!QuizNetworkRuntime.IsMultiplayerActive)
            return;

        RegisterHandlers();
        ApplyScenePlacement();
        RefreshSendButton();
    }

    private void RegisterHandlers()
    {
        var current = NetworkManager.Singleton;
        if (!current || !current.IsListening || current.CustomMessagingManager == null)
            return;

        bool serverStateChanged = registered && registeredAsServer != current.IsServer;
        if (registered && manager == current && !serverStateChanged)
            return;

        UnregisterHandlers();

        manager = current;
        registeredAsServer = manager.IsServer;

        if (registeredAsServer)
        {
            manager.CustomMessagingManager.RegisterNamedMessageHandler(
                ChatRequestMessage,
                OnChatRequestMessage
            );
        }

        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            ChatBroadcastMessage,
            OnChatBroadcastMessage
        );
        registered = true;
    }

    private void UnregisterHandlers()
    {
        if (!registered || !manager || manager.CustomMessagingManager == null)
        {
            registered = false;
            manager = null;
            registeredAsServer = false;
            return;
        }

        if (registeredAsServer)
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(ChatRequestMessage);

        manager.CustomMessagingManager.UnregisterNamedMessageHandler(ChatBroadcastMessage);
        registered = false;
        manager = null;
        registeredAsServer = false;
    }

    private void SendCurrentMessage()
    {
        if (!manager || !manager.IsListening)
            RegisterHandlers();
        if (!manager || !manager.IsListening)
            return;

        string message = NormalizeMessage(inputField ? inputField.text : null);
        if (string.IsNullOrEmpty(message))
            return;

        if (inputField)
        {
            inputField.SetTextWithoutNotify(string.Empty);
            inputField.ActivateInputField();
            inputField.Select();
        }
        RefreshSendButton();

        if (manager.IsServer)
        {
            RelayChatLine(manager.LocalClientId, QuizNetworkRuntime.PlayerNickname, message);
            return;
        }

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        writer.WriteValueSafe(QuizNetworkRuntime.PlayerNickname);
        writer.WriteValueSafe(message);
        manager.CustomMessagingManager.SendNamedMessage(ChatRequestMessage, 0UL, writer);
    }

    private void OnChatRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string requestedName);
        reader.ReadValueSafe(out string message);
        RelayChatLine(senderClientId, requestedName, message);
    }

    private void RelayChatLine(ulong senderClientId, string requestedName, string rawMessage)
    {
        if (!manager || !manager.IsServer || manager.CustomMessagingManager == null)
            return;

        string message = NormalizeMessage(rawMessage);
        if (string.IsNullOrEmpty(message))
            return;

        string senderName = ResolveDisplayName(senderClientId, requestedName);
        string timestamp = CurrentTimestamp();
        AppendLine(timestamp, senderClientId, senderName, message);

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        writer.WriteValueSafe(senderClientId);
        writer.WriteValueSafe(timestamp);
        writer.WriteValueSafe(senderName);
        writer.WriteValueSafe(message);
        manager.CustomMessagingManager.SendNamedMessageToAll(ChatBroadcastMessage, writer);
    }

    private void OnChatBroadcastMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (manager && manager.IsServer)
            return;

        reader.ReadValueSafe(out ulong chatSenderClientId);
        reader.ReadValueSafe(out string timestamp);
        reader.ReadValueSafe(out string senderName);
        reader.ReadValueSafe(out string message);

        AppendLine(
            timestamp,
            chatSenderClientId,
            ResolveDisplayName(chatSenderClientId, senderName),
            message
        );
    }

    private void BuildUi()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-480f, -116f);
        rt.sizeDelta = new Vector2(330f, 124f);

        var background = gameObject.AddComponent<Image>();
        background.color = new Color(0.05f, 0.06f, 0.07f, 0.84f);

        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(9, 9, 7, 7);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateHeader();
        CreateMessageList();
        CreateInputRow();
        ApplyScenePlacement(force: true);
    }

    private void ApplyScenePlacement(bool force = false)
    {
        var rt = (RectTransform)transform;
        string sceneName = SceneManager.GetActiveScene().name;
        bool mainMenu = string.Equals(sceneName, "MainMenu", System.StringComparison.OrdinalIgnoreCase);
        string layoutKey = mainMenu ? "main-menu" : "quiz";
        if (!force && appliedLayoutScene == layoutKey)
            return;

        appliedLayoutScene = layoutKey;

        if (mainMenu)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(207f, -520f);
            rt.sizeDelta = new Vector2(240f, 176f);
            SetMessageListHeight(106f);
            return;
        }

        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-480f, -116f);
        rt.sizeDelta = new Vector2(330f, 124f);
        SetMessageListHeight(54f);
    }

    private void SetMessageListHeight(float height)
    {
        if (!messageListLayout)
            return;

        messageListLayout.minHeight = height;
        messageListLayout.preferredHeight = height;
    }

    private void CreateHeader()
    {
        var label = UiObject("Header").AddComponent<TextMeshProUGUI>();
        label.text = "Chat";
        label.fontSize = 13f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;

        var layout = label.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 16f;
        layout.preferredHeight = 16f;
    }

    private void CreateMessageList()
    {
        var go = UiObject("Messages");
        var image = go.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.18f);

        messageListLayout = go.AddComponent<LayoutElement>();
        messageListLayout.minHeight = 54f;
        messageListLayout.preferredHeight = 54f;

        scrollRect = go.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(go.transform, false);
        var viewport = (RectTransform)viewportGo.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(7f, 4f);
        viewport.offsetMax = new Vector2(-7f, -4f);
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        lineContainer = (RectTransform)contentGo.transform;
        lineContainer.anchorMin = new Vector2(0f, 1f);
        lineContainer.anchorMax = new Vector2(1f, 1f);
        lineContainer.pivot = new Vector2(0.5f, 1f);
        lineContainer.anchoredPosition = Vector2.zero;
        lineContainer.sizeDelta = Vector2.zero;

        var contentLayout = contentGo.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 4f;
        contentLayout.childAlignment = TextAnchor.UpperLeft;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewport;
        scrollRect.content = lineContainer;
    }

    private void CreateInputRow()
    {
        var row = UiObject("Input Row");
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = 28f;
        rowElement.preferredHeight = 28f;

        inputField = CreateInput(row.transform);
        inputField.onValueChanged.AddListener(_ => RefreshSendButton());
        inputField.onSubmit.AddListener(_ => SendCurrentMessage());

        sendButton = CreateButton(row.transform, "Send", SendCurrentMessage);
        RefreshSendButton();
    }

    private TMP_InputField CreateInput(Transform parent)
    {
        var go = new GameObject("Chat Input", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.94f);

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 28f;
        layout.preferredHeight = 28f;
        layout.flexibleWidth = 1f;

        var input = go.AddComponent<TMP_InputField>();
        input.characterLimit = MaxMessageLength;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.richText = false;

        var viewportGo = new GameObject("Text Area", typeof(RectTransform));
        viewportGo.transform.SetParent(go.transform, false);
        var viewport = (RectTransform)viewportGo.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(8f, 2f);
        viewport.offsetMax = new Vector2(-8f, -2f);
        viewportGo.AddComponent<RectMask2D>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(viewportGo.transform, false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.fontSize = 13f;
        text.color = new Color(0.05f, 0.06f, 0.07f, 1f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.richText = false;

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGo.transform.SetParent(viewportGo.transform, false);
        var placeholderRt = (RectTransform)placeholderGo.transform;
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;

        var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholder.text = "Message";
        placeholder.fontSize = 13f;
        placeholder.color = new Color(0.2f, 0.25f, 0.3f, 0.55f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.textWrappingMode = TextWrappingModes.NoWrap;
        placeholder.richText = false;

        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;
        return input;
    }

    private Button CreateButton(Transform parent, string labelText, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(labelText, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.16f, 0.35f, 0.70f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        var layout = go.AddComponent<LayoutElement>();
        layout.minWidth = 56f;
        layout.preferredWidth = 56f;
        layout.minHeight = 28f;
        layout.preferredHeight = 28f;

        var labelGo = new GameObject("Text", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(6f, 0f);
        labelRt.offsetMax = new Vector2(-6f, 0f);

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 12f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return button;
    }

    private GameObject UiObject(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        return go;
    }

    private void RefreshSendButton()
    {
        if (sendButton)
            sendButton.interactable = !string.IsNullOrWhiteSpace(inputField ? inputField.text : null);
    }

    private void AppendLine(string timestamp, ulong senderClientId, string senderName, string message)
    {
        timestamp = NormalizeTimestamp(timestamp);
        senderName = NormalizeSenderName(senderName);
        message = NormalizeMessage(message);
        if (string.IsNullOrEmpty(message) || !lineContainer)
            return;

        var lineGo = new GameObject("Message", typeof(RectTransform));
        lineGo.transform.SetParent(lineContainer, false);

        var label = lineGo.AddComponent<TextMeshProUGUI>();
        label.text =
            $"{QuizMultiplayerCoordinator.EscapeRichText(timestamp)} "
            + $"{QuizMultiplayerCoordinator.FormatColoredPlayerName(senderClientId, senderName)}: "
            + QuizMultiplayerCoordinator.EscapeRichText(message);
        label.fontSize = 12f;
        label.color = new Color(0.91f, 0.96f, 1f, 1f);
        label.alignment = TextAlignmentOptions.Left;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.richText = true;
        label.raycastTarget = false;

        var layout = lineGo.AddComponent<LayoutElement>();
        layout.minHeight = 15f;

        lineObjects.Add(lineGo);
        while (lineObjects.Count > MaxChatLines)
        {
            var old = lineObjects[0];
            lineObjects.RemoveAt(0);
            if (old)
                Destroy(old);
        }

        StartCoroutine(CoScrollToBottom());
    }

    private System.Collections.IEnumerator CoScrollToBottom()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (scrollRect)
            scrollRect.verticalNormalizedPosition = 0f;
    }

    private static string ResolveDisplayName(ulong clientId, string requestedName)
    {
        requestedName = NormalizeSenderName(requestedName);
        if (string.Equals(requestedName, "Player", System.StringComparison.OrdinalIgnoreCase))
            return DefaultChatName(clientId);

        return requestedName;
    }

    private static string NormalizeSenderName(string senderName)
    {
        senderName = QuizNetworkRuntime.NormalizeNickname(senderName);
        return senderName.Replace(":", string.Empty);
    }

    private static string DefaultChatName(ulong clientId)
    {
        return clientId == 0UL ? "Player1" : "Player2";
    }

    private static string CurrentTimestamp()
    {
        return System.DateTime.Now.ToString(
            "HH\\:mm",
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    private static string NormalizeTimestamp(string timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
            return CurrentTimestamp();

        timestamp = timestamp.Trim();
        if (
            timestamp.Length == 5
            && char.IsDigit(timestamp[0])
            && char.IsDigit(timestamp[1])
            && timestamp[2] == ':'
            && char.IsDigit(timestamp[3])
            && char.IsDigit(timestamp[4])
        )
        {
            return timestamp;
        }

        return CurrentTimestamp();
    }

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        message = message.Trim();
        if (message.Length > MaxMessageLength)
            message = message[..MaxMessageLength];

        System.Text.StringBuilder sb = new(message.Length);
        bool lastWasSpace = false;
        foreach (char ch in message)
        {
            if (char.IsControl(ch))
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                    sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(ch);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }
}
