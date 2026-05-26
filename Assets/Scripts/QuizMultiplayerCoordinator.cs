using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class QuizMultiplayerCoordinator : MonoBehaviour
{
    private const string GuessMessage = "pkmnquiz_guess";
    private const string SolvedMessage = "pkmnquiz_solved";
    private const string GuessFeedbackMessage = "pkmnquiz_guess_feedback";
    private const string ActionRequestMessage = "pkmnquiz_action_request";
    private const string ActionMessage = "pkmnquiz_action";
    private const string NicknameMessage = "pkmnquiz_nickname";
    private const string ScoreboardMessage = "pkmnquiz_scoreboard";
    private const string StateRequestMessage = "pkmnquiz_state_request";
    private const string StateMessage = "pkmnquiz_state";
    private const string TimerMessage = "pkmnquiz_timer";
    private const string PlayerNoticeMessage = "pkmnquiz_player_notice";
    private const int MessageSize = 32768;
    private const float TimerSyncInterval = 0.25f;
    private static readonly Color MissedEndStateColor = new(1f, 0f, 0f, 1f);
    private static readonly Color LeaveNoticeBackground = new(0.72f, 0.08f, 0.10f, 0.95f);

    private static QuizMultiplayerCoordinator instance;
    private static List<PlayerScore> latestScoreboard = new();
    private static readonly Dictionary<ulong, string> latestPlayerColors = new();
    private static readonly Dictionary<string, SavedQuizSession> savedQuizSessions = new();
    private static string latestSavedQuizSessionKey;
    private static string restoreSavedQuizSessionKey;

    private QuizManager quiz;
    private NetworkManager manager;
    private bool registered;
    private bool stateReceived;
    private bool returningToMenu;
    private float nextStateRequest;
    private float nextTimerSync;
    private NetworkStateSnapshot pendingState;
    private readonly Dictionary<ulong, string> playerNames = new();
    private readonly Dictionary<ulong, string> playerColors = new();
    private readonly Dictionary<ulong, int> playerScores = new();
    private readonly Dictionary<ulong, int> playerTypeHints = new();
    private readonly Dictionary<ulong, int> playerShadows = new();
    private readonly Dictionary<int, ulong> solvedByClientId = new();
    private readonly HashSet<ulong> hostJoinNotifiedClientIds = new();

    public static bool IsActive => QuizNetworkRuntime.IsMultiplayerActive;
    public static bool IsClientOnly => QuizNetworkRuntime.IsMultiplayerClientOnly;
    public static bool HasSavedQuizSession => savedQuizSessions.Count > 0;

    public static bool TryGetSavedQuizSelection(out int generation, out string typeFilter)
    {
        if (!TryGetLatestSavedQuizSession(out var session))
        {
            generation = 0;
            typeFilter = null;
            return false;
        }

        generation = session.Generation;
        typeFilter = session.TypeFilter;
        return true;
    }

    public static bool QueueSavedQuizSessionRestore(int generation, string typeFilter)
    {
        string key = SavedQuizSession.KeyFor(generation, typeFilter);
        if (!savedQuizSessions.ContainsKey(key))
            return false;

        restoreSavedQuizSessionKey = key;
        return true;
    }

    public static void ClearSavedQuizSession()
    {
        savedQuizSessions.Clear();
        latestSavedQuizSessionKey = null;
        restoreSavedQuizSessionKey = null;
    }

    public static void ClearSavedQuizSession(int generation, string typeFilter)
    {
        string key = SavedQuizSession.KeyFor(generation, typeFilter);
        savedQuizSessions.Remove(key);
        if (string.Equals(latestSavedQuizSessionKey, key, System.StringComparison.Ordinal))
            latestSavedQuizSessionKey = null;
        if (string.Equals(restoreSavedQuizSessionKey, key, System.StringComparison.Ordinal))
            restoreSavedQuizSessionKey = null;
    }

    public static void ClearSavedQuizSession(QuizManager quizManager)
    {
        if (!quizManager)
            return;

        ClearSavedQuizSession(quizManager.CurrentQuizGeneration, quizManager.CurrentTypeFilter);
    }

    public static void SaveCurrentQuizSessionForLobby(QuizManager quizManager = null)
    {
        if (!quizManager)
            quizManager = FindFirstObjectByType<QuizManager>();
        if (!quizManager)
            return;

        if (quizManager.IsQuizFinished)
        {
            ClearSavedQuizSession(quizManager);
            return;
        }

        IReadOnlyList<PlayerScore> scoreboard = instance
            ? instance.BuildScoreboard()
            : latestScoreboard;
        IReadOnlyDictionary<int, ulong> solvedOwners = instance
            ? instance.solvedByClientId
            : null;

        var session = new SavedQuizSession(
            quizManager.CurrentQuizGeneration,
            quizManager.CurrentTypeFilter,
            quizManager.SolvedIds,
            quizManager.HintedIds,
            quizManager.ShadowedIds,
            quizManager.ElapsedSeconds,
            quizManager.IsQuizRunning,
            scoreboard,
            solvedOwners
        );
        string key = session.Key;
        savedQuizSessions[key] = session;
        latestSavedQuizSessionKey = key;
        if (string.Equals(restoreSavedQuizSessionKey, key, System.StringComparison.Ordinal))
            restoreSavedQuizSessionKey = null;
    }

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
            return QuizNetworkRuntime.ColorFromHex(GetKnownPlayerColor(solverClientId));
        }

        return QuizNetworkRuntime.ColorFromHex(QuizNetworkRuntime.PlayerColorHex);
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

    public static void NotifyLocalPlayerLeavingQuiz()
    {
        if (!QuizNetworkRuntime.IsMultiplayerClientOnly)
            return;

        if (!instance)
            Attach(FindFirstObjectByType<QuizManager>());
        if (!instance || !instance.manager || instance.manager.CustomMessagingManager == null)
            return;

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        writer.WriteValueSafe(string.Empty);
        writer.WriteValueSafe(true);
        instance.manager.CustomMessagingManager.SendNamedMessage(PlayerNoticeMessage, 0UL, writer);
    }

    public static string LocalPlayerColorHex
    {
        get
        {
            var current = NetworkManager.Singleton;
            if (current && latestPlayerColors.TryGetValue(current.LocalClientId, out var colorHex))
                return QuizNetworkRuntime.NormalizeColorHex(colorHex);

            return QuizNetworkRuntime.NormalizeColorHex(QuizNetworkRuntime.PlayerColorHex);
        }
    }

    public static bool IsPlayerColorTakenByAnother(string colorHex)
    {
        colorHex = QuizNetworkRuntime.NormalizeColorHex(colorHex);
        var current = NetworkManager.Singleton;
        ulong localClientId = current ? current.LocalClientId : ulong.MaxValue;

        if (instance)
        {
            foreach (var kv in instance.playerColors)
            {
                if (kv.Key == localClientId)
                    continue;
                if (
                    string.Equals(
                        QuizNetworkRuntime.NormalizeColorHex(kv.Value),
                        colorHex,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                )
                    return true;
            }
        }

        if (latestScoreboard != null)
        {
            foreach (var score in latestScoreboard)
            {
                if (score.ClientId == localClientId)
                    continue;
                if (
                    string.Equals(
                        QuizNetworkRuntime.NormalizeColorHex(score.ColorHex),
                        colorHex,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                )
                    return true;
            }
        }

        return false;
    }

    public static bool TrySetLocalPlayerColor(string colorHex)
    {
        colorHex = QuizNetworkRuntime.NormalizeColorHex(colorHex);

        var current = NetworkManager.Singleton;
        if (current && current.IsListening && IsPlayerColorTakenByAnother(colorHex))
        {
            ShowLocalNotice("That color is already taken.", false);
            return false;
        }

        QuizNetworkRuntime.SetPlayerColorHex(colorHex);

        if (!current || !current.IsListening)
            return true;

        if (!instance)
            Attach(FindFirstObjectByType<QuizManager>());

        ulong localClientId = current.LocalClientId;
        ApplyLocalPlayerColorToScoreboard(localClientId, colorHex);

        if (instance)
        {
            instance.EnsurePlayer(localClientId, QuizNetworkRuntime.PlayerNickname, colorHex);
            if (current.IsServer)
                instance.BroadcastScoreboard();
            else if (current.IsClient)
                instance.SendNickname();
        }

        _ = QuizNetworkRuntime.UpdateCurrentLobbyPlayerAsync(
            QuizNetworkRuntime.PlayerNickname,
            colorHex
        );
        return true;
    }

    private static bool RequestAction(CoopAction action)
    {
        if (!QuizNetworkRuntime.IsMultiplayerActive)
            return false;

        if (IsHostControlledAction(action) && QuizNetworkRuntime.IsMultiplayerClientOnly)
            return action == CoopAction.ReturnToMenu ? false : true;

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
            EnsurePlayer(
                manager.LocalClientId,
                QuizNetworkRuntime.PlayerNickname,
                QuizNetworkRuntime.PlayerColorHex
            );
            MarkCurrentClientsAsAlreadyPresent();
            if (!TryRestoreSavedQuizSessionForCurrentQuiz())
            {
                ResetScores();
                solvedByClientId.Clear();
            }
            BroadcastScoreboard();
        }
        else if (manager && manager.IsClient)
        {
            stateReceived = false;
            nextStateRequest = 0f;
            SendNickname();
            if (pendingState != null)
                ApplyNetworkStateSnapshot(pendingState);
            else
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
            GuessFeedbackMessage,
            OnGuessFeedbackMessage
        );
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            ScoreboardMessage,
            OnScoreboardMessage
        );
        manager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, OnStateMessage);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(TimerMessage, OnTimerMessage);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            PlayerNoticeMessage,
            OnPlayerNoticeMessage
        );
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
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(GuessFeedbackMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(ScoreboardMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(TimerMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(PlayerNoticeMessage);
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

        if (IsHostControlledAction(action) && !manager.IsServer)
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
        var feedback = quiz.LastNetworkGuessFeedback;
        if (solvedIds.Count == 0)
        {
            BroadcastGuessFeedback(feedback, senderClientId);
            return;
        }

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

        if (IsHostControlledAction(action) && senderClientId != manager.LocalClientId)
            return;

        if (action == CoopAction.ReturnToMenu)
        {
            SaveCurrentQuizSessionForLobby();
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
                ClearSavedQuizSession(quiz);
                break;
            case CoopAction.GiveUp:
                quiz.ApplyNetworkGiveUp();
                ClearSavedQuizSession(quiz);
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

    private void BroadcastGuessFeedback(
        QuizManager.MultiplayerGuessFeedback feedback,
        ulong guesserClientId
    )
    {
        if (!feedback.HasValue || !manager || !manager.IsServer || manager.CustomMessagingManager == null)
            return;

        using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
        writer.WriteValueSafe(guesserClientId);
        writer.WriteValueSafe((int)feedback.Kind);
        writer.WriteValueSafe(feedback.PokemonId);
        writer.WriteValueSafe(feedback.Message ?? string.Empty);
        writer.WriteValueSafe(feedback.Duration);
        manager.CustomMessagingManager.SendNamedMessageToAll(GuessFeedbackMessage, writer);
    }

    private void OnGuessFeedbackMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (manager && manager.IsServer)
            return;

        reader.ReadValueSafe(out ulong guesserClientId);
        reader.ReadValueSafe(out int kindValue);
        reader.ReadValueSafe(out int pokemonId);
        reader.ReadValueSafe(out string message);
        reader.ReadValueSafe(out float duration);

        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (!quiz)
            return;

        var feedback = new QuizManager.MultiplayerGuessFeedback(
            (QuizManager.MultiplayerGuessFeedbackKind)kindValue,
            pokemonId,
            message,
            duration
        );
        bool clearInput = manager && guesserClientId == manager.LocalClientId;
        quiz.ApplyNetworkGuessFeedback(feedback, clearInput);
    }

    private void RecordSolvedBy(IReadOnlyCollection<int> solvedIds, ulong solverClientId)
    {
        if (solvedIds == null)
            return;

        foreach (var id in solvedIds)
            solvedByClientId[id] = solverClientId;
    }

    private void SaveCurrentQuizSessionForLobby()
    {
        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        SaveCurrentQuizSessionForLobby(quiz);
    }

    private bool TryRestoreSavedQuizSessionForCurrentQuiz()
    {
        if (string.IsNullOrEmpty(restoreSavedQuizSessionKey) || !quiz)
            return false;

        if (!savedQuizSessions.TryGetValue(restoreSavedQuizSessionKey, out var session))
        {
            restoreSavedQuizSessionKey = null;
            return false;
        }

        restoreSavedQuizSessionKey = null;
        if (!session.Matches(quiz.CurrentQuizGeneration, quiz.CurrentTypeFilter))
            return false;

        var connectedClientIds = new HashSet<ulong>();
        if (manager)
        {
            foreach (var clientId in manager.ConnectedClientsIds)
                connectedClientIds.Add(clientId);
        }

        RestoreSavedScoreboard(session.Scoreboard, connectedClientIds);

        solvedByClientId.Clear();
        foreach (var kv in session.SolvedOwners)
        {
            if (connectedClientIds.Count > 0 && !connectedClientIds.Contains(kv.Value))
                continue;

            solvedByClientId[kv.Key] = kv.Value;
        }

        quiz.ApplySavedMultiplayerSession(
            session.SolvedIds,
            session.HintedIds,
            session.ShadowedIds,
            session.Elapsed,
            session.Running
        );
        savedQuizSessions.Remove(session.Key);
        if (string.Equals(latestSavedQuizSessionKey, session.Key, System.StringComparison.Ordinal))
            latestSavedQuizSessionKey = null;
        return true;
    }

    private void RestoreSavedScoreboard(
        IReadOnlyList<PlayerScore> scoreboard,
        HashSet<ulong> connectedClientIds
    )
    {
        if (scoreboard == null)
            return;

        foreach (var score in scoreboard)
        {
            if (connectedClientIds.Count > 0 && !connectedClientIds.Contains(score.ClientId))
                continue;

            EnsurePlayer(score.ClientId, score.Name, score.ColorHex);
            playerScores[score.ClientId] = score.Count;
            playerTypeHints[score.ClientId] = score.TypeHints;
            playerShadows[score.ClientId] = score.Shadows;
        }
    }

    private static bool TryGetLatestSavedQuizSession(out SavedQuizSession session)
    {
        if (
            !string.IsNullOrEmpty(latestSavedQuizSessionKey)
            && savedQuizSessions.TryGetValue(latestSavedQuizSessionKey, out session)
        )
        {
            return true;
        }

        foreach (var kv in savedQuizSessions)
        {
            latestSavedQuizSessionKey = kv.Key;
            session = kv.Value;
            return true;
        }

        latestSavedQuizSessionKey = null;
        session = null;
        return false;
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
        writer.WriteValueSafe(QuizNetworkRuntime.PlayerColorHex);
        manager.CustomMessagingManager.SendNamedMessage(NicknameMessage, 0UL, writer);
    }

    private void OnNicknameMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string nickname);
        reader.ReadValueSafe(out string colorHex);
        colorHex = NormalizeRequestedPlayerColor(senderClientId, colorHex);
        EnsurePlayer(senderClientId, nickname, colorHex);
        MaybeShowHostJoinNotice(senderClientId, playerNames[senderClientId]);
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
            string playerName = playerNames.TryGetValue(clientId, out var knownName)
                ? knownName
                : DefaultPlayerName(clientId);
            if (clientId != manager.LocalClientId && IsQuizSceneActive())
            {
                string message = $"{QuizNetworkRuntime.NormalizeNickname(playerName)} left the quiz.";
                ShowLocalNotice(message, true);
                BroadcastPlayerNotice(message, true);
            }

            playerNames.Remove(clientId);
            playerColors.Remove(clientId);
            playerScores.Remove(clientId);
            playerTypeHints.Remove(clientId);
            playerShadows.Remove(clientId);
            hostJoinNotifiedClientIds.Remove(clientId);
            BroadcastScoreboard();
            return;
        }

        if (clientId == 0UL)
            BeginReturnToMenu(0f);
    }

    private static bool IsQuizSceneActive()
    {
        return SceneManager.GetActiveScene().name.Equals(
            "Quiz",
            System.StringComparison.OrdinalIgnoreCase
        );
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
        WriteIds(writer, quiz.HintedIds);
        WriteIds(writer, quiz.ShadowedIds);
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
        var hintedIds = ReadIds(reader);
        var shadowedIds = ReadIds(reader);
        var scoreboard = ReadScoreboard(reader);
        var solvedOwners = ReadSolvedOwners(reader);
        var snapshot = new NetworkStateSnapshot(
            generation,
            typeFilter,
            solvedIds,
            hintedIds,
            shadowedIds,
            elapsed,
            isRunning,
            scoreboard,
            solvedOwners
        );

        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (!quiz)
        {
            pendingState = snapshot;
            stateReceived = false;
            nextStateRequest = 0f;
            return;
        }

        ApplyNetworkStateSnapshot(snapshot);
    }

    private void ApplyNetworkStateSnapshot(NetworkStateSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();
        if (!quiz)
        {
            pendingState = snapshot;
            stateReceived = false;
            nextStateRequest = 0f;
            return;
        }

        pendingState = null;
        solvedByClientId.Clear();
        foreach (var kv in snapshot.SolvedOwners)
            solvedByClientId[kv.Key] = kv.Value;

        ApplyScoreboard(snapshot.Scoreboard);
        quiz.ApplyNetworkState(
            snapshot.Generation,
            snapshot.TypeFilter,
            snapshot.SolvedIds,
            snapshot.HintedIds,
            snapshot.ShadowedIds,
            snapshot.Elapsed,
            snapshot.Running
        );
        stateReceived = true;
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

    private void BroadcastPlayerNotice(
        string message,
        bool isLeaveNotice,
        ulong excludedClientId = ulong.MaxValue
    )
    {
        if (!manager || !manager.IsServer || manager.CustomMessagingManager == null)
            return;

        foreach (var clientId in manager.ConnectedClientsIds)
        {
            if (clientId == manager.LocalClientId || clientId == excludedClientId)
                continue;

            using var writer = new FastBufferWriter(MessageSize, Allocator.Temp);
            writer.WriteValueSafe(message ?? string.Empty);
            writer.WriteValueSafe(isLeaveNotice);
            manager.CustomMessagingManager.SendNamedMessage(PlayerNoticeMessage, clientId, writer);
        }
    }

    private void OnPlayerNoticeMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string message);
        reader.ReadValueSafe(out bool isLeaveNotice);

        if (manager && manager.IsServer)
        {
            if (senderClientId != manager.LocalClientId && isLeaveNotice)
            {
                string playerName = playerNames.TryGetValue(senderClientId, out var knownName)
                    ? knownName
                    : DefaultPlayerName(senderClientId);
                string relayMessage =
                    $"{QuizNetworkRuntime.NormalizeNickname(playerName)} left the quiz.";
                ShowLocalNotice(relayMessage, true);
                BroadcastPlayerNotice(relayMessage, true, senderClientId);
            }
            return;
        }

        ShowLocalNotice(message, isLeaveNotice);
    }

    private static void ShowLocalNotice(string message, bool isLeaveNotice)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var currentQuiz = FindFirstObjectByType<QuizManager>();
        if (!currentQuiz || !currentQuiz.toast)
            return;

        Color? backgroundColor = isLeaveNotice ? LeaveNoticeBackground : null;
        currentQuiz.toast.Show(message, 2.5f, backgroundColor);
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

    private void EnsurePlayer(ulong clientId, string nickname, string colorHex = null)
    {
        if (!playerNames.ContainsKey(clientId))
            playerNames[clientId] = DefaultPlayerName(clientId);

        if (!string.IsNullOrWhiteSpace(nickname))
            playerNames[clientId] = QuizNetworkRuntime.NormalizeNickname(nickname);

        if (!playerColors.ContainsKey(clientId))
            playerColors[clientId] = QuizNetworkRuntime.DefaultColorForClient(clientId);

        if (!string.IsNullOrWhiteSpace(colorHex))
            playerColors[clientId] = QuizNetworkRuntime.NormalizeColorHex(colorHex);

        if (!playerScores.ContainsKey(clientId))
            playerScores[clientId] = 0;
        if (!playerTypeHints.ContainsKey(clientId))
            playerTypeHints[clientId] = 0;
        if (!playerShadows.ContainsKey(clientId))
            playerShadows[clientId] = 0;
    }

    private string NormalizeRequestedPlayerColor(ulong clientId, string colorHex)
    {
        colorHex = QuizNetworkRuntime.NormalizeColorHex(colorHex);
        if (!IsColorTakenByOtherClient(clientId, colorHex))
            return colorHex;

        if (playerColors.TryGetValue(clientId, out var existingColor))
            return QuizNetworkRuntime.NormalizeColorHex(existingColor);

        foreach (var paletteColor in QuizNetworkRuntime.PlayerColorPalette)
        {
            string normalized = QuizNetworkRuntime.NormalizeColorHex(paletteColor);
            if (!IsColorTakenByOtherClient(clientId, normalized))
                return normalized;
        }

        return QuizNetworkRuntime.DefaultColorForClient(clientId);
    }

    private bool IsColorTakenByOtherClient(ulong clientId, string colorHex)
    {
        colorHex = QuizNetworkRuntime.NormalizeColorHex(colorHex);
        foreach (var kv in playerColors)
        {
            if (kv.Key == clientId)
                continue;
            if (
                string.Equals(
                    QuizNetworkRuntime.NormalizeColorHex(kv.Value),
                    colorHex,
                    System.StringComparison.OrdinalIgnoreCase
                )
            )
                return true;
        }

        return false;
    }

    private void MarkCurrentClientsAsAlreadyPresent()
    {
        if (!manager || !manager.IsServer)
            return;

        foreach (var clientId in manager.ConnectedClientsIds)
        {
            EnsurePlayer(clientId, null);
            if (clientId != manager.LocalClientId)
                hostJoinNotifiedClientIds.Add(clientId);
        }
    }

    private void MaybeShowHostJoinNotice(ulong clientId, string playerName)
    {
        if (!manager || !manager.IsServer || clientId == manager.LocalClientId)
            return;
        if (!hostJoinNotifiedClientIds.Add(clientId))
            return;

        if (!quiz)
            quiz = FindFirstObjectByType<QuizManager>();

        string destination = IsQuizSceneActive() ? "quiz" : "lobby";
        string message = $"{QuizNetworkRuntime.NormalizeNickname(playerName)} joined the {destination}.";
        WindowsAppNotifier.NotifyLobbyJoin();

        if (quiz && quiz.toast)
            quiz.toast.Show(message, 2.5f);

        if (IsQuizSceneActive())
            BroadcastPlayerNotice(message, false, clientId);
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
            var colorHex = playerColors.TryGetValue(clientId, out var color)
                ? color
                : QuizNetworkRuntime.DefaultColorForClient(clientId);
            scores.Add(
                new PlayerScore(clientId, playerNames[clientId], colorHex, score, typeHints, shadows)
            );
        }

        return scores;
    }

    private static void ApplyScoreboard(List<PlayerScore> scoreboard)
    {
        latestScoreboard = scoreboard == null
            ? new List<PlayerScore>()
            : new List<PlayerScore>(scoreboard);

        latestPlayerColors.Clear();
        foreach (var score in latestScoreboard)
            latestPlayerColors[score.ClientId] = score.ColorHex;

        QuizMultiplayerStatusOverlay.SetScoreboard(FormatScoreboard(scoreboard));
        var currentQuiz = FindFirstObjectByType<QuizManager>();
        if (currentQuiz)
            currentQuiz.RefreshMultiplayerEndStateColors();
    }

    private static void ApplyLocalPlayerColorToScoreboard(ulong localClientId, string colorHex)
    {
        if (latestScoreboard == null || latestScoreboard.Count == 0)
        {
            latestPlayerColors[localClientId] = QuizNetworkRuntime.NormalizeColorHex(colorHex);
            QuizMultiplayerStatusOverlay.SetScoreboard(FormatScoreboard(latestScoreboard));
            return;
        }

        var next = new List<PlayerScore>(latestScoreboard.Count);
        bool changed = false;
        foreach (var score in latestScoreboard)
        {
            if (score.ClientId != localClientId)
            {
                next.Add(score);
                continue;
            }

            next.Add(
                new PlayerScore(
                    score.ClientId,
                    score.Name,
                    colorHex,
                    score.Count,
                    score.TypeHints,
                    score.Shadows
                )
            );
            changed = true;
        }

        if (changed)
            ApplyScoreboard(next);
        else
            latestPlayerColors[localClientId] = QuizNetworkRuntime.NormalizeColorHex(colorHex);
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
        var current = NetworkManager.Singleton;
        ulong localClientId = current ? current.LocalClientId : ulong.MaxValue;
        for (int i = 0; i < scoreboard.Count; i++)
        {
            if (i > 0)
                sb.Append(" | ");

            bool isLocalPlayer = current && scoreboard[i].ClientId == localClientId;
            if (isLocalPlayer)
                sb.Append("<link=\"local_color\">");
            sb.Append(FormatColoredPlayerName(scoreboard[i].ClientId, scoreboard[i].Name));
            if (isLocalPlayer)
                sb.Append("</link>");
            sb.Append(": ");
            sb.Append(scoreboard[i].Count);
        }

        return sb.ToString();
    }

    public static string FormatColoredPlayerName(ulong clientId, string name)
    {
        return $"<color={GetKnownPlayerColor(clientId)}>{EscapeRichText(name)}</color>";
    }

    public static string EscapeRichText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string GetKnownPlayerColor(ulong clientId)
    {
        if (latestPlayerColors.TryGetValue(clientId, out var latestColor))
            return QuizNetworkRuntime.NormalizeColorHex(latestColor);

        if (instance && instance.playerColors.TryGetValue(clientId, out var liveColor))
            return QuizNetworkRuntime.NormalizeColorHex(liveColor);

        return QuizNetworkRuntime.DefaultColorForClient(clientId);
    }

    private static string DefaultPlayerName(ulong clientId)
    {
        return $"Player {clientId + 1UL}";
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
            writer.WriteValueSafe(scores[i].ColorHex);
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
            reader.ReadValueSafe(out string colorHex);
            reader.ReadValueSafe(out int score);
            reader.ReadValueSafe(out int typeHints);
            reader.ReadValueSafe(out int shadows);
            scores.Add(new PlayerScore(clientId, name, colorHex, score, typeHints, shadows));
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

    private sealed class NetworkStateSnapshot
    {
        public readonly int Generation;
        public readonly string TypeFilter;
        public readonly List<int> SolvedIds;
        public readonly List<int> HintedIds;
        public readonly List<int> ShadowedIds;
        public readonly float Elapsed;
        public readonly bool Running;
        public readonly List<PlayerScore> Scoreboard;
        public readonly Dictionary<int, ulong> SolvedOwners;

        public NetworkStateSnapshot(
            int generation,
            string typeFilter,
            List<int> solvedIds,
            List<int> hintedIds,
            List<int> shadowedIds,
            float elapsed,
            bool running,
            List<PlayerScore> scoreboard,
            Dictionary<int, ulong> solvedOwners
        )
        {
            Generation = generation;
            TypeFilter = typeFilter;
            SolvedIds = solvedIds ?? new List<int>();
            HintedIds = hintedIds ?? new List<int>();
            ShadowedIds = shadowedIds ?? new List<int>();
            Elapsed = Mathf.Max(0f, elapsed);
            Running = running;
            Scoreboard = scoreboard ?? new List<PlayerScore>();
            SolvedOwners = solvedOwners ?? new Dictionary<int, ulong>();
        }
    }

    private readonly struct PlayerScore
    {
        public readonly ulong ClientId;
        public readonly string Name;
        public readonly string ColorHex;
        public readonly int Count;
        public readonly int TypeHints;
        public readonly int Shadows;

        public PlayerScore(
            ulong clientId,
            string name,
            string colorHex,
            int count,
            int typeHints,
            int shadows
        )
        {
            ClientId = clientId;
            Name = string.IsNullOrWhiteSpace(name) ? "Player" : name;
            ColorHex = QuizNetworkRuntime.NormalizeColorHex(colorHex);
            Count = count;
            TypeHints = Mathf.Max(0, typeHints);
            Shadows = Mathf.Max(0, shadows);
        }
    }

    private sealed class SavedQuizSession
    {
        public readonly int Generation;
        public readonly string TypeFilter;
        public readonly string Key;
        public readonly List<int> SolvedIds;
        public readonly List<int> HintedIds;
        public readonly List<int> ShadowedIds;
        public readonly float Elapsed;
        public readonly bool Running;
        public readonly List<PlayerScore> Scoreboard;
        public readonly Dictionary<int, ulong> SolvedOwners;

        public SavedQuizSession(
            int generation,
            string typeFilter,
            IReadOnlyCollection<int> solvedIds,
            IReadOnlyCollection<int> hintedIds,
            IReadOnlyCollection<int> shadowedIds,
            float elapsed,
            bool running,
            IReadOnlyList<PlayerScore> scoreboard,
            IReadOnlyDictionary<int, ulong> solvedOwners
        )
        {
            Generation = generation;
            TypeFilter = NormalizeSavedTypeFilter(typeFilter);
            Key = KeyFor(generation, typeFilter);
            SolvedIds = solvedIds == null ? new List<int>() : new List<int>(solvedIds);
            HintedIds = hintedIds == null ? new List<int>() : new List<int>(hintedIds);
            ShadowedIds = shadowedIds == null ? new List<int>() : new List<int>(shadowedIds);
            Elapsed = Mathf.Max(0f, elapsed);
            Running = running;
            Scoreboard = scoreboard == null
                ? new List<PlayerScore>()
                : new List<PlayerScore>(scoreboard);
            SolvedOwners = new Dictionary<int, ulong>();
            if (solvedOwners != null)
                foreach (var kv in solvedOwners)
                    SolvedOwners[kv.Key] = kv.Value;
        }

        public bool Matches(int generation, string typeFilter)
        {
            return Generation == generation
                && string.Equals(
                    TypeFilter,
                    NormalizeSavedTypeFilter(typeFilter),
                    System.StringComparison.OrdinalIgnoreCase
                );
        }

        public static string KeyFor(int generation, string typeFilter)
        {
            return $"{generation}|{NormalizeSavedTypeFilter(typeFilter) ?? string.Empty}";
        }
    }

    private static string NormalizeSavedTypeFilter(string typeFilter)
    {
        return string.IsNullOrWhiteSpace(typeFilter) ? null : typeFilter.Trim().ToLowerInvariant();
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
        QuizNetworkRuntime.ReturnToLobbyMenu();
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

    private static bool IsHostControlledAction(CoopAction action)
    {
        return action == CoopAction.Reset
            || action == CoopAction.GiveUp
            || action == CoopAction.ReturnToMenu;
    }
}

public sealed class QuizMultiplayerStatusOverlay : MonoBehaviour, IPointerClickHandler
{
    private const string PlayerCountColor = "#7DD3FC";
    private const float ColorPanelWidth = 176f;
    private const float ColorPanelHeight = 118f;
    private const float ColorSwatchSize = 18f;
    private static string scoreboardText = string.Empty;
    private readonly List<Button> colorButtons = new();
    private readonly List<Image> colorButtonImages = new();
    private readonly List<Outline> colorButtonOutlines = new();
    private TMP_Text label;
    private GameObject colorPickerPanel;
    private bool colorPickerVisible;
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
        canvas.sortingOrder = 700;

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
        rt.anchoredPosition = new Vector2(-8f, -112f);
        rt.sizeDelta = new Vector2(390f, 54f);

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

        CreateColorPicker();
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh)
            return;

        Refresh();
        RefreshColorPicker();
        nextRefresh = Time.unscaledTime + 0.5f;
    }

    private void Refresh()
    {
        if (!label)
            return;

        string scores = string.IsNullOrEmpty(scoreboardText) ? "" : $"\n{scoreboardText}";
        if (NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
        {
            int players = NetworkManager.Singleton.ConnectedClientsIds.Count;
            label.text = $"Co-op host | Players: {FormatPlayerCount(players)}{scores}";
            return;
        }

        label.text = $"Co-op joined{scores}";
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!QuizNetworkRuntime.IsMultiplayerActive)
            return;

        if (label)
            label.ForceMeshUpdate();

        int linkIndex = label
            ? TMP_TextUtilities.FindIntersectingLink(
                label,
                eventData.position,
                eventData.pressEventCamera
            )
            : -1;
        bool clickedLocalName =
            linkIndex >= 0 && label.textInfo.linkInfo[linkIndex].GetLinkID() == "local_color";

        if (!clickedLocalName && string.IsNullOrWhiteSpace(scoreboardText))
            return;

        colorPickerVisible = !colorPickerVisible;
        ApplyColorPickerVisibility();
    }

    private void CreateColorPicker()
    {
        colorPickerPanel = new GameObject("Status Color Picker", typeof(RectTransform));
        colorPickerPanel.transform.SetParent(transform, false);

        var rt = (RectTransform)colorPickerPanel.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-8f, 0f);
        rt.sizeDelta = new Vector2(ColorPanelWidth, ColorPanelHeight);

        var image = colorPickerPanel.AddComponent<Image>();
        image.color = new Color(0.05f, 0.06f, 0.07f, 0.92f);

        var layout = colorPickerPanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(colorPickerPanel.transform, false);
        var title = titleGo.AddComponent<TextMeshProUGUI>();
        title.text = "Select color";
        title.fontSize = 11f;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;
        var titleLayout = titleGo.AddComponent<LayoutElement>();
        titleLayout.minWidth = ColorPanelWidth - 20f;
        titleLayout.preferredWidth = ColorPanelWidth - 20f;
        titleLayout.minHeight = 16f;
        titleLayout.preferredHeight = 16f;

        var gridGo = new GameObject("Swatches", typeof(RectTransform));
        gridGo.transform.SetParent(colorPickerPanel.transform, false);
        ((RectTransform)gridGo.transform).sizeDelta = new Vector2(ColorPanelWidth - 20f, 2f * ColorSwatchSize + 5f);
        var grid = gridGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(ColorSwatchSize, ColorSwatchSize);
        grid.spacing = new Vector2(5f, 5f);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 6;
        var gridLayout = gridGo.AddComponent<LayoutElement>();
        gridLayout.minWidth = ColorPanelWidth - 20f;
        gridLayout.preferredWidth = ColorPanelWidth - 20f;
        gridLayout.minHeight = 2f * ColorSwatchSize + 5f;
        gridLayout.preferredHeight = gridLayout.minHeight;

        foreach (var colorHex in QuizNetworkRuntime.PlayerColorPalette)
            CreateColorSwatch(gridGo.transform, colorHex);

        ApplyColorPickerVisibility();
        RefreshColorPicker();
    }

    private void CreateColorSwatch(Transform parent, string colorHex)
    {
        var go = new GameObject($"Color {colorHex}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(ColorSwatchSize, ColorSwatchSize);

        var image = go.AddComponent<Image>();
        image.color = QuizNetworkRuntime.ColorFromHex(colorHex);

        var outline = go.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.enabled = false;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        PreserveSwatchTint(button);
        button.onClick.AddListener(() =>
        {
            if (QuizMultiplayerCoordinator.TrySetLocalPlayerColor(colorHex))
            {
                colorPickerVisible = false;
                ApplyColorPickerVisibility();
                RefreshColorPicker();
            }
        });

        colorButtons.Add(button);
        colorButtonImages.Add(image);
        colorButtonOutlines.Add(outline);
    }

    private void RefreshColorPicker()
    {
        if (!colorPickerPanel)
            return;

        string currentColor = QuizMultiplayerCoordinator.LocalPlayerColorHex;
        for (int i = 0; i < colorButtons.Count; i++)
        {
            string colorHex = i < QuizNetworkRuntime.PlayerColorPalette.Length
                ? QuizNetworkRuntime.NormalizeColorHex(QuizNetworkRuntime.PlayerColorPalette[i])
                : string.Empty;
            bool selected = string.Equals(
                colorHex,
                currentColor,
                System.StringComparison.OrdinalIgnoreCase
            );
            bool taken = QuizMultiplayerCoordinator.IsPlayerColorTakenByAnother(colorHex);

            if (colorButtons[i])
                colorButtons[i].gameObject.SetActive(!taken || selected);
            if (colorButtons[i])
                colorButtons[i].interactable = !taken || selected;
            if (colorButtonOutlines[i])
                colorButtonOutlines[i].enabled = selected;
            if (colorButtonImages[i])
                colorButtonImages[i].color = QuizNetworkRuntime.ColorFromHex(colorHex);
        }
    }

    private static void PreserveSwatchTint(Button button)
    {
        if (!button)
            return;

        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white;
        colors.colorMultiplier = 1f;
        button.colors = colors;
    }

    private void ApplyColorPickerVisibility()
    {
        if (colorPickerPanel)
            colorPickerPanel.SetActive(colorPickerVisible && QuizNetworkRuntime.IsMultiplayerActive);
    }

    private static string FormatPlayerCount(int players)
    {
        return $"<color={PlayerCountColor}><b>{players}</b></color>";
    }
}

