using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
        IReadOnlyDictionary<int, ulong> solvedOwners = instance ? instance.solvedByClientId : null;
        var solvedIds = BuildSolvedIdsSnapshot(quizManager.SolvedIds, solvedOwners);

        var session = new SavedQuizSession(
            quizManager.CurrentQuizGeneration,
            quizManager.CurrentTypeFilter,
            solvedIds,
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

    private static List<int> BuildSolvedIdsSnapshot(
        IReadOnlyCollection<int> solvedIds,
        IReadOnlyDictionary<int, ulong> solvedOwners
    )
    {
        var ids = new HashSet<int>();

        if (solvedIds != null)
        {
            foreach (var id in solvedIds)
                ids.Add(id);
        }

        if (solvedOwners != null)
        {
            foreach (var id in solvedOwners.Keys)
                ids.Add(id);
        }

        var snapshot = new List<int>(ids);
        snapshot.Sort();
        return snapshot;
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
        if (
            !feedback.HasValue
            || !manager
            || !manager.IsServer
            || manager.CustomMessagingManager == null
        )
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

        if (!session.Matches(quiz.CurrentQuizGeneration, quiz.CurrentTypeFilter))
        {
            if (!quiz.IsReadyForSavedMultiplayerSessionRestore)
                return true;

            restoreSavedQuizSessionKey = null;
            return false;
        }

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
            session.SolvedIdsForRestore(),
            session.HintedIds,
            session.ShadowedIds,
            session.Elapsed,
            session.Running
        );

        if (quiz.HasPendingSavedMultiplayerSessionRestore)
            return true;

        restoreSavedQuizSessionKey = null;
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
                string message =
                    $"{QuizNetworkRuntime.NormalizeNickname(playerName)} left the quiz.";
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
        return SceneManager
            .GetActiveScene()
            .name.Equals("Quiz", System.StringComparison.OrdinalIgnoreCase);
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
        WriteIds(writer, BuildSolvedIdsSnapshot(quiz.SolvedIds, solvedByClientId));
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

        if (
            !manager
            || !manager.IsListening
            || !manager.IsServer
            || manager.CustomMessagingManager == null
        )
            return;

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
        string message =
            $"{QuizNetworkRuntime.NormalizeNickname(playerName)} joined the {destination}.";
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
                new PlayerScore(
                    clientId,
                    playerNames[clientId],
                    colorHex,
                    score,
                    typeHints,
                    shadows
                )
            );
        }

        return scores;
    }

    private static void ApplyScoreboard(List<PlayerScore> scoreboard)
    {
        latestScoreboard =
            scoreboard == null ? new List<PlayerScore>() : new List<PlayerScore>(scoreboard);

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
            HintedIds = hintedIds ?? new List<int>();
            ShadowedIds = shadowedIds ?? new List<int>();
            Elapsed = Mathf.Max(0f, elapsed);
            Running = running;
            Scoreboard = scoreboard ?? new List<PlayerScore>();
            SolvedOwners = solvedOwners ?? new Dictionary<int, ulong>();
            SolvedIds = BuildSolvedIdsSnapshot(solvedIds, SolvedOwners);
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
            HintedIds = hintedIds == null ? new List<int>() : new List<int>(hintedIds);
            ShadowedIds = shadowedIds == null ? new List<int>() : new List<int>(shadowedIds);
            Elapsed = Mathf.Max(0f, elapsed);
            Running = running;
            Scoreboard =
                scoreboard == null ? new List<PlayerScore>() : new List<PlayerScore>(scoreboard);
            SolvedOwners = new Dictionary<int, ulong>();
            if (solvedOwners != null)
                foreach (var kv in solvedOwners)
                    SolvedOwners[kv.Key] = kv.Value;
            SolvedIds = BuildSolvedIdsSnapshot(solvedIds, SolvedOwners);
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

        public List<int> SolvedIdsForRestore()
        {
            return BuildSolvedIdsSnapshot(SolvedIds, SolvedOwners);
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
    private const float QuizSidePanelWidth = 390f;
    private const float QuizSidePanelX = -1.200012f;
    private const float QuizSidePanelY = -112f;
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
        rt.anchoredPosition = new Vector2(QuizSidePanelX, QuizSidePanelY);
        rt.sizeDelta = new Vector2(QuizSidePanelWidth, 54f);

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
        ((RectTransform)gridGo.transform).sizeDelta = new Vector2(
            ColorPanelWidth - 20f,
            2f * ColorSwatchSize + 5f
        );
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
            string colorHex =
                i < QuizNetworkRuntime.PlayerColorPalette.Length
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
            colorPickerPanel.SetActive(
                colorPickerVisible && QuizNetworkRuntime.IsMultiplayerActive
            );
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
    private const string ChatImageRequestMessage = "pkmnquiz_chat_image_request";
    private const string ChatImageBroadcastMessage = "pkmnquiz_chat_image";
    private const int MessageSize = 32768;
    private const int MaxChatMessageChunkBytes = 24000;
    private const int MaxImageChunkBytes = 24000;
    private const int MaxEncodedImageBytes = 4 * 1024 * 1024;
    private const int MaxImageDimension = 4096;
    private const int MaxChatLines = 256;
    private const int MaxStoredChatImages = 64;
    private const float MinInputHeight = 28f;
    private const float MaxInputHeight = 72f;
    private const float InputVerticalPadding = 8f;
    private const float MenuChatWidth = 360f;
    private const float MenuChatHeight = 236f;
    private const float MenuChatMessagesHeight = 166f;
    private const float MenuChatPanelGap = 18f;
    private const float MenuFallbackLobbyPanelHeight = 300f;
    private const float QuizChatWidth = 390f;
    private const float QuizChatHeight = 800f;
    private const float QuizChatMessagesHeight = 730f;
    private const float QuizExpandedChatWidth = 390f;
    private const float QuizExpandedChatHeight = 800f;
    private const float QuizExpandedChatMessagesHeight = 730f;
    private const float QuizDockX = 1.099976f;
    private const float QuizDockY = -214f;
    private const float PausedChatWidth = 430f;
    private const float PausedChatHeight = 140f;
    private const float PausedChatMessagesHeight = 70f;
    private const float PausedExpandedChatHeight = 330f;
    private const float PausedExpandedChatMessagesHeight = 260f;
    private static readonly Vector2 MenuFallbackLobbyAnchor = new(0.108f, 0.73f);
    private static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg" };

    private static QuizMultiplayerChatOverlay instance;
    private static bool overlayVisible = true;

    private readonly List<GameObject> lineObjects = new();
    private readonly Dictionary<string, PendingChatImage> pendingImages = new();
    private readonly Dictionary<string, ChatImagePayload> imagesById = new();
    private readonly Queue<string> imageIdOrder = new();
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
    private GameObject imageModalRoot;
    private RectTransform imageModalPanelRect;
    private TMP_Text imageModalTitle;
    private Image imageModalImage;
    private RectTransform mainMenuLobbyPanelRect;
    private bool expandedInQuiz;
    private bool fixedHeightDock;
    private float basePanelHeight = 124f;
    private float currentInputHeight = MinInputHeight;
    private readonly Vector3[] mainMenuLobbyPanelCorners = new Vector3[4];

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
        foreach (var payload in imagesById.Values)
            payload?.Destroy();
        imagesById.Clear();
        imageIdOrder.Clear();
        pendingImages.Clear();
        if (imageModalRoot)
            Destroy(imageModalRoot);

        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        if (!QuizNetworkRuntime.IsMultiplayerActive)
            return;

        ChatWindowsDropBridge.Ensure();
        ApplyOverlayVisibility();
        RegisterHandlers();
        ApplyScenePlacement();
        HandleImagePasteShortcut();
        HandleDroppedImageFiles();
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
            manager.CustomMessagingManager.RegisterNamedMessageHandler(
                ChatImageRequestMessage,
                OnChatImageRequestMessage
            );
        }

        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            ChatBroadcastMessage,
            OnChatBroadcastMessage
        );
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            ChatImageBroadcastMessage,
            OnChatImageBroadcastMessage
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
        {
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(ChatRequestMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(ChatImageRequestMessage);
        }

        manager.CustomMessagingManager.UnregisterNamedMessageHandler(ChatBroadcastMessage);
        manager.CustomMessagingManager.UnregisterNamedMessageHandler(ChatImageBroadcastMessage);
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

        foreach (string chunk in SplitMessageForTransport(message))
        {
            if (manager.IsServer)
            {
                RelayChatLine(manager.LocalClientId, QuizNetworkRuntime.PlayerNickname, chunk);
                continue;
            }

            using var writer = new FastBufferWriter(
                ChatWriterCapacity(QuizNetworkRuntime.PlayerNickname, chunk),
                Allocator.Temp
            );
            writer.WriteValueSafe(QuizNetworkRuntime.PlayerNickname);
            writer.WriteValueSafe(chunk);
            manager.CustomMessagingManager.SendNamedMessage(ChatRequestMessage, 0UL, writer);
        }
    }

    private void SendImageBytes(byte[] sourceBytes, string sourceName)
    {
        if (!manager || !manager.IsListening)
            RegisterHandlers();
        if (!manager || !manager.IsListening)
            return;

        if (!TryPrepareImagePayload(sourceBytes, sourceName, out var payload))
            return;

        if (manager.IsServer)
        {
            RelayChatImage(
                manager.LocalClientId,
                QuizNetworkRuntime.PlayerNickname,
                payload.FileName,
                payload.Width,
                payload.Height,
                payload.Bytes
            );
            return;
        }

        string imageId = System.Guid.NewGuid().ToString("N");
        int totalChunks = Mathf.Max(
            1,
            Mathf.CeilToInt(payload.Bytes.Length / (float)MaxImageChunkBytes)
        );
        for (int i = 0; i < totalChunks; i++)
        {
            int start = i * MaxImageChunkBytes;
            int chunkLength = Mathf.Min(MaxImageChunkBytes, payload.Bytes.Length - start);
            using var writer = new FastBufferWriter(
                ChatWriterCapacity(QuizNetworkRuntime.PlayerNickname, imageId, payload.FileName)
                    + chunkLength
                    + 96,
                Allocator.Temp
            );
            writer.WriteValueSafe(QuizNetworkRuntime.PlayerNickname);
            writer.WriteValueSafe(imageId);
            writer.WriteValueSafe(payload.FileName);
            writer.WriteValueSafe(payload.Width);
            writer.WriteValueSafe(payload.Height);
            writer.WriteValueSafe(totalChunks);
            writer.WriteValueSafe(i);
            writer.WriteValueSafe(chunkLength);
            writer.WriteBytesSafe(payload.Bytes, chunkLength, start);
            manager.CustomMessagingManager.SendNamedMessage(
                ChatImageRequestMessage,
                0UL,
                writer,
                NetworkDelivery.ReliableFragmentedSequenced
            );
        }
    }

    private void HandleImagePasteShortcut()
    {
        if (!inputField || !inputField.isFocused)
            return;
        if (!WasPastePressedThisFrame())
            return;

        if (ChatClipboardImageReader.TryGetImageBytes(out var imageBytes, out var sourceName))
            SendImageBytes(imageBytes, sourceName);
    }

    private void HandleDroppedImageFiles()
    {
        foreach (string path in ChatWindowsDropBridge.DrainDroppedFiles())
            TrySendImageFile(path);
    }

    private void TrySendImageFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return;
        if (!IsSupportedImagePath(path))
            return;

        try
        {
            SendImageBytes(System.IO.File.ReadAllBytes(path), System.IO.Path.GetFileName(path));
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Chat] Failed to send image file: {ex.Message}");
        }
    }

    private static bool WasPastePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        return kb != null
            && kb.vKey.wasPressedThisFrame
            && (
                kb.leftCtrlKey.isPressed
                || kb.rightCtrlKey.isPressed
                || kb.leftCommandKey.isPressed
                || kb.rightCommandKey.isPressed
            );
