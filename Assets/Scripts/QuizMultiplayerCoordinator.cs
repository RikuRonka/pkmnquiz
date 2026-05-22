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

    private static QuizMultiplayerCoordinator instance;

    private QuizManager quiz;
    private NetworkManager manager;
    private bool registered;
    private bool stateReceived;
    private bool returningToMenu;
    private float nextStateRequest;
    private float nextTimerSync;
    private readonly Dictionary<ulong, string> playerNames = new();
    private readonly Dictionary<ulong, int> playerScores = new();

    public static bool IsActive => QuizNetworkRuntime.IsMultiplayerActive;
    public static bool IsClientOnly => QuizNetworkRuntime.IsMultiplayerClientOnly;

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

        BroadcastSolved(solvedIds, senderClientId);
        BroadcastScoreboard();
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
                break;
            case CoopAction.RevealType:
                payload = quiz.ApplyNetworkRevealType();
                if (payload == 0)
                    return;
                break;
            case CoopAction.Reset:
                quiz.ApplyNetworkReset();
                ResetScores();
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
    }

    private void BroadcastAction(CoopAction action, int payload)
    {
        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        writer.WriteValueSafe((int)action);
        writer.WriteValueSafe(payload);
        writer.WriteValueSafe(quiz ? quiz.ElapsedSeconds : 0f);
        writer.WriteValueSafe(quiz && quiz.IsQuizRunning);
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
                quiz.ApplyNetworkReset();
                break;
            case CoopAction.GiveUp:
                quiz.ApplyNetworkGiveUp();
                break;
        }
    }

    private void BroadcastSolved(IReadOnlyCollection<int> solvedIds, ulong solverClientId)
    {
        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        WriteSolvedPayload(writer, solvedIds, solverClientId);
        manager.CustomMessagingManager.SendNamedMessageToAll(SolvedMessage, writer);
    }

    private void OnSolvedMessage(ulong senderClientId, FastBufferReader reader)
    {
        var solvedIds = ReadSolvedPayload(reader, out var solverClientId);
        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (!quiz)
            return;

        bool solvedByLocalPlayer = manager && solverClientId == manager.LocalClientId;
        quiz.ApplyNetworkSolvedIds(solvedIds, clearInput: solvedByLocalPlayer, playSound: true);
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
        stateReceived = true;

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
    }

    private List<PlayerScore> BuildScoreboard()
    {
        var scores = new List<PlayerScore>();
        var clientIds = new List<ulong>(playerNames.Keys);
        clientIds.Sort();

        foreach (var clientId in clientIds)
        {
            var score = playerScores.TryGetValue(clientId, out var count) ? count : 0;
            scores.Add(new PlayerScore(playerNames[clientId], score));
        }

        return scores;
    }

    private static void ApplyScoreboard(List<PlayerScore> scoreboard)
    {
        QuizMultiplayerStatusOverlay.SetScoreboard(FormatScoreboard(scoreboard));
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

            sb.Append(scoreboard[i].Name);
            sb.Append(": ");
            sb.Append(scoreboard[i].Count);
        }

        return sb.ToString();
    }

    private static string DefaultPlayerName(ulong clientId)
    {
        return clientId == 0UL ? "Player 1" : "Player 2";
    }

    private static void WriteSolvedPayload(
        FastBufferWriter writer,
        IReadOnlyCollection<int> solvedIds,
        ulong solverClientId
    )
    {
        writer.WriteValueSafe(solverClientId);
        WriteIds(writer, solvedIds);
    }

    private static List<int> ReadSolvedPayload(FastBufferReader reader, out ulong solverClientId)
    {
        reader.ReadValueSafe(out solverClientId);
        return ReadIds(reader);
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
            writer.WriteValueSafe(scores[i].Name);
            writer.WriteValueSafe(scores[i].Count);
        }
    }

    private static List<PlayerScore> ReadScoreboard(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        var scores = new List<PlayerScore>(Mathf.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out string name);
            reader.ReadValueSafe(out int score);
            scores.Add(new PlayerScore(name, score));
        }

        return scores;
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
        public readonly string Name;
        public readonly int Count;

        public PlayerScore(string name, int count)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Player" : name;
            Count = count;
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
