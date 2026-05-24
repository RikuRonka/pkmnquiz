using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class MultiplayerMenuPanel : MonoBehaviour
{
    private const string NicknamePrefsKey = "coop_nickname";
    private const float PanelWidth = 360f;
    private const float CompactPanelHeight = 262f;
    private const float BrowserPanelHeight = 376f;
    private const float InputHeight = 34f;
    private const float ButtonHeight = 34f;
    private const float LobbyListHeight = 106f;
    private const float LobbyEntryHeight = 28f;
    private const float LobbyEntryJoinWidth = 60f;
    private const int MaxVisibleLobbyRows = 3;
    private const string ReadyActionText = "Choose a quiz button.";
    private const string PlayerCountColor = "#7DD3FC";
    private const string ReadyGlowColor = "#FFEA00";
    private static readonly Color HostButtonColor = new Color(0.30f, 0.24f, 0.62f, 1f);
    private static readonly Color BrowseButtonColor = new Color(0.04f, 0.45f, 0.42f, 1f);
    private static readonly Color LeaveButtonColor = new Color(0.72f, 0.13f, 0.17f, 1f);
    private static readonly Color LobbyEntryColor = new Color(0.16f, 0.35f, 0.70f, 0.95f);
    private static readonly Color JoinBadgeColor = new Color(0.07f, 0.56f, 0.35f, 1f);

    private static bool overlayVisible = true;

    private TMP_InputField nicknameInput;
    private TMP_Text statusLabel;
    private TMP_Text hostNoticeLabel;
    private Button hostButton;
    private Button refreshLobbiesButton;
    private Button leaveButton;
    private GameObject lobbyListPanel;
    private RectTransform lobbyListContent;
    private TMP_Text lobbyListEmptyLabel;
    private CanvasGroup canvasGroup;
    private readonly List<GameObject> lobbyEntryObjects = new();
    private bool hostingLobby;
    private bool joinedLobby;
    private bool operationBusy;
    private int lastObservedPlayerCount = -1;
    private Coroutine hostNoticeRoutine;
    private float nextStatusRefresh;
    private string rawStatusMessage;
    private readonly Dictionary<string, string> observedLobbyMembers = new();
    private bool lobbyMemberSnapshotRunning;
    private bool pendingLobbyJoinNotice;
    private int pendingLobbyJoinNoticeCount;

    public static void EnsureInScene()
    {
        if (FindFirstObjectByType<MultiplayerMenuPanel>())
            return;

        var canvas = CreateCanvas();

        var go = new GameObject("Multiplayer Menu Panel", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.AddComponent<MultiplayerMenuPanel>();
    }

    public static void SetOverlayVisible(bool visible)
    {
        overlayVisible = visible;

        foreach (var panel in FindObjectsByType<MultiplayerMenuPanel>(FindObjectsSortMode.None))
            panel.ApplyOverlayVisibility();
    }

    private static Canvas CreateCanvas()
    {
        var existing = GameObject.Find("Multiplayer Overlay Canvas");
        if (existing && existing.TryGetComponent(out Canvas existingCanvas))
        {
            ConfigureCanvas(existingCanvas);
            return existingCanvas;
        }

        var go = new GameObject("Multiplayer Overlay Canvas", typeof(RectTransform));
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
        BuildUi();
        QuizNetworkRuntime.StatusChanged += SetStatus;
    }

    private void OnDestroy()
    {
        QuizNetworkRuntime.StatusChanged -= SetStatus;
    }

    private void BuildUi()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(0.108f, 0.73f);
        rt.anchorMax = new Vector2(0.108f, 0.73f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(PanelWidth, BrowserPanelHeight);

        var image = gameObject.AddComponent<Image>();
        image.color = new Color(0.03f, 0.04f, 0.05f, 0.72f);
        canvasGroup = gameObject.GetOrAdd<CanvasGroup>();

        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 12, 12);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateLabel("Co-op lobby", 18f, FontStyles.Bold, 24f);
        statusLabel = CreateLabel(
            "Host a lobby, then choose a quiz after another player joins.",
            13f,
            FontStyles.Normal,
            52f
        );
        statusLabel.color = new Color(0.82f, 0.88f, 0.93f, 1f);
        statusLabel.textWrappingMode = TextWrappingModes.Normal;
        statusLabel.richText = true;

        hostNoticeLabel = CreateLabel("", 12f, FontStyles.Bold, 20f);
        hostNoticeLabel.color = new Color(1f, 0.92f, 0.08f, 1f);
        hostNoticeLabel.gameObject.SetActive(false);

        nicknameInput = CreateInput("Nickname", 14);
        PlayerPrefs.DeleteKey(NicknamePrefsKey);
        PlayerPrefs.Save();
        nicknameInput.SetTextWithoutNotify(string.Empty);
        hostButton = CreateButton("Host co-op lobby", OnHostClicked, HostButtonColor);
        refreshLobbiesButton = CreateButton("Find open lobbies", OnFindLobbiesClicked, BrowseButtonColor);
        CreateLobbyBrowser();
        leaveButton = CreateButton("Leave co-op lobby", OnLeaveClicked, LeaveButtonColor);
        ApplyOverlayVisibility();
        RefreshLobbyUi();
    }

    private void ApplyOverlayVisibility()
    {
        if (!canvasGroup)
            canvasGroup = gameObject.GetOrAdd<CanvasGroup>();

        canvasGroup.alpha = overlayVisible ? 1f : 0f;
        canvasGroup.interactable = overlayVisible;
        canvasGroup.blocksRaycasts = overlayVisible;
    }

    private TMP_Text CreateLabel(string text, float size, FontStyles style, float height = 0f)
    {
        var go = UiObject("Label");
        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Left;
        label.enableAutoSizing = false;

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = height > 0f ? height : Mathf.Ceil(size + 6f);
        layout.preferredHeight = layout.minHeight;
        return label;
    }

    private Button CreateButton(string labelText, Action onClick, Color color)
    {
        var go = UiObject(labelText);
        var image = go.AddComponent<Image>();
        image.color = color;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = ButtonHeight;
        layout.preferredHeight = ButtonHeight;

        var labelGo = new GameObject("Text", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(8f, 0f);
        labelRt.offsetMax = new Vector2(-8f, 0f);

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 14f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        return button;
    }

    private TMP_InputField CreateInput(string placeholderText, int characterLimit)
    {
        var go = UiObject($"{placeholderText} Input");
        var image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.94f);

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = InputHeight;
        layout.preferredHeight = InputHeight;

        var input = go.AddComponent<TMP_InputField>();
        input.characterLimit = characterLimit;
        input.contentType = TMP_InputField.ContentType.Alphanumeric;
        input.lineType = TMP_InputField.LineType.SingleLine;

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
        text.fontSize = 14f;
        text.color = new Color(0.05f, 0.06f, 0.07f, 1f);
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGo.transform.SetParent(viewportGo.transform, false);
        var placeholderRt = (RectTransform)placeholderGo.transform;
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;

        var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholder.text = placeholderText;
        placeholder.fontSize = 14f;
        placeholder.color = new Color(0.2f, 0.25f, 0.3f, 0.55f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;

        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;

        return input;
    }

    private void CreateLobbyBrowser()
    {
        lobbyListPanel = UiObject("Open Lobby List");

        var image = lobbyListPanel.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.22f);

        var layout = lobbyListPanel.AddComponent<LayoutElement>();
        layout.minHeight = LobbyListHeight;
        layout.preferredHeight = LobbyListHeight;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(lobbyListPanel.transform, false);
        var viewport = (RectTransform)viewportGo.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(6f, 6f);
        viewport.offsetMax = new Vector2(-6f, -6f);
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        lobbyListContent = (RectTransform)contentGo.transform;
        lobbyListContent.anchorMin = new Vector2(0f, 1f);
        lobbyListContent.anchorMax = new Vector2(1f, 1f);
        lobbyListContent.pivot = new Vector2(0.5f, 1f);
        lobbyListContent.anchoredPosition = Vector2.zero;
        lobbyListContent.sizeDelta = Vector2.zero;

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

        lobbyListEmptyLabel = CreateLobbyListLabel(contentGo.transform, "No open lobbies found.");
    }

    private TMP_Text CreateLobbyListLabel(Transform parent, string text)
    {
        var go = new GameObject("Empty", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 12f;
        label.color = new Color(0.82f, 0.88f, 0.93f, 1f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = LobbyEntryHeight;
        layout.preferredHeight = LobbyEntryHeight;
        return label;
    }

    private GameObject UiObject(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        return go;
    }

    private async void OnHostClicked()
    {
        SetBusy(true);
        SetStatus("Creating co-op lobby...");

        try
        {
            var nickname = CurrentNickname();
            await QuizNetworkRuntime.StartHostLobbyAsync(0, nickname: nickname);
            hostingLobby = true;
            joinedLobby = false;
            SetBusy(false);
            RefreshLobbyUi();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            SetStatus($"Host failed: {ReadableError(ex)}");
            SetBusy(false);
        }
    }

    private async void OnFindLobbiesClicked()
    {
        if (operationBusy)
            return;

        SetBusy(true);
        SetStatus("Looking for open co-op lobbies...");

        try
        {
            var nickname = CurrentNickname();
            var lobbies = await QuizNetworkRuntime.FindAvailableLobbiesAsync(nickname);
            PopulateLobbyList(lobbies);
            if (lobbies.Count == 0)
                SetStatus("No open co-op lobbies found.");
            else if (lobbies.Count > MaxVisibleLobbyRows)
                SetStatus($"Found {lobbies.Count} open co-op lobbies. Showing first {MaxVisibleLobbyRows}.");
            else
                SetStatus($"Found {lobbies.Count} open co-op lobby{(lobbies.Count == 1 ? "" : "ies")}.");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            SetStatus($"Lobby search failed: {ReadableError(ex)}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnBrowserLobbyClicked(string code)
    {
        if (operationBusy || string.IsNullOrWhiteSpace(code))
            return;

        await JoinCodeAsync(code);
    }

    private async Task JoinCodeAsync(string code)
    {
        SetBusy(true);
        SetStatus("Joining co-op lobby...");

        try
        {
            var nickname = CurrentNickname();
            await QuizNetworkRuntime.StartClientAsync(code, nickname);
            hostingLobby = false;
            joinedLobby = true;
            SetBusy(false);
            RefreshLobbyUi();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            SetStatus($"Join failed: {ReadableError(ex)}");
            SetBusy(false);
        }
    }

    private void PopulateLobbyList(IReadOnlyList<QuizNetworkRuntime.AvailableLobby> lobbies)
    {
        foreach (var entry in lobbyEntryObjects)
            if (entry)
                Destroy(entry);
        lobbyEntryObjects.Clear();

        bool hasLobbies = lobbies != null && lobbies.Count > 0;
        if (lobbyListEmptyLabel)
            lobbyListEmptyLabel.gameObject.SetActive(!hasLobbies);
        if (!hasLobbies || !lobbyListContent)
            return;

        int count = Mathf.Min(lobbies.Count, MaxVisibleLobbyRows);
        for (int i = 0; i < count; i++)
            CreateLobbyEntry(lobbies[i]);
    }

    private void CreateLobbyEntry(QuizNetworkRuntime.AvailableLobby lobby)
    {
        string code = lobby.Code;
        if (string.IsNullOrEmpty(code))
            return;

        var go = new GameObject("Lobby Entry", typeof(RectTransform));
        go.transform.SetParent(lobbyListContent, false);
        lobbyEntryObjects.Add(go);

        var image = go.AddComponent<Image>();
        image.color = LobbyEntryColor;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => OnBrowserLobbyClicked(code));

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = LobbyEntryHeight;
        layout.preferredHeight = LobbyEntryHeight;

        var labelGo = new GameObject("Text", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(8f, 0f);
        labelRt.offsetMax = new Vector2(-(LobbyEntryJoinWidth + 12f), 0f);

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        string host = QuizNetworkRuntime.NormalizeNickname(lobby.HostName);
        label.text =
            $"{EscapeRichText(host)}  "
            + $"Players: <color={PlayerCountColor}><b>{lobby.PlayerCount}</b></color>";
        label.fontSize = 12f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.richText = true;
        label.raycastTarget = false;

        var joinGo = new GameObject("Join Badge", typeof(RectTransform));
        joinGo.transform.SetParent(go.transform, false);
        var joinRt = (RectTransform)joinGo.transform;
        joinRt.anchorMin = new Vector2(1f, 0f);
        joinRt.anchorMax = new Vector2(1f, 1f);
        joinRt.pivot = new Vector2(1f, 0.5f);
        joinRt.sizeDelta = new Vector2(LobbyEntryJoinWidth, 0f);
        joinRt.anchoredPosition = new Vector2(-4f, 0f);
        joinRt.offsetMin = new Vector2(joinRt.offsetMin.x, 3f);
        joinRt.offsetMax = new Vector2(joinRt.offsetMax.x, -3f);

        var joinImage = joinGo.AddComponent<Image>();
        joinImage.color = JoinBadgeColor;
        joinImage.raycastTarget = false;

        var joinLabelGo = new GameObject("Text", typeof(RectTransform));
        joinLabelGo.transform.SetParent(joinGo.transform, false);
        var joinLabelRt = (RectTransform)joinLabelGo.transform;
        joinLabelRt.anchorMin = Vector2.zero;
        joinLabelRt.anchorMax = Vector2.one;
        joinLabelRt.offsetMin = Vector2.zero;
        joinLabelRt.offsetMax = Vector2.zero;

        var joinLabel = joinLabelGo.AddComponent<TextMeshProUGUI>();
        joinLabel.text = "Join";
        joinLabel.fontSize = 12f;
        joinLabel.fontStyle = FontStyles.Bold;
        joinLabel.color = Color.white;
        joinLabel.alignment = TextAlignmentOptions.Center;
        joinLabel.raycastTarget = false;
    }

    private void OnLeaveClicked()
    {
        SetBusy(true);
        QuizNetworkRuntime.Shutdown();
        hostingLobby = false;
        joinedLobby = false;
        SetStatus("Host a lobby, then choose a quiz after another player joins.");
        lastObservedPlayerCount = -1;
        SetBusy(false);
        RefreshLobbyUi();
    }

    private void SetBusy(bool busy)
    {
        operationBusy = busy;
        ApplyInteractivity();
    }

    private void ApplyInteractivity()
    {
        bool networkActive = NetworkManager.Singleton && NetworkManager.Singleton.IsListening;
        bool inLobby = hostingLobby || joinedLobby || networkActive;
        bool canBrowse = !inLobby;

        if (hostButton)
            hostButton.interactable = !operationBusy && !inLobby;
        if (refreshLobbiesButton)
        {
            refreshLobbiesButton.gameObject.SetActive(canBrowse);
            refreshLobbiesButton.interactable = !operationBusy && canBrowse;
        }
        if (lobbyListPanel)
            lobbyListPanel.SetActive(canBrowse);
        if (nicknameInput)
            nicknameInput.interactable = !operationBusy && !inLobby;
        if (leaveButton)
        {
            bool canLeave =
                !operationBusy
                && (
                    hostingLobby
                    || joinedLobby
                    || networkActive
                );
            leaveButton.gameObject.SetActive(canLeave);
            leaveButton.interactable = canLeave;
        }

        var rt = (RectTransform)transform;
        var size = rt.sizeDelta;
        size.y = canBrowse ? BrowserPanelHeight : CompactPanelHeight;
        rt.sizeDelta = size;
    }

    private void SetStatus(string message)
    {
        if (statusLabel && !string.IsNullOrWhiteSpace(message))
        {
            rawStatusMessage = message;
            RenderStatusText();
        }
    }

    private void RenderStatusText()
    {
        if (statusLabel && !string.IsNullOrWhiteSpace(rawStatusMessage))
            statusLabel.text = FormatLobbyStatusText(rawStatusMessage, CurrentReadyPulsePercent());
    }

    private int CurrentReadyPulsePercent()
    {
        float pulse = (Mathf.Sin(Time.unscaledTime * 5.5f) + 1f) * 0.5f;
        return Mathf.RoundToInt(Mathf.Lerp(104f, 122f, pulse));
    }

    private static string FormatLobbyStatusText(string message, int readyPulsePercent)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        message = HighlightPlayerCount(message);
        message = HighlightReadyAction(message, readyPulsePercent);
        return message;
    }

    private static string HighlightReadyAction(string message, int readyPulsePercent)
    {
        return message.Replace(
            ReadyActionText,
            $"<mark={ReadyGlowColor}36><size={readyPulsePercent}%><color={ReadyGlowColor}><b>{ReadyActionText}</b></color></size></mark>"
        );
    }

    private static string HighlightPlayerCount(string message)
    {
        const string playersPrefix = "Players:";
        int prefixIndex = message.IndexOf(playersPrefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex < 0)
            return message;

        int numberStart = prefixIndex + playersPrefix.Length;
        while (numberStart < message.Length && char.IsWhiteSpace(message[numberStart]))
            numberStart++;

        int numberEnd = numberStart;
        while (numberEnd < message.Length && char.IsDigit(message[numberEnd]))
            numberEnd++;

        if (numberEnd == numberStart)
            return message;

        return message.Substring(0, numberStart)
            + $"<color={PlayerCountColor}><b>{message.Substring(numberStart, numberEnd - numberStart)}</b></color>"
            + message.Substring(numberEnd);
    }

    private static string EscapeRichText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string ReadableError(Exception ex)
    {
        if (ex == null)
            return "Unknown error";

        if (ex is NullReferenceException)
            return "missing Netcode setup";

        return string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
    }

    private void Update()
    {
        if (
            statusLabel
            && !string.IsNullOrWhiteSpace(rawStatusMessage)
            && rawStatusMessage.IndexOf(ReadyActionText, StringComparison.Ordinal) >= 0
        )
        {
            RenderStatusText();
        }

        if (Time.unscaledTime < nextStatusRefresh)
            return;

        RefreshLobbyUi();
        nextStatusRefresh = Time.unscaledTime + 0.5f;
    }

    private void RefreshLobbyUi()
    {
        var manager = NetworkManager.Singleton;
        bool networkActive = manager && manager.IsListening;

        if (networkActive && !operationBusy)
        {
            hostingLobby = manager.IsServer;
            joinedLobby = manager.IsClient && !manager.IsServer;
        }

        if ((hostingLobby || joinedLobby) && !operationBusy && !networkActive)
        {
            hostingLobby = false;
            joinedLobby = false;
            observedLobbyMembers.Clear();
            SetStatus("Host a lobby, then choose a quiz after another player joins.");
        }

        if (hostingLobby && manager && manager.IsServer)
        {
            int players = manager.ConnectedClientsIds.Count;
            MaybeShowHostJoinNotice(players);
            string nextStep =
                players >= QuizNetworkRuntime.RequiredPlayerCount
                    ? "Choose a quiz button."
                    : "Waiting for players.";
            SetStatus($"Co-op lobby | Players: {players} | {nextStep}");
        }
        else if (joinedLobby && manager && manager.IsClient && !manager.IsServer)
        {
            SetStatus("Joined co-op. Waiting for host...");
            lastObservedPlayerCount = -1;
            observedLobbyMembers.Clear();
        }
        else if (!networkActive)
        {
            lastObservedPlayerCount = -1;
            observedLobbyMembers.Clear();
        }

        ApplyInteractivity();
    }

    private void MaybeShowHostJoinNotice(int players)
    {
        if (lastObservedPlayerCount < 1)
        {
            lastObservedPlayerCount = players;
            QueueLobbyMemberSnapshot(false);
            return;
        }

        if (players > lastObservedPlayerCount)
        {
            QueueLobbyMemberSnapshot(true, players - lastObservedPlayerCount);
        }
        else if (players < lastObservedPlayerCount)
        {
            QueueLobbyMemberSnapshot(false);
        }

        lastObservedPlayerCount = players;
    }

    private void QueueLobbyMemberSnapshot(bool showJoinNotice, int joinCount = 0)
    {
        if (showJoinNotice)
        {
            pendingLobbyJoinNotice = true;
            pendingLobbyJoinNoticeCount += Mathf.Max(1, joinCount);
        }

        if (!lobbyMemberSnapshotRunning)
            RefreshLobbyMemberSnapshotAsync();
    }

    private async void RefreshLobbyMemberSnapshotAsync()
    {
        lobbyMemberSnapshotRunning = true;

        try
        {
            while (true)
            {
                bool showJoinNotice = pendingLobbyJoinNotice;
                int fallbackJoinCount = pendingLobbyJoinNoticeCount;
                pendingLobbyJoinNotice = false;
                pendingLobbyJoinNoticeCount = 0;

                IReadOnlyList<QuizNetworkRuntime.LobbyMemberInfo> members =
                    await QuizNetworkRuntime.GetCurrentLobbyMembersAsync();
                if (!this)
                    return;

                var joinedNames = new List<string>();
                var nonLocalNames = new List<string>();
                var currentMembers = new Dictionary<string, string>();
                foreach (var member in members)
                {
                    if (string.IsNullOrEmpty(member.Id))
                        continue;

                    currentMembers[member.Id] = member.Name;
                    if (!member.IsLocalPlayer)
                        nonLocalNames.Add(member.Name);

                    if (
                        showJoinNotice
                        && !member.IsLocalPlayer
                        && !observedLobbyMembers.ContainsKey(member.Id)
                    )
                    {
                        joinedNames.Add(member.Name);
                    }
                }

                observedLobbyMembers.Clear();
                foreach (var member in currentMembers)
                    observedLobbyMembers[member.Key] = member.Value;

                if (showJoinNotice && joinedNames.Count == 0 && fallbackJoinCount > 0)
                {
                    int fallbackNames = Mathf.Min(fallbackJoinCount, nonLocalNames.Count);
                    for (int i = nonLocalNames.Count - fallbackNames; i < nonLocalNames.Count; i++)
                        if (i >= 0)
                            joinedNames.Add(nonLocalNames[i]);
                }

                if (showJoinNotice)
                    ShowHostNotice(BuildLobbyJoinNotice(joinedNames, fallbackJoinCount));

                if (!pendingLobbyJoinNotice)
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            if (pendingLobbyJoinNoticeCount > 0)
                ShowHostNotice(BuildLobbyJoinNotice(null, pendingLobbyJoinNoticeCount));
            pendingLobbyJoinNotice = false;
            pendingLobbyJoinNoticeCount = 0;
        }
        finally
        {
            lobbyMemberSnapshotRunning = false;
            if (pendingLobbyJoinNotice && this)
                RefreshLobbyMemberSnapshotAsync();
        }
    }

    private static string BuildLobbyJoinNotice(IReadOnlyList<string> joinedNames, int fallbackJoinCount)
    {
        if (joinedNames != null && joinedNames.Count == 1)
            return $"{FormatNoticeName(joinedNames[0])} joined the lobby.";

        if (joinedNames != null && joinedNames.Count == 2)
            return $"{FormatNoticeName(joinedNames[0])} and {FormatNoticeName(joinedNames[1])} joined the lobby.";

        if (joinedNames != null && joinedNames.Count > 2)
            return $"{FormatNoticeName(joinedNames[0])} and {joinedNames.Count - 1} others joined the lobby.";

        int joined = Mathf.Max(1, fallbackJoinCount);
        return joined == 1 ? "A player joined the lobby." : $"{joined} players joined the lobby.";
    }

    private static string FormatNoticeName(string name)
    {
        return $"<color={PlayerCountColor}><b>{EscapeRichText(QuizNetworkRuntime.NormalizeNickname(name))}</b></color>";
    }

    private void ShowHostNotice(string message)
    {
        if (!hostNoticeLabel || string.IsNullOrWhiteSpace(message))
            return;

        hostNoticeLabel.text = message;
        hostNoticeLabel.gameObject.SetActive(true);

        if (hostNoticeRoutine != null)
            StopCoroutine(hostNoticeRoutine);
        hostNoticeRoutine = StartCoroutine(CoHideHostNotice());
    }

    private IEnumerator CoHideHostNotice()
    {
        yield return new WaitForSecondsRealtime(3f);
        if (hostNoticeLabel)
            hostNoticeLabel.gameObject.SetActive(false);
        hostNoticeRoutine = null;
    }

    private string CurrentNickname()
    {
        return QuizNetworkRuntime.NormalizeNickname(nicknameInput ? nicknameInput.text : null);
    }

}