#else
        return UnityEngine.Input.GetKeyDown(KeyCode.V)
            && (
                UnityEngine.Input.GetKey(KeyCode.LeftControl)
                || UnityEngine.Input.GetKey(KeyCode.RightControl)
                || UnityEngine.Input.GetKey(KeyCode.LeftCommand)
                || UnityEngine.Input.GetKey(KeyCode.RightCommand)
            );
#endif
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

    private void OnChatImageRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string requestedName);
        reader.ReadValueSafe(out string imageId);
        reader.ReadValueSafe(out string fileName);
        reader.ReadValueSafe(out int width);
        reader.ReadValueSafe(out int height);
        reader.ReadValueSafe(out int totalChunks);
        reader.ReadValueSafe(out int chunkIndex);
        reader.ReadValueSafe(out int chunkLength);
        if (chunkLength <= 0 || chunkLength > MaxImageChunkBytes)
            return;
        byte[] chunk = new byte[chunkLength];
        reader.ReadBytesSafe(ref chunk, chunkLength);

        if (
            !TryAcceptImageChunk(
                $"request:{senderClientId}:{imageId}",
                senderClientId,
                requestedName,
                null,
                imageId,
                fileName,
                width,
                height,
                totalChunks,
                chunkIndex,
                chunk,
                out var complete
            )
        )
        {
            return;
        }

        RelayChatImage(
            senderClientId,
            requestedName,
            complete.FileName,
            complete.Width,
            complete.Height,
            complete.Bytes
        );
    }

    private void OnChatImageBroadcastMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (manager && manager.IsServer)
            return;

        reader.ReadValueSafe(out ulong chatSenderClientId);
        reader.ReadValueSafe(out string timestamp);
        reader.ReadValueSafe(out string senderName);
        reader.ReadValueSafe(out string imageId);
        reader.ReadValueSafe(out string fileName);
        reader.ReadValueSafe(out int width);
        reader.ReadValueSafe(out int height);
        reader.ReadValueSafe(out int totalChunks);
        reader.ReadValueSafe(out int chunkIndex);
        reader.ReadValueSafe(out int chunkLength);
        if (chunkLength <= 0 || chunkLength > MaxImageChunkBytes)
            return;
        byte[] chunk = new byte[chunkLength];
        reader.ReadBytesSafe(ref chunk, chunkLength);

        if (
            !TryAcceptImageChunk(
                $"broadcast:{imageId}",
                chatSenderClientId,
                senderName,
                timestamp,
                imageId,
                fileName,
                width,
                height,
                totalChunks,
                chunkIndex,
                chunk,
                out var complete
            )
        )
        {
            return;
        }

        AppendImageLine(
            complete.Timestamp,
            complete.SenderClientId,
            ResolveDisplayName(complete.SenderClientId, complete.SenderName),
            complete.ImageId,
            complete.FileName,
            complete.Width,
            complete.Height,
            complete.Bytes
        );
    }

    private void RelayChatImage(
        ulong senderClientId,
        string requestedName,
        string fileName,
        int width,
        int height,
        byte[] imageBytes
    )
    {
        if (!manager || !manager.IsServer || manager.CustomMessagingManager == null)
            return;
        if (imageBytes == null || imageBytes.Length == 0)
            return;

        string senderName = ResolveDisplayName(senderClientId, requestedName);
        string timestamp = CurrentTimestamp();
        string imageId = System.Guid.NewGuid().ToString("N");

        AppendImageLine(
            timestamp,
            senderClientId,
            senderName,
            imageId,
            fileName,
            width,
            height,
            imageBytes
        );
        BroadcastChatImage(
            senderClientId,
            timestamp,
            senderName,
            imageId,
            fileName,
            width,
            height,
            imageBytes
        );
    }

    private void BroadcastChatImage(
        ulong senderClientId,
        string timestamp,
        string senderName,
        string imageId,
        string fileName,
        int width,
        int height,
        byte[] imageBytes
    )
    {
        int totalChunks = Mathf.Max(
            1,
            Mathf.CeilToInt(imageBytes.Length / (float)MaxImageChunkBytes)
        );
        for (int i = 0; i < totalChunks; i++)
        {
            int start = i * MaxImageChunkBytes;
            int chunkLength = Mathf.Min(MaxImageChunkBytes, imageBytes.Length - start);
            using var writer = new FastBufferWriter(
                ChatWriterCapacity(timestamp, senderName, imageId, fileName) + chunkLength + 96,
                Allocator.Temp
            );
            writer.WriteValueSafe(senderClientId);
            writer.WriteValueSafe(timestamp);
            writer.WriteValueSafe(senderName);
            writer.WriteValueSafe(imageId);
            writer.WriteValueSafe(fileName ?? "image.jpg");
            writer.WriteValueSafe(width);
            writer.WriteValueSafe(height);
            writer.WriteValueSafe(totalChunks);
            writer.WriteValueSafe(i);
            writer.WriteValueSafe(chunkLength);
            writer.WriteBytesSafe(imageBytes, chunkLength, start);
            manager.CustomMessagingManager.SendNamedMessageToAll(
                ChatImageBroadcastMessage,
                writer,
                NetworkDelivery.ReliableFragmentedSequenced
            );
        }
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
        bool mainMenu = string.Equals(
            sceneName,
            "MainMenu",
            System.StringComparison.OrdinalIgnoreCase
        );
        string layoutKey = mainMenu ? "main-menu" : "quiz-docked";
        if (!mainMenu && !force && appliedLayoutScene == layoutKey)
            return;

        appliedLayoutScene = layoutKey;
        SetExpandButtonVisible(false);
        fixedHeightDock = !mainMenu;

        if (mainMenu)
        {
            fixedHeightDock = false;
            rt.sizeDelta = new Vector2(MenuChatWidth, MenuChatHeight);
            basePanelHeight = MenuChatHeight;
            SetMessageListHeight(MenuChatMessagesHeight);
            ApplyMainMenuPlacement(rt);
            ApplyPanelHeight();
            RefreshInputHeight();
            return;
        }

        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(QuizDockX, QuizDockY);
        rt.sizeDelta = new Vector2(
            expandedInQuiz ? QuizExpandedChatWidth : QuizChatWidth,
            QuizChatHeight
        );
        basePanelHeight = expandedInQuiz ? QuizExpandedChatHeight : QuizChatHeight;
        SetDockedMessageListHeight();
        ApplyPanelHeight();
        RefreshInputHeight();
    }

    private void ApplyMainMenuPlacement(RectTransform rt)
    {
        rt.pivot = new Vector2(0f, 1f);

        if (TryGetMainMenuLobbyPanel(out var panelRt) && rootCanvas)
        {
            var rootRt = rootCanvas.transform as RectTransform;
            if (rootRt)
            {
                panelRt.GetWorldCorners(mainMenuLobbyPanelCorners);
                Vector2 panelBottomLeft = rootRt.InverseTransformPoint(
                    mainMenuLobbyPanelCorners[0]
                );
                Vector2 panelBottomRight = rootRt.InverseTransformPoint(
                    mainMenuLobbyPanelCorners[3]
                );
                var rootRect = rootRt.rect;

                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(
                    Mathf.Round(panelBottomLeft.x - rootRect.xMin),
                    Mathf.Round(panelBottomLeft.y - rootRect.yMax - MenuChatPanelGap)
                );

                var size = rt.sizeDelta;
                size.x = Mathf.Max(260f, panelBottomRight.x - panelBottomLeft.x);
                rt.sizeDelta = size;
                return;
            }
        }

        rt.anchorMin = MenuFallbackLobbyAnchor;
        rt.anchorMax = MenuFallbackLobbyAnchor;
        rt.anchoredPosition = new Vector2(
            0f,
            -(MenuFallbackLobbyPanelHeight + MenuChatPanelGap)
        );
    }

    private bool TryGetMainMenuLobbyPanel(out RectTransform panelRt)
    {
        if (!mainMenuLobbyPanelRect)
        {
            var panel = FindFirstObjectByType<MultiplayerMenuPanel>();
            if (panel)
                mainMenuLobbyPanelRect = panel.transform as RectTransform;
        }

        panelRt = mainMenuLobbyPanelRect;
        return panelRt;
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
        if (
            SceneManager
                .GetActiveScene()
                .name.Equals("MainMenu", System.StringComparison.OrdinalIgnoreCase)
        )
            return;

        var rt = (RectTransform)transform;
        float scale = rootCanvas ? rootCanvas.scaleFactor : 1f;
        rt.anchoredPosition += screenDelta / Mathf.Max(0.01f, scale);
    }

    private void ResizeBy(Vector2 screenDelta)
    {
        if (
            SceneManager
                .GetActiveScene()
                .name.Equals("MainMenu", System.StringComparison.OrdinalIgnoreCase)
        )
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

    private Button CreateButton(
        Transform parent,
        string labelText,
        UnityEngine.Events.UnityAction onClick
    )
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
            sendButton.interactable = !string.IsNullOrWhiteSpace(
                inputField ? inputField.text : null
            );
    }

    private void AppendLine(
        string timestamp,
        ulong senderClientId,
        string senderName,
        string message
    )
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
            + FormatMessageWithLinks(message);
        label.fontSize = 12f;
        label.color = new Color(0.91f, 0.96f, 1f, 1f);
        label.alignment = TextAlignmentOptions.Left;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.richText = true;
        label.raycastTarget = true;

        var layout = lineGo.AddComponent<LayoutElement>();
        layout.minHeight = 15f;
        layout.preferredHeight = 15f;
        lineGo.AddComponent<ChatLinePreferredHeight>().Configure(label, layout, 15f, 3f);
        lineGo
            .AddComponent<ChatLineInteraction>()
            .Configure(label, $"{timestamp} {senderName}: {message}");

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

    private void AppendImageLine(
        string timestamp,
        ulong senderClientId,
        string senderName,
        string imageId,
        string fileName,
        int width,
        int height,
        byte[] imageBytes
    )
    {
        timestamp = NormalizeTimestamp(timestamp);
        senderName = NormalizeSenderName(senderName);
        imageId = string.IsNullOrWhiteSpace(imageId)
            ? System.Guid.NewGuid().ToString("N")
            : imageId;
        fileName = SanitizeImageFileName(fileName);
        if (imageBytes == null || imageBytes.Length == 0 || !lineContainer)
            return;

        if (!TryCreateImagePayload(imageId, fileName, width, height, imageBytes, out var payload))
            return;

        StoreImagePayload(payload);

        var lineGo = new GameObject("Image Message", typeof(RectTransform));
        lineGo.transform.SetParent(lineContainer, false);

        var lineImage = lineGo.AddComponent<Image>();
        lineImage.color = new Color(0f, 0f, 0f, 0.12f);

        var vertical = lineGo.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 4f;
        vertical.padding = new RectOffset(0, 0, 2, 4);
        vertical.childAlignment = TextAnchor.UpperLeft;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        var headerGo = new GameObject("Header", typeof(RectTransform));
        headerGo.transform.SetParent(lineGo.transform, false);
        var header = headerGo.AddComponent<TextMeshProUGUI>();
        TmpCjkFontFallback.ApplyTo(header);
        header.text =
            $"{QuizMultiplayerCoordinator.EscapeRichText(timestamp)} "
            + $"{QuizMultiplayerCoordinator.FormatColoredPlayerName(senderClientId, senderName)}: "
            + $"<color=#7DD3FC><u>{QuizMultiplayerCoordinator.EscapeRichText(fileName)}</u></color>";
        header.fontSize = 12f;
        header.color = new Color(0.91f, 0.96f, 1f, 1f);
        header.richText = true;
        header.raycastTarget = false;
        var headerLayout = headerGo.AddComponent<LayoutElement>();
        headerLayout.minHeight = 15f;
        headerLayout.preferredHeight = 15f;

        var buttonGo = new GameObject("Preview", typeof(RectTransform));
        buttonGo.transform.SetParent(lineGo.transform, false);
        var preview = buttonGo.AddComponent<Image>();
        preview.sprite = payload.Sprite;
        preview.preserveAspect = true;
        preview.color = Color.white;
        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = preview;
        button.onClick.AddListener(() => ShowImageModal(payload.ImageId));
        float maxWidth = Mathf.Max(120f, ((RectTransform)transform).sizeDelta.x - 28f);
        float aspect =
            payload.Width > 0 && payload.Height > 0 ? payload.Width / (float)payload.Height : 1f;
        float previewWidth = Mathf.Min(maxWidth, Mathf.Max(96f, payload.Width));
        float previewHeight = Mathf.Clamp(previewWidth / Mathf.Max(0.01f, aspect), 72f, 150f);
        if (previewHeight >= 150f)
            previewWidth = previewHeight * aspect;

        var previewLayout = buttonGo.AddComponent<LayoutElement>();
        previewLayout.minHeight = previewHeight;
        previewLayout.preferredHeight = previewHeight;
        previewLayout.minWidth = previewWidth;
        previewLayout.preferredWidth = previewWidth;

        var lineLayout = lineGo.AddComponent<LayoutElement>();
        lineLayout.minHeight = previewHeight + 24f;
        lineLayout.preferredHeight = previewHeight + 24f;

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

    private bool TryAcceptImageChunk(
        string key,
        ulong senderClientId,
        string senderName,
        string timestamp,
        string imageId,
        string fileName,
        int width,
        int height,
        int totalChunks,
        int chunkIndex,
        byte[] chunk,
        out PendingChatImage complete
    )
    {
        complete = null;
        if (
            string.IsNullOrWhiteSpace(key)
            || string.IsNullOrWhiteSpace(imageId)
            || totalChunks <= 0
            || totalChunks > MaxAllowedImageChunks()
            || chunkIndex < 0
            || chunkIndex >= totalChunks
            || chunk == null
            || chunk.Length <= 0
            || chunk.Length > MaxImageChunkBytes
        )
        {
            return false;
        }

        if (!pendingImages.TryGetValue(key, out var pending))
        {
            pending = new PendingChatImage(
                senderClientId,
                senderName,
                timestamp,
                imageId,
                SanitizeImageFileName(fileName),
                Mathf.Max(1, width),
                Mathf.Max(1, height),
                totalChunks
            );
            pendingImages[key] = pending;
        }

        pending.Chunks[chunkIndex] = chunk;
        if (!pending.IsComplete)
            return false;

        pendingImages.Remove(key);
        int byteCount = 0;
        for (int i = 0; i < pending.Chunks.Length; i++)
        {
            byteCount += pending.Chunks[i].Length;
            if (byteCount > MaxEncodedImageBytes)
                return false;
        }

        if (byteCount <= 0)
            return false;

        byte[] bytes = new byte[byteCount];
        int offset = 0;
        for (int i = 0; i < pending.Chunks.Length; i++)
        {
            byte[] part = pending.Chunks[i];
            System.Buffer.BlockCopy(part, 0, bytes, offset, part.Length);
            offset += part.Length;
        }

        pending.Bytes = bytes;
        complete = pending;
        return true;
    }

    private bool TryPrepareImagePayload(
        byte[] sourceBytes,
        string sourceName,
        out PreparedChatImage payload
    )
    {
        payload = default;
        if (sourceBytes == null || sourceBytes.Length == 0)
            return false;

        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!source.LoadImage(sourceBytes))
        {
            Destroy(source);
            return false;
        }

        int srcW = source.width;
        int srcH = source.height;
        if (
            sourceBytes.Length <= MaxEncodedImageBytes
            && Mathf.Max(srcW, srcH) <= MaxImageDimension
        )
        {
            payload = new PreparedChatImage(
                SanitizeImageFileName(sourceName, GuessImageExtension(sourceBytes)),
                srcW,
                srcH,
                sourceBytes
            );
            Destroy(source);
            return true;
        }

        Texture2D resized = null;
        Texture2D working = source;
        float scale = Mathf.Min(1f, MaxImageDimension / (float)Mathf.Max(srcW, srcH));
        int outW = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
        int outH = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));
        if (outW != srcW || outH != srcH)
        {
            resized = ResizeTexture(source, outW, outH);
            working = resized;
        }

        byte[] encoded = null;
        int quality = 88;
        while (quality >= 58)
        {
            encoded = working.EncodeToJPG(quality);
            if (encoded != null && encoded.Length <= MaxEncodedImageBytes)
                break;
            quality -= 10;
        }

        while (encoded != null && encoded.Length > MaxEncodedImageBytes && outW > 160 && outH > 160)
        {
            outW = Mathf.Max(1, Mathf.RoundToInt(outW * 0.82f));
            outH = Mathf.Max(1, Mathf.RoundToInt(outH * 0.82f));
            if (resized)
                Destroy(resized);
            resized = ResizeTexture(source, outW, outH);
            working = resized;
            encoded = working.EncodeToJPG(52);
        }

        if (resized)
            Destroy(resized);
        Destroy(source);

        if (encoded == null || encoded.Length == 0 || encoded.Length > MaxEncodedImageBytes)
            return false;

        payload = new PreparedChatImage(
            SanitizeImageFileName(ChangeImageExtension(sourceName, ".jpg"), ".jpg"),
            outW,
            outH,
            encoded
        );
        return true;
    }

    private static Texture2D ResizeTexture(Texture2D source, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        var resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
        resized.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        resized.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return resized;
    }

    private bool TryCreateImagePayload(
        string imageId,
        string fileName,
        int width,
        int height,
        byte[] imageBytes,
        out ChatImagePayload payload
    )
    {
        payload = null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(imageBytes))
        {
            Destroy(tex);
            return false;
        }

        var sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f
        );
        payload = new ChatImagePayload(
            imageId,
            SanitizeImageFileName(fileName),
            Mathf.Max(width, tex.width),
            Mathf.Max(height, tex.height),
            tex,
            sprite
        );
        return true;
    }

    private void StoreImagePayload(ChatImagePayload payload)
    {
        if (payload == null)
            return;

        if (imagesById.TryGetValue(payload.ImageId, out var existing))
            existing.Destroy();

        imagesById[payload.ImageId] = payload;
        imageIdOrder.Enqueue(payload.ImageId);

        while (imageIdOrder.Count > MaxStoredChatImages)
        {
            string oldId = imageIdOrder.Dequeue();
            if (imagesById.Remove(oldId, out var oldPayload))
                oldPayload.Destroy();
        }
    }

    private void ShowImageModal(string imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId) || !imagesById.TryGetValue(imageId, out var payload))
            return;

        EnsureImageModal();
        if (!imageModalRoot || !imageModalImage)
            return;

        imageModalTitle.text = payload.FileName;
        imageModalImage.sprite = payload.Sprite;
        imageModalImage.preserveAspect = true;

        float canvasW = 1120f;
        float canvasH = 820f;
        if (imageModalRoot.transform.parent is RectTransform canvasRt)
        {
            canvasW = Mathf.Max(480f, canvasRt.rect.width);
            canvasH = Mathf.Max(360f, canvasRt.rect.height);
        }

        var rt = (RectTransform)imageModalImage.transform;
        float maxW = Mathf.Min(1280f, canvasW - 80f);
        float maxH = Mathf.Min(820f, canvasH - 132f);
        float aspect = payload.Width / (float)Mathf.Max(1, payload.Height);
        float w = maxW;
        float h = w / Mathf.Max(0.01f, aspect);
        if (h > maxH)
        {
            h = maxH;
            w = h * aspect;
        }
        rt.sizeDelta = new Vector2(w, h);
        if (imageModalPanelRect)
            imageModalPanelRect.sizeDelta = new Vector2(
                Mathf.Min(canvasW - 40f, Mathf.Max(520f, w + 36f)),
                Mathf.Min(canvasH - 40f, h + 92f)
            );
        imageModalRoot.SetActive(true);
    }

    private void EnsureImageModal()
    {
        if (imageModalRoot)
            return;

        var canvas = rootCanvas ? rootCanvas : GetComponentInParent<Canvas>();
        if (!canvas)
            return;

        imageModalRoot = new GameObject("Chat Image Modal", typeof(RectTransform));
        imageModalRoot.transform.SetParent(canvas.transform, false);
        var rootRt = (RectTransform)imageModalRoot.transform;
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var scrim = imageModalRoot.AddComponent<Image>();
        scrim.color = new Color(0f, 0f, 0f, 0.72f);

        var closeArea = imageModalRoot.AddComponent<Button>();
        closeArea.targetGraphic = scrim;
        closeArea.onClick.AddListener(() => imageModalRoot.SetActive(false));

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(imageModalRoot.transform, false);
        var panelRt = (RectTransform)panel.transform;
        imageModalPanelRect = panelRt;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(1120f, 820f);

        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.05f, 0.06f, 0.07f, 0.96f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 18);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var headerGo = new GameObject("Header", typeof(RectTransform));
        headerGo.transform.SetParent(panel.transform, false);
        var headerLayout = headerGo.AddComponent<LayoutElement>();
        headerLayout.minWidth = 1084f;
        headerLayout.preferredWidth = 1084f;
        headerLayout.minHeight = 30f;
        headerLayout.preferredHeight = 30f;

        var header = headerGo.AddComponent<HorizontalLayoutGroup>();
        header.childAlignment = TextAnchor.MiddleCenter;
        header.childControlWidth = true;
        header.childControlHeight = true;
        header.childForceExpandWidth = true;
        header.childForceExpandHeight = false;
        header.spacing = 8f;

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(headerGo.transform, false);
        imageModalTitle = titleGo.AddComponent<TextMeshProUGUI>();
        TmpCjkFontFallback.ApplyTo(imageModalTitle);
        imageModalTitle.fontSize = 16f;
        imageModalTitle.fontStyle = FontStyles.Bold;
        imageModalTitle.color = Color.white;
        imageModalTitle.alignment = TextAlignmentOptions.MidlineLeft;
        imageModalTitle.textWrappingMode = TextWrappingModes.NoWrap;
        imageModalTitle.overflowMode = TextOverflowModes.Ellipsis;
        imageModalTitle.richText = false;
        imageModalTitle.raycastTarget = false;
        var titleLayout = titleGo.AddComponent<LayoutElement>();
        titleLayout.flexibleWidth = 1f;
        titleLayout.minWidth = 320f;
        titleLayout.preferredWidth = 984f;
        titleLayout.minHeight = 30f;
        titleLayout.preferredHeight = 30f;

        var closeButton = CreateButton(
            headerGo.transform,
            "Close",
            () => imageModalRoot.SetActive(false)
        );
        var closeLayout = closeButton.GetComponent<LayoutElement>();
        closeLayout.minWidth = 72f;
        closeLayout.preferredWidth = 72f;
        closeLayout.minHeight = 30f;
        closeLayout.preferredHeight = 30f;

        var imageGo = new GameObject("Image", typeof(RectTransform));
        imageGo.transform.SetParent(panel.transform, false);
        imageModalImage = imageGo.AddComponent<Image>();
        imageModalImage.color = Color.white;
        imageModalImage.preserveAspect = true;

        imageModalRoot.SetActive(false);
    }

    private static bool IsSupportedImagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string ext = System.IO.Path.GetExtension(path);
        for (int i = 0; i < SupportedImageExtensions.Length; i++)
        {
            if (
                string.Equals(
                    ext,
                    SupportedImageExtensions[i],
                    System.StringComparison.OrdinalIgnoreCase
                )
            )
                return true;
        }

        return false;
    }

    private static string SanitizeImageFileName(string fileName, string fallbackExtension = ".jpg")
    {
        fileName = string.IsNullOrWhiteSpace(fileName)
            ? null
            : System.IO.Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"image{fallbackExtension}";

        foreach (char ch in System.IO.Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(ch, '_');

        if (string.IsNullOrWhiteSpace(System.IO.Path.GetExtension(fileName)))
            fileName += fallbackExtension;

        return fileName;
    }

    private static string ChangeImageExtension(string fileName, string extension)
    {
        fileName = string.IsNullOrWhiteSpace(fileName)
            ? "image"
            : System.IO.Path.GetFileName(fileName);
        string name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(name))
            name = "image";
        return name + extension;
    }

    private static string GuessImageExtension(byte[] bytes)
    {
        if (bytes != null && bytes.Length >= 4)
        {
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return ".png";
            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
                return ".jpg";
        }

        return ".jpg";
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

    private static IEnumerable<string> SplitMessageForTransport(string message)
    {
        if (string.IsNullOrEmpty(message))
            yield break;

        var sb = new System.Text.StringBuilder();
        int byteCount = 0;
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(message);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            int elementBytes = System.Text.Encoding.UTF8.GetByteCount(element);
            if (sb.Length > 0 && byteCount + elementBytes > MaxChatMessageChunkBytes)
            {
                yield return sb.ToString();
                sb.Clear();
                byteCount = 0;
            }

            sb.Append(element);
            byteCount += elementBytes;
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private static string FormatMessageWithLinks(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        var matches = System.Text.RegularExpressions.Regex.Matches(
            message,
            @"(?i)(https?://[^\s<]+|www\.[^\s<]+)"
        );
        if (matches.Count == 0)
            return QuizMultiplayerCoordinator.EscapeRichText(message);

        var sb = new System.Text.StringBuilder(message.Length + matches.Count * 32);
        int cursor = 0;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Index > cursor)
                sb.Append(
                    QuizMultiplayerCoordinator.EscapeRichText(
                        message.Substring(cursor, match.Index - cursor)
                    )
                );

            string rawMatch = match.Value;
            string linkText = TrimTrailingUrlPunctuation(rawMatch, out string trailingText);
            string url = NormalizeUrlForOpen(linkText);
            if (string.IsNullOrEmpty(url))
            {
                sb.Append(QuizMultiplayerCoordinator.EscapeRichText(rawMatch));
            }
            else
            {
                sb.Append("<link=\"");
                sb.Append(EscapeLinkId(url));
                sb.Append("\"><u><color=#7DD3FC>");
                sb.Append(QuizMultiplayerCoordinator.EscapeRichText(linkText));
                sb.Append("</color></u></link>");
                sb.Append(QuizMultiplayerCoordinator.EscapeRichText(trailingText));
            }

            cursor = match.Index + rawMatch.Length;
        }

        if (cursor < message.Length)
            sb.Append(QuizMultiplayerCoordinator.EscapeRichText(message.Substring(cursor)));

        return sb.ToString();
    }

    private static string TrimTrailingUrlPunctuation(string value, out string trailing)
    {
        int end = value.Length;
        while (end > 0 && ".,;:!?)\"]}".IndexOf(value[end - 1]) >= 0)
            end--;

        trailing = end < value.Length ? value.Substring(end) : string.Empty;
        return value.Substring(0, end);
    }

    private static string NormalizeUrlForOpen(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim().Replace("\"", "%22").Replace("<", "%3C").Replace(">", "%3E");
        if (value.StartsWith("www.", System.StringComparison.OrdinalIgnoreCase))
            value = "https://" + value;

        if (
            System.Uri.TryCreate(value, System.UriKind.Absolute, out var uri)
            && (
                string.Equals(
                    uri.Scheme,
                    System.Uri.UriSchemeHttp,
                    System.StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    uri.Scheme,
                    System.Uri.UriSchemeHttps,
                    System.StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            return uri.AbsoluteUri;
        }

        return null;
    }

    private static string EscapeLinkId(string value)
    {
        return value?.Replace("\"", "%22").Replace("<", "%3C").Replace(">", "%3E") ?? string.Empty;
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
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                    sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            if (char.IsControl(ch))
                continue;

            sb.Append(ch);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }

    private static int ChatWriterCapacity(params string[] values)
    {
        int needed = 32;
        foreach (string value in values)
            needed += FastBufferWriter.GetWriteSize(value ?? string.Empty);

        return Mathf.Max(MessageSize, needed);
    }

    private static int MaxAllowedImageChunks()
    {
        return Mathf.CeilToInt(MaxEncodedImageBytes / (float)MaxImageChunkBytes) + 1;
    }

    private readonly struct PreparedChatImage
    {
        public readonly string FileName;
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Bytes;

        public PreparedChatImage(string fileName, int width, int height, byte[] bytes)
        {
            FileName = fileName;
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            Bytes = bytes;
        }
    }

    private sealed class PendingChatImage
    {
        public readonly ulong SenderClientId;
        public readonly string SenderName;
        public readonly string Timestamp;
        public readonly string ImageId;
        public readonly string FileName;
        public readonly int Width;
        public readonly int Height;
        public readonly byte[][] Chunks;
        public byte[] Bytes;

        public bool IsComplete
        {
            get
            {
                for (int i = 0; i < Chunks.Length; i++)
                    if (Chunks[i] == null || Chunks[i].Length == 0)
                        return false;

                return true;
            }
        }

        public PendingChatImage(
            ulong senderClientId,
            string senderName,
            string timestamp,
            string imageId,
            string fileName,
            int width,
            int height,
            int totalChunks
        )
        {
            SenderClientId = senderClientId;
            SenderName = senderName;
            Timestamp = timestamp;
            ImageId = imageId;
            FileName = fileName;
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            Chunks = new byte[Mathf.Max(1, totalChunks)][];
        }
    }

    private sealed class ChatImagePayload
    {
        public readonly string ImageId;
        public readonly string FileName;
        public readonly int Width;
        public readonly int Height;
        public readonly Texture2D Texture;
        public readonly Sprite Sprite;

        public ChatImagePayload(
            string imageId,
            string fileName,
            int width,
            int height,
            Texture2D texture,
            Sprite sprite
        )
        {
            ImageId = imageId;
            FileName = fileName;
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            Texture = texture;
            Sprite = sprite;
        }

        public void Destroy()
        {
            if (Sprite)
                UnityEngine.Object.Destroy(Sprite);
            if (Texture)
                UnityEngine.Object.Destroy(Texture);
        }
    }
}

public sealed class ChatLineInteraction
    : MonoBehaviour,
        IPointerDownHandler,
        IBeginDragHandler,
        IDragHandler,
        IPointerUpHandler,
        IPointerClickHandler
{
    private static readonly Color SelectionColor = new(0.24f, 0.50f, 1f, 0.42f);
    private static ChatLineInteraction activeSelection;

    private readonly List<Image> selectionImages = new();
    private TMP_Text label;
    private string copyText;
    private bool pointerDown;
    private bool draggedSelection;
    private int selectionAnchor = -1;
    private int selectionFocus = -1;

    public void Configure(TMP_Text lineLabel, string plainText)
    {
        label = lineLabel;
        copyText = plainText ?? string.Empty;
    }

    private void Update()
    {
        if (activeSelection == this && HasSelection && WasCopyPressedThisFrame())
            GUIUtility.systemCopyBuffer = GetSelectedText();
    }

    private void OnDestroy()
    {
        if (activeSelection == this)
            activeSelection = null;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!label || eventData.button != PointerEventData.InputButton.Left)
            return;

        if (activeSelection && activeSelection != this)
            activeSelection.ClearSelection();
        activeSelection = this;

        pointerDown = true;
        draggedSelection = false;
        selectionAnchor = CharacterIndexFromPointer(eventData);
        selectionFocus = selectionAnchor;
        ClearSelection();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        UpdateDraggedSelection(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        UpdateDraggedSelection(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!pointerDown || eventData.button != PointerEventData.InputButton.Left)
            return;

        if (draggedSelection)
        {
            UpdateDraggedSelection(eventData);
            string selected = GetSelectedText();
            if (!string.IsNullOrEmpty(selected))
                GUIUtility.systemCopyBuffer = selected;
        }

        pointerDown = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!label)
            return;
        if (draggedSelection && HasSelection)
            return;

        label.ForceMeshUpdate();
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(
            label,
            eventData.position,
            eventData.pressEventCamera
        );

        if (eventData.button == PointerEventData.InputButton.Left && linkIndex >= 0)
        {
            string url = label.textInfo.linkInfo[linkIndex].GetLinkID();
            if (IsSafeWebUrl(url))
                Application.OpenURL(url);
            return;
        }

        GUIUtility.systemCopyBuffer = copyText;
        SelectWholeLine();
    }

    private void UpdateDraggedSelection(PointerEventData eventData)
    {
        if (!pointerDown || !label)
            return;

        selectionFocus = CharacterIndexFromPointer(eventData);
        draggedSelection = true;
        RefreshSelection();
    }

    private int CharacterIndexFromPointer(PointerEventData eventData)
    {
        label.ForceMeshUpdate();
        int count = label.textInfo.characterCount;
        if (count <= 0)
            return -1;

        int index = TMP_TextUtilities.FindNearestCharacter(
            label,
            eventData.position,
            eventData.pressEventCamera,
            false
        );
        return Mathf.Clamp(index, 0, count - 1);
    }

    private bool HasSelection
    {
        get
        {
            GetSelectionRange(out int start, out int end);
            return start >= 0 && end > start;
        }
    }

    private void GetSelectionRange(out int start, out int end)
    {
        start = -1;
        end = -1;
        if (!label || selectionAnchor < 0 || selectionFocus < 0)
            return;

        int count = label.textInfo.characterCount;
        if (count <= 0)
            return;

        start = Mathf.Clamp(Mathf.Min(selectionAnchor, selectionFocus), 0, count - 1);
        end = Mathf.Clamp(Mathf.Max(selectionAnchor, selectionFocus) + 1, 0, count);
    }

    private void SelectWholeLine()
    {
        if (!label)
            return;

        label.ForceMeshUpdate();
        int count = label.textInfo.characterCount;
        if (count <= 0)
            return;

        if (activeSelection && activeSelection != this)
            activeSelection.ClearSelection();
        activeSelection = this;
        selectionAnchor = 0;
        selectionFocus = count - 1;
        draggedSelection = false;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        ClearSelection();
        if (!label)
            return;

        label.ForceMeshUpdate();
        GetSelectionRange(out int start, out int end);
        if (start < 0 || end <= start)
            return;

        var textInfo = label.textInfo;
        int runLine = -1;
        float runMinX = 0f;
        float runMaxX = 0f;
        float runMinY = 0f;
        float runMaxY = 0f;
        bool hasRun = false;

        for (int i = start; i < end && i < textInfo.characterCount; i++)
        {
            var ch = textInfo.characterInfo[i];
            if (!ch.isVisible)
                continue;

            if (!hasRun || ch.lineNumber != runLine || ch.bottomLeft.x > runMaxX + 4f)
            {
                if (hasRun)
                    AddSelectionRect(runMinX, runMaxX, runMinY, runMaxY);

                runLine = ch.lineNumber;
                runMinX = ch.bottomLeft.x;
                runMaxX = ch.topRight.x;
                runMinY = ch.descender;
                runMaxY = ch.ascender;
                hasRun = true;
                continue;
            }

            runMaxX = Mathf.Max(runMaxX, ch.topRight.x);
            runMinY = Mathf.Min(runMinY, ch.descender);
            runMaxY = Mathf.Max(runMaxY, ch.ascender);
        }

        if (hasRun)
            AddSelectionRect(runMinX, runMaxX, runMinY, runMaxY);
    }

    private void AddSelectionRect(float minX, float maxX, float minY, float maxY)
    {
        var go = new GameObject("Selection", typeof(RectTransform));
        go.transform.SetParent(label.transform, false);

        var image = go.AddComponent<Image>();
        image.color = SelectionColor;
        image.raycastTarget = false;

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localPosition = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
        rt.sizeDelta = new Vector2(
            Mathf.Max(2f, maxX - minX + 3f),
            Mathf.Max(8f, maxY - minY + 3f)
        );
        selectionImages.Add(image);
    }

    private string GetSelectedText()
    {
        if (!label)
            return string.Empty;

        label.ForceMeshUpdate();
        GetSelectionRange(out int start, out int end);
        if (start < 0 || end <= start)
            return string.Empty;

        var sb = new System.Text.StringBuilder(end - start);
        var textInfo = label.textInfo;
        for (int i = start; i < end && i < textInfo.characterCount; i++)
        {
            char ch = textInfo.characterInfo[i].character;
            if (ch != '\0')
                sb.Append(ch);
        }

        return sb.ToString();
    }

    private void ClearSelection()
    {
        for (int i = 0; i < selectionImages.Count; i++)
        {
            if (selectionImages[i])
                Destroy(selectionImages[i].gameObject);
        }
        selectionImages.Clear();
    }

    private static bool WasCopyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        return kb != null
            && kb.cKey.wasPressedThisFrame
            && (
                kb.leftCtrlKey.isPressed
                || kb.rightCtrlKey.isPressed
                || kb.leftCommandKey.isPressed
                || kb.rightCommandKey.isPressed
            );
#else
        return UnityEngine.Input.GetKeyDown(KeyCode.C)
            && (
                UnityEngine.Input.GetKey(KeyCode.LeftControl)
                || UnityEngine.Input.GetKey(KeyCode.RightControl)
                || UnityEngine.Input.GetKey(KeyCode.LeftCommand)
                || UnityEngine.Input.GetKey(KeyCode.RightCommand)
            );
#endif
    }

    private static bool IsSafeWebUrl(string url)
    {
        return System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri)
            && (
                string.Equals(
                    uri.Scheme,
                    System.Uri.UriSchemeHttp,
                    System.StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    uri.Scheme,
                    System.Uri.UriSchemeHttps,
                    System.StringComparison.OrdinalIgnoreCase
                )
            );
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

    public void Configure(
        TMP_Text textLabel,
        LayoutElement layoutElement,
        float minimum,
        float extraPadding
    )
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