public sealed class QuizMultiplayerChatOverlay : MonoBehaviour
{
    private const string ChatRequestMessage = "pkmnquiz_chat_request";
    private const string ChatBroadcastMessage = "pkmnquiz_chat";
    private const int MessageSize = 1024;
    private const int MaxChatLines = 64;
    private const float MinInputHeight = 28f;
    private const float MaxInputHeight = 72f;
    private const float InputVerticalPadding = 8f;
    private const float MenuChatWidth = 360f;
    private const float MenuChatHeight = 236f;
    private const float MenuChatMessagesHeight = 166f;
    private const float QuizChatWidth = 340f;
    private const float QuizChatHeight = 800f;
    private const float QuizChatMessagesHeight = 730f;
    private const float QuizExpandedChatWidth = 340f;
    private const float QuizExpandedChatHeight = 800f;
    private const float QuizExpandedChatMessagesHeight = 730f;
    private const float QuizDockRight = 24f;
    private const float QuizDockTop = 214f;
    private const float PausedChatWidth = 430f;
    private const float PausedChatHeight = 140f;
    private const float PausedChatMessagesHeight = 70f;
    private const float PausedExpandedChatHeight = 330f;
    private const float PausedExpandedChatMessagesHeight = 260f;

    private static QuizMultiplayerChatOverlay instance;
    private static bool overlayVisible = true;