public static class ChatClipboardImageReader
{
    public static bool TryGetImageBytes(out byte[] bytes, out string sourceName)
    {
        bytes = null;
        sourceName = null;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (TryGetWindowsClipboardPng(out bytes))
        {
            sourceName = "clipboard.png";
            return true;
        }

        if (TryGetWindowsClipboardDib(out bytes))
        {
            sourceName = "clipboard.png";
            return true;
        }

        if (TryGetWindowsClipboardImageFile(out bytes, out sourceName))
            return true;
#endif

        return false;
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const uint CfDib = 8;
    private const uint CfHdrop = 15;
    private static uint pngFormat;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool OpenClipboard(System.IntPtr hWndNewOwner);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern System.IntPtr GetClipboardData(uint uFormat);

    [System.Runtime.InteropServices.DllImport(
        "user32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode
    )]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern System.IntPtr GlobalLock(System.IntPtr hMem);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(System.IntPtr hMem);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern System.UIntPtr GlobalSize(System.IntPtr hMem);

    [System.Runtime.InteropServices.DllImport(
        "shell32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode
    )]
    private static extern uint DragQueryFile(
        System.IntPtr hDrop,
        uint iFile,
        System.Text.StringBuilder lpszFile,
        uint cch
    );

    private static bool TryGetWindowsClipboardPng(out byte[] bytes)
    {
        bytes = null;
        pngFormat = pngFormat != 0 ? pngFormat : RegisterClipboardFormat("PNG");
        if (pngFormat == 0 || !IsClipboardFormatAvailable(pngFormat))
            return false;

        return TryReadClipboardHandleBytes(pngFormat, out bytes);
    }

    private static bool TryGetWindowsClipboardDib(out byte[] pngBytes)
    {
        pngBytes = null;
        if (!IsClipboardFormatAvailable(CfDib))
            return false;
        if (!TryReadClipboardHandleBytes(CfDib, out var dibBytes))
            return false;

        return TryConvertDibToPng(dibBytes, out pngBytes);
    }

    private static bool TryGetWindowsClipboardImageFile(out byte[] bytes, out string sourceName)
    {
        bytes = null;
        sourceName = null;
        if (!IsClipboardFormatAvailable(CfHdrop))
            return false;

        if (!OpenClipboard(System.IntPtr.Zero))
            return false;

        try
        {
            var hDrop = GetClipboardData(CfHdrop);
            if (hDrop == System.IntPtr.Zero)
                return false;

            uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(hDrop, i, null, 0);
                var sb = new System.Text.StringBuilder((int)length + 1);
                DragQueryFile(hDrop, i, sb, length + 1);
                string path = sb.ToString();
                if (!IsImagePath(path) || !System.IO.File.Exists(path))
                    continue;

                bytes = System.IO.File.ReadAllBytes(path);
                sourceName = System.IO.Path.GetFileName(path);
                return true;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Chat] Failed to read clipboard image file: {ex.Message}");
        }
        finally
        {
            CloseClipboard();
        }

        return false;
    }