    private readonly List<GameObject> lineObjects = new();
    private Canvas rootCanvas;
    private CanvasGroup canvasGroup;
    private NetworkManager manager;
    private bool registered;
    private bool registeredAsServer;
    private string appliedLayoutScene;
    private RectTransform lineContainer;
    private LayoutElement messageListLayout;
    private LayoutElement inputRowLayout;
    private LayoutElement inputLayout;
    private ScrollRect scrollRect;
    private TMP_InputField inputField;
    private TMP_Text inputText;
    private Button sendButton;
    private Button expandButton;
    private TMP_Text expandButtonLabel;
    private bool expandedInQuiz;
    private bool fixedHeightDock;
    private float basePanelHeight = 124f;
    private float currentInputHeight = MinInputHeight;

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
        instance.ApplyOverlayVisibility();
        instance.RegisterHandlers();
        instance.ApplyScenePlacement();
    }

    public static void SetOverlayVisible(bool visible)
    {
        overlayVisible = visible;

        if (instance)
            instance.ApplyOverlayVisibility();
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
        canvasGroup = gameObject.GetOrAdd<CanvasGroup>();
        BuildUi();
        ApplyOverlayVisibility();
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

        ApplyOverlayVisibility();
        RegisterHandlers();
        ApplyScenePlacement();
        RefreshSendButton();
        RefreshInputHeight();
    }

    private void ApplyOverlayVisibility()
    {
        if (!canvasGroup)
            canvasGroup = gameObject.GetOrAdd<CanvasGroup>();

        canvasGroup.alpha = overlayVisible ? 1f : 0f;
        canvasGroup.interactable = overlayVisible;
        canvasGroup.blocksRaycasts = overlayVisible;
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
        RefreshInputHeight();

        if (manager.IsServer)
        {
            RelayChatLine(manager.LocalClientId, QuizNetworkRuntime.PlayerNickname, message);
            return;
        }

        using var writer = new FastBufferWriter(
            ChatWriterCapacity(QuizNetworkRuntime.PlayerNickname, message),
            Allocator.Temp
        );
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

        using var writer = new FastBufferWriter(
            ChatWriterCapacity(timestamp, senderName, message) + sizeof(ulong),
            Allocator.Temp
        );
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
        TmpCjkFontFallback.EnsureRegistered();

        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-480f, -116f);
        rt.sizeDelta = new Vector2(QuizChatWidth, QuizChatHeight);

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
        CreateResizeHandle();
        ApplyScenePlacement(force: true);
    }

    private void ApplyScenePlacement(bool force = false)
    {
        var rt = (RectTransform)transform;
        string sceneName = SceneManager.GetActiveScene().name;
        bool mainMenu = string.Equals(sceneName, "MainMenu", System.StringComparison.OrdinalIgnoreCase);
        string layoutKey = mainMenu ? "main-menu" : "quiz-docked";
        if (!force && appliedLayoutScene == layoutKey)
            return;

        appliedLayoutScene = layoutKey;
        SetExpandButtonVisible(false);
        fixedHeightDock = !mainMenu;

        if (mainMenu)
        {
            fixedHeightDock = false;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(252f, -782f);
            rt.sizeDelta = new Vector2(MenuChatWidth, MenuChatHeight);
            basePanelHeight = MenuChatHeight;
            SetMessageListHeight(MenuChatMessagesHeight);
            ApplyPanelHeight();
            RefreshInputHeight();
            return;
        }

        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-QuizDockRight, -QuizDockTop);
        rt.sizeDelta = new Vector2(QuizChatWidth, QuizChatHeight);
        basePanelHeight = expandedInQuiz ? QuizExpandedChatHeight : QuizChatHeight;
        SetDockedMessageListHeight();
        ApplyPanelHeight();
        RefreshInputHeight();
    }

    private static bool IsPauseMenuShowing()
    {
        var pauseMenu = FindFirstObjectByType<PauseMenu>();
        return pauseMenu && pauseMenu.IsShowing;
    }

    private void SetExpandButtonVisible(bool visible)
    {
        if (expandButton)
            expandButton.gameObject.SetActive(visible);

        RefreshExpandButtonLabel();
    }

    private void SetMessageListHeight(float height)
    {
        if (!messageListLayout)
            return;

        messageListLayout.minHeight = height;
        messageListLayout.preferredHeight = height;
    }

    private void ApplyPanelHeight()
    {
        var rt = (RectTransform)transform;
        var size = rt.sizeDelta;
        size.y = fixedHeightDock
            ? basePanelHeight
            : basePanelHeight + Mathf.Max(0f, currentInputHeight - MinInputHeight);
        rt.sizeDelta = size;
    }

    private void OnChatInputChanged(string value)
    {
        RefreshSendButton();
        RefreshInputHeight();
    }

    private void RefreshInputHeight()
    {
        if (!inputField || !inputText || !inputRowLayout || !inputLayout)
            return;

        var inputRt = (RectTransform)inputField.transform;
        float width = inputRt.rect.width - 16f;
        if (width <= 1f && inputRt.parent is RectTransform parentRt)
            width = parentRt.rect.width - 16f - 64f;
        if (width <= 1f)
            return;

        string value = string.IsNullOrEmpty(inputField.text) ? "Message" : inputField.text;
        float preferred = inputText.GetPreferredValues(value, width, 0f).y + InputVerticalPadding;
        float height = Mathf.Clamp(Mathf.Ceil(preferred), MinInputHeight, MaxInputHeight);
        if (Mathf.Abs(height - currentInputHeight) < 0.5f)
            return;

        currentInputHeight = height;
        inputRowLayout.minHeight = height;
        inputRowLayout.preferredHeight = height;
        inputLayout.minHeight = height;
        inputLayout.preferredHeight = height;
        if (fixedHeightDock)
            SetDockedMessageListHeight();
        ApplyPanelHeight();
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
    }

    private void SetDockedMessageListHeight()
    {
        var rt = (RectTransform)transform;
        float available = rt.sizeDelta.y - currentInputHeight - 52f;
        SetMessageListHeight(Mathf.Max(120f, available));
    }

    private void DragBy(Vector2 screenDelta)
    {
        if (SceneManager.GetActiveScene().name.Equals("MainMenu", System.StringComparison.OrdinalIgnoreCase))
            return;

        var rt = (RectTransform)transform;
        float scale = rootCanvas ? rootCanvas.scaleFactor : 1f;
        rt.anchoredPosition += screenDelta / Mathf.Max(0.01f, scale);
    }

    private void ResizeBy(Vector2 screenDelta)
    {
        if (SceneManager.GetActiveScene().name.Equals("MainMenu", System.StringComparison.OrdinalIgnoreCase))
            return;

        var rt = (RectTransform)transform;
        float scale = rootCanvas ? rootCanvas.scaleFactor : 1f;
        Vector2 delta = screenDelta / Mathf.Max(0.01f, scale);
        Vector2 size = rt.sizeDelta;
        size.x = Mathf.Clamp(size.x + delta.x, 260f, 620f);
        size.y = Mathf.Clamp(size.y - delta.y, 180f, 880f);
        rt.sizeDelta = size;
        basePanelHeight = size.y;
        fixedHeightDock = true;
        SetDockedMessageListHeight();
        ApplyPanelHeight();
        LayoutRebuilder.MarkLayoutForRebuild(rt);
    }

    private void CreateResizeHandle()
    {
        var go = new GameObject("Resize Handle", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-4f, 4f);
        rt.sizeDelta = new Vector2(18f, 18f);

        var layout = go.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);
        image.raycastTarget = true;

        go.AddComponent<ChatResizeHandle>().Initialize(this);
    }

    private void CreateHeader()
    {
        var row = UiObject("Header");
        var rowImage = row.AddComponent<Image>();
        rowImage.color = new Color(1f, 1f, 1f, 0f);
        rowImage.raycastTarget = true;
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = 18f;
        rowElement.preferredHeight = 18f;
        row.AddComponent<ChatDragHandle>().Initialize(this);

        var labelGo = new GameObject("Title", typeof(RectTransform));
        labelGo.transform.SetParent(row.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        TmpCjkFontFallback.ApplyTo(label);
        label.text = "Chat";
        label.fontSize = 13f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;

        var labelLayout = labelGo.AddComponent<LayoutElement>();
        labelLayout.minHeight = 18f;
        labelLayout.preferredHeight = 18f;
        labelLayout.flexibleWidth = 1f;

        expandButton = CreateHeaderButton(row.transform);
        RefreshExpandButtonLabel();
    }

    private Button CreateHeaderButton(Transform parent)
    {
        var go = new GameObject("Expand Chat", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.16f, 0.35f, 0.70f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(ToggleExpandedInQuiz);

        var layout = go.AddComponent<LayoutElement>();
        layout.minWidth = 24f;
        layout.preferredWidth = 24f;
        layout.minHeight = 18f;
        layout.preferredHeight = 18f;

        var labelGo = new GameObject("Text", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        expandButtonLabel = labelGo.AddComponent<TextMeshProUGUI>();
        TmpCjkFontFallback.ApplyTo(expandButtonLabel);
        expandButtonLabel.fontSize = 14f;
        expandButtonLabel.fontStyle = FontStyles.Bold;
        expandButtonLabel.color = Color.white;
        expandButtonLabel.alignment = TextAlignmentOptions.Center;
        expandButtonLabel.raycastTarget = false;
        return button;
    }

    private void ToggleExpandedInQuiz()
    {
        expandedInQuiz = !expandedInQuiz;
        appliedLayoutScene = null;
        RefreshExpandButtonLabel();
        ApplyScenePlacement(force: true);
    }

    private void RefreshExpandButtonLabel()
    {
        if (expandButtonLabel)
            expandButtonLabel.text = expandedInQuiz ? "-" : "+";
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
        rowLayout.childAlignment = TextAnchor.LowerLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        inputRowLayout = row.AddComponent<LayoutElement>();
        inputRowLayout.minHeight = MinInputHeight;
        inputRowLayout.preferredHeight = MinInputHeight;

        inputField = CreateInput(row.transform);
        inputField.onValueChanged.AddListener(OnChatInputChanged);
        inputField.onSubmit.AddListener(_ => SendCurrentMessage());

        sendButton = CreateButton(row.transform, "Send", SendCurrentMessage);
        RefreshSendButton();
        RefreshInputHeight();
    }

    private TMP_InputField CreateInput(Transform parent)
    {
        var go = new GameObject("Chat Input", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.94f);

        inputLayout = go.AddComponent<LayoutElement>();
        inputLayout.minHeight = MinInputHeight;
        inputLayout.preferredHeight = MinInputHeight;
        inputLayout.flexibleWidth = 1f;

        var input = go.AddComponent<TMP_InputField>();
        input.characterLimit = 0;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.lineType = TMP_InputField.LineType.MultiLineSubmit;
        input.richText = false;

        var viewportGo = new GameObject("Text Area", typeof(RectTransform));
        viewportGo.transform.SetParent(go.transform, false);
        var viewport = (RectTransform)viewportGo.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(8f, 4f);
        viewport.offsetMax = new Vector2(-8f, -4f);
        viewportGo.AddComponent<RectMask2D>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(viewportGo.transform, false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var text = textGo.AddComponent<TextMeshProUGUI>();
        TmpCjkFontFallback.ApplyTo(text);
        text.fontSize = 13f;
        text.color = new Color(0.05f, 0.06f, 0.07f, 1f);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = false;
        inputText = text;

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGo.transform.SetParent(viewportGo.transform, false);
        var placeholderRt = (RectTransform)placeholderGo.transform;
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;

        var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
        TmpCjkFontFallback.ApplyTo(placeholder);
        placeholder.text = "Message";
        placeholder.fontSize = 13f;
        placeholder.color = new Color(0.2f, 0.25f, 0.3f, 0.55f);
        placeholder.alignment = TextAlignmentOptions.TopLeft;
        placeholder.textWrappingMode = TextWrappingModes.Normal;
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
        TmpCjkFontFallback.ApplyTo(label);
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
        TmpCjkFontFallback.ApplyTo(label);
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
        layout.preferredHeight = 15f;
        lineGo.AddComponent<ChatLinePreferredHeight>().Configure(label, layout, 15f, 3f);

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
        return $"Player{clientId + 1UL}";
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

    private static int ChatWriterCapacity(params string[] values)
    {
        int needed = 32;
        foreach (string value in values)
            needed += 8 + System.Text.Encoding.UTF8.GetByteCount(value ?? string.Empty);

        return Mathf.Max(MessageSize, needed);
    }
}

public sealed class ChatDragHandle : MonoBehaviour, IDragHandler
{
    private QuizMultiplayerChatOverlay owner;

    public void Initialize(QuizMultiplayerChatOverlay chatOverlay)
    {
        owner = chatOverlay;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (owner)
            owner.SendMessage("DragBy", eventData.delta, SendMessageOptions.DontRequireReceiver);
    }
}

public sealed class ChatResizeHandle : MonoBehaviour, IDragHandler
{
    private QuizMultiplayerChatOverlay owner;

    public void Initialize(QuizMultiplayerChatOverlay chatOverlay)
    {
        owner = chatOverlay;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (owner)
            owner.SendMessage("ResizeBy", eventData.delta, SendMessageOptions.DontRequireReceiver);
    }
}

public sealed class ChatLinePreferredHeight : MonoBehaviour
{
    private TMP_Text label;
    private LayoutElement layout;
    private float minHeight;
    private float padding;
    private float lastWidth = -1f;
    private string lastText;

    public void Configure(TMP_Text textLabel, LayoutElement layoutElement, float minimum, float extraPadding)
    {
        label = textLabel;
        layout = layoutElement;
        minHeight = minimum;
        padding = extraPadding;
        Refresh();
    }

    private void LateUpdate()
    {
        Refresh();
    }

    private void OnRectTransformDimensionsChange()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (!label || !layout)
            return;

        var rt = (RectTransform)transform;
        float width = rt.rect.width;
        if (width <= 1f && rt.parent is RectTransform parentRt)
            width = parentRt.rect.width;
        if (width <= 1f)
            return;

        string text = label.text ?? string.Empty;
        if (Mathf.Abs(width - lastWidth) < 0.5f && string.Equals(text, lastText))
            return;

        lastWidth = width;
        lastText = text;

        float height = Mathf.Max(
            minHeight,
            Mathf.Ceil(label.GetPreferredValues(text, width, 0f).y + padding)
        );
        if (Mathf.Abs(layout.preferredHeight - height) < 0.5f)
            return;

        layout.minHeight = height;
        layout.preferredHeight = height;

        if (rt.parent is RectTransform layoutRoot)
            LayoutRebuilder.MarkLayoutForRebuild(layoutRoot);
    }
}

public static class TmpCjkFontFallback
{
    private const string JapaneseSample = "\u65e5\u672c\u8a9e\u304b\u306a\u30ab\u30ca";
    private const string KoreanSample = "\ud55c\uad6d\uc5b4\uac00";
    private static bool attempted;
    private static string[] installedFontNames;

    private enum Coverage
    {
        Japanese,
        Korean,
    }

    private readonly struct FontCandidate
    {
        public readonly string Family;
        public readonly string Style;
        public readonly Coverage Coverage;

        public FontCandidate(string family, string style, Coverage coverage)
        {
            Family = family;
            Style = style;
            Coverage = coverage;
        }
    }

    private static readonly FontCandidate[] Candidates =
    {
        new("Noto Sans CJK JP", "Regular", Coverage.Japanese),
        new("Noto Sans JP", "Regular", Coverage.Japanese),
        new("Yu Gothic", "Regular", Coverage.Japanese),
        new("Meiryo", "Regular", Coverage.Japanese),
        new("MS Gothic", "Regular", Coverage.Japanese),
        new("Hiragino Sans", "W3", Coverage.Japanese),
        new("Hiragino Kaku Gothic ProN", "W3", Coverage.Japanese),
        new("Noto Sans CJK KR", "Regular", Coverage.Korean),
        new("Noto Sans KR", "Regular", Coverage.Korean),
        new("Malgun Gothic", "Regular", Coverage.Korean),
        new("Gulim", "Regular", Coverage.Korean),
        new("Batang", "Regular", Coverage.Korean),
        new("Apple SD Gothic Neo", "Regular", Coverage.Korean),
        new("NanumGothic", "Regular", Coverage.Korean),
    };

    public static void ApplyTo(TMP_Text text)
    {
        EnsureRegistered();
        if (text)
            text.SetAllDirty();
    }

    public static void EnsureRegistered()
    {
        if (attempted)
            return;

        attempted = true;

        var fallbacks = TMP_Settings.fallbackFontAssets;
        if (fallbacks == null)
        {
            fallbacks = new List<TMP_FontAsset>();
            TMP_Settings.fallbackFontAssets = fallbacks;
        }

        bool hasJapanese = AnyFallbackSupports(fallbacks, JapaneseSample);
        bool hasKorean = AnyFallbackSupports(fallbacks, KoreanSample);

        foreach (var candidate in Candidates)
        {
            if (candidate.Coverage == Coverage.Japanese && hasJapanese)
                continue;
            if (candidate.Coverage == Coverage.Korean && hasKorean)
                continue;
            if (!IsFontInstalled(candidate.Family))
                continue;

            var fontAsset = TMP_FontAsset.CreateFontAsset(candidate.Family, candidate.Style, 90);
            if (!fontAsset)
                continue;

            fontAsset.name = $"Runtime {candidate.Family} {candidate.Style} TMP Fallback";
            fontAsset.hideFlags = HideFlags.HideAndDontSave;
            fontAsset.isMultiAtlasTexturesEnabled = true;

            string sample = candidate.Coverage == Coverage.Japanese ? JapaneseSample : KoreanSample;
            if (!SupportsSample(fontAsset, sample))
            {
                UnityEngine.Object.Destroy(fontAsset);
                continue;
            }

            fallbacks.Add(fontAsset);
            if (candidate.Coverage == Coverage.Japanese)
                hasJapanese = true;
            else
                hasKorean = true;

            if (hasJapanese && hasKorean)
                break;
        }
    }

    private static bool IsFontInstalled(string family)
    {
        if (installedFontNames == null)
            installedFontNames = Font.GetOSInstalledFontNames();

        foreach (var fontName in installedFontNames)
            if (string.Equals(fontName, family, System.StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static bool AnyFallbackSupports(List<TMP_FontAsset> fallbacks, string sample)
    {
        if (fallbacks == null)
            return false;

        foreach (var fallback in fallbacks)
            if (fallback && SupportsSample(fallback, sample))
                return true;

        return false;
    }

    private static bool SupportsSample(TMP_FontAsset fontAsset, string sample)
    {
        uint[] missing;
        return fontAsset && fontAsset.HasCharacters(sample, out missing, false, true);
    }
}