    private static bool TryReadClipboardHandleBytes(uint format, out byte[] bytes)
    {
        bytes = null;
        if (!OpenClipboard(System.IntPtr.Zero))
            return false;

        try
        {
            var handle = GetClipboardData(format);
            if (handle == System.IntPtr.Zero)
                return false;

            var locked = GlobalLock(handle);
            if (locked == System.IntPtr.Zero)
                return false;

            try
            {
                int size = checked((int)GlobalSize(handle).ToUInt64());
                if (size <= 0)
                    return false;

                bytes = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(locked, bytes, 0, size);
                return true;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Chat] Failed to read clipboard image: {ex.Message}");
            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryConvertDibToPng(byte[] dib, out byte[] pngBytes)
    {
        pngBytes = null;
        if (dib == null || dib.Length < 40)
            return false;

        int headerSize = System.BitConverter.ToInt32(dib, 0);
        int width = System.BitConverter.ToInt32(dib, 4);
        int rawHeight = System.BitConverter.ToInt32(dib, 8);
        short bitCount = System.BitConverter.ToInt16(dib, 14);
        int compression = System.BitConverter.ToInt32(dib, 16);
        int colorsUsed = headerSize >= 40 ? System.BitConverter.ToInt32(dib, 32) : 0;

        if (headerSize <= 0 || width <= 0 || rawHeight == 0)
            return false;
        if (bitCount != 24 && bitCount != 32)
            return false;
        if (compression != 0 && compression != 3)
            return false;

        bool topDown = rawHeight < 0;
        int height = Mathf.Abs(rawHeight);
        int pixelOffset = headerSize;
        if (compression == 3 && headerSize == 40)
            pixelOffset += bitCount == 16 || bitCount == 32 ? 12 : 0;
        if (bitCount <= 8)
        {
            int colors = colorsUsed > 0 ? colorsUsed : 1 << bitCount;
            pixelOffset += colors * 4;
        }

        int rowStride = bitCount == 32 ? width * 4 : ((width * 3 + 3) / 4) * 4;
        if (pixelOffset < 0 || pixelOffset + rowStride * height > dib.Length)
            return false;

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            int srcY = topDown ? height - 1 - y : y;
            int row = pixelOffset + srcY * rowStride;
            for (int x = 0; x < width; x++)
            {
                int src = row + x * (bitCount / 8);
                byte b = dib[src];
                byte g = dib[src + 1];
                byte r = dib[src + 2];
                byte a = bitCount == 32 ? dib[src + 3] : (byte)255;
                if (a == 0)
                    a = 255;
                pixels[y * width + x] = new Color32(r, g, b, a);
            }
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        pngBytes = texture.EncodeToPNG();
        UnityEngine.Object.Destroy(texture);
        return pngBytes != null && pngBytes.Length > 0;
    }

    private static bool IsImagePath(string path)
    {
        string ext = System.IO.Path.GetExtension(path);
        return string.Equals(ext, ".png", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpg", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpeg", System.StringComparison.OrdinalIgnoreCase);
    }
#endif
}

public static class ChatWindowsDropBridge
{
    private static readonly Queue<string> droppedFiles = new();
    private static bool initialized;

    public static void Ensure()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (initialized)
            return;

        initialized = WindowsDropTarget.Initialize();
#endif
    }

    public static void Shutdown()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        WindowsDropTarget.Shutdown();
        initialized = false;
        lock (droppedFiles)
            droppedFiles.Clear();
#endif
    }

    public static List<string> DrainDroppedFiles()
    {
        var files = new List<string>();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        WindowsDropTarget.Drain(files);
#endif
        return files;
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private static class WindowsDropTarget
    {
        private const int GwlpWndproc = -4;
        private const uint WmDropfiles = 0x0233;
        private static System.IntPtr hwnd;
        private static System.IntPtr previousWndProc;
        private static WndProcDelegate wndProcDelegate;

        private delegate System.IntPtr WndProcDelegate(
            System.IntPtr hWnd,
            uint msg,
            System.IntPtr wParam,
            System.IntPtr lParam
        );

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(System.IntPtr hWnd, bool fAccept);

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern void DragFinish(System.IntPtr hDrop);

        [System.Runtime.InteropServices.DllImport(
            "shell32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode
        )]
        private static extern uint DragQueryFile(
            System.IntPtr hDrop,
            uint iFile,
            System.Text.StringBuilder lpszFile,
            uint cch
        );

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern System.IntPtr SetWindowLongPtr64(
            System.IntPtr hWnd,
            int nIndex,
            System.IntPtr dwNewLong
        );

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern System.IntPtr SetWindowLongPtr32(
            System.IntPtr hWnd,
            int nIndex,
            System.IntPtr dwNewLong
        );

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr CallWindowProc(
            System.IntPtr lpPrevWndFunc,
            System.IntPtr hWnd,
            uint msg,
            System.IntPtr wParam,
            System.IntPtr lParam
        );

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr GetActiveWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr DefWindowProc(
            System.IntPtr hWnd,
            uint msg,
            System.IntPtr wParam,
            System.IntPtr lParam
        );

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindow(System.IntPtr hWnd);

        public static bool Initialize()
        {
            hwnd = GetMainWindowHandle();
            if (hwnd == System.IntPtr.Zero)
                return false;

            wndProcDelegate = WndProc;
            var newWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(
                wndProcDelegate
            );
            previousWndProc =
                System.IntPtr.Size == 8
                    ? SetWindowLongPtr64(hwnd, GwlpWndproc, newWndProc)
                    : SetWindowLongPtr32(hwnd, GwlpWndproc, newWndProc);

            if (previousWndProc == System.IntPtr.Zero)
            {
                hwnd = System.IntPtr.Zero;
                wndProcDelegate = null;
                return false;
            }

            DragAcceptFiles(hwnd, true);
            return true;
        }

        public static void Shutdown()
        {
            if (hwnd != System.IntPtr.Zero && IsWindow(hwnd))
            {
                DragAcceptFiles(hwnd, false);
                if (previousWndProc != System.IntPtr.Zero)
                {
                    if (System.IntPtr.Size == 8)
                        SetWindowLongPtr64(hwnd, GwlpWndproc, previousWndProc);
                    else
                        SetWindowLongPtr32(hwnd, GwlpWndproc, previousWndProc);
                }
            }

            hwnd = System.IntPtr.Zero;
            previousWndProc = System.IntPtr.Zero;
            wndProcDelegate = null;
        }

        public static void Drain(List<string> files)
        {
            lock (droppedFiles)
            {
                while (droppedFiles.Count > 0)
                    files.Add(droppedFiles.Dequeue());
            }
        }

        private static System.IntPtr WndProc(
            System.IntPtr hWnd,
            uint msg,
            System.IntPtr wParam,
            System.IntPtr lParam
        )
        {
            if (msg == WmDropfiles)
            {
                try
                {
                    uint count = DragQueryFile(wParam, 0xFFFFFFFF, null, 0);
                    lock (droppedFiles)
                    {
                        for (uint i = 0; i < count; i++)
                        {
                            uint length = DragQueryFile(wParam, i, null, 0);
                            var sb = new System.Text.StringBuilder((int)length + 1);
                            DragQueryFile(wParam, i, sb, length + 1);
                            droppedFiles.Enqueue(sb.ToString());
                        }
                    }
                }
                finally
                {
                    DragFinish(wParam);
                }

                return System.IntPtr.Zero;
            }

            return previousWndProc != System.IntPtr.Zero
                ? CallWindowProc(previousWndProc, hWnd, msg, wParam, lParam)
                : DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private static System.IntPtr GetMainWindowHandle()
        {
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                process.Refresh();
                if (process.MainWindowHandle != System.IntPtr.Zero)
                    return process.MainWindowHandle;
            }
            catch { }

            return GetActiveWindow();
        }
    }
#endif
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
