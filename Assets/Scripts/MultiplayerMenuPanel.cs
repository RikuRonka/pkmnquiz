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
    private const float CompactPanelHeight = 300f;
    private const float BrowserPanelHeight = 376f;
    private const float InputHeight = 34f;
    private const float ButtonHeight = 34f;
    private const float ColorPanelWidth = 88f;
    private const float ColorPanelHeight = 166f;
    private const float ColorSwatchSize = 18f;
    private const float LobbyListHeight = 106f;
    private const float LobbyEntryHeight = 28f;
    private const float LobbyEntryJoinWidth = 60f;
    private const float LobbyBrowserRefreshInterval = 2.5f;
    private const float PlayerListMinHeight = 18f;
    private const float PlayerListLineHeight = 16f;
    private const int MaxDisplayedLobbyMembers = 6;
    private const int MaxVisibleLobbyRows = 3;
    private const string ReadyActionText = "Choose a quiz button.";
    private const string PlayerCountColor = "#7DD3FC";
    private const string ReadyGlowColor = "#FFEA00";
    private static readonly Color HostButtonColor = new Color(0.30f, 0.24f, 0.62f, 1f);
    private static readonly Color BrowseButtonColor = new Color(0.04f, 0.45f, 0.42f, 1f);
    private static readonly Color LeaveButtonColor = new Color(0.72f, 0.13f, 0.17f, 1f);
    private static readonly Color LobbyEntryColor = new Color(0.16f, 0.35f, 0.70f, 0.95f);
    private static readonly Color JoinBadgeColor = new Color(0.07f, 0.56f, 0.35f, 1f);
    private static readonly HashSet<string> MenuQuizButtonMethodNames = new(StringComparer.Ordinal)
    {
        "PlayFullQuiz",
        "PlayGenQuiz",
        "PlayTypeQuiz",
        "PlayMegaEvolutionsQuiz",
        "PlayGen",
        "PlayType",
    };

    private static bool overlayVisible = true;

    private TMP_InputField nicknameInput;
    private TMP_Text statusLabel;
    private TMP_Text playersListLabel;
    private Button hostButton;
    private Button refreshLobbiesButton;
    private Button returnToQuizButton;
    private Button leaveButton;
    private readonly List<Button> colorButtons = new();
    private readonly List<Outline> colorButtonOutlines = new();
    private readonly HashSet<string> occupiedColorHexes = new(StringComparer.OrdinalIgnoreCase);
    private GameObject colorPickerPanel;
    private CanvasGroup colorPickerCanvasGroup;
    private GameObject lobbyListPanel;
    private RectTransform lobbyListContent;
    private TMP_Text lobbyListEmptyLabel;
    private CanvasGroup canvasGroup;
    private readonly List<GameObject> lobbyEntryObjects = new();
    private readonly Dictionary<Button, bool> menuQuizButtonBaseInteractivity = new();
    private bool hostingLobby;
    private bool joinedLobby;
    private bool operationBusy;
    private int lastObservedPlayerCount = -1;
    private float nextStatusRefresh;
    private string rawStatusMessage;
    private readonly Dictionary<string, string> observedLobbyMembers = new();
    private bool lobbyMemberSnapshotRunning;
    private bool lobbyBrowserAutoRefresh;
    private bool lobbyBrowserRefreshRunning;
    private int lobbyMemberDisplayRows;
    private string selectedColorHex = QuizNetworkRuntime.PlayerColorHex;
    private float nextLobbyMemberRefresh;
    private float nextLobbyBrowserRefresh;
    private static string sessionNickname = string.Empty;

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
        Application.runInBackground = true;
        BuildUi();
        QuizNetworkRuntime.StatusChanged += SetStatus;
    }

    private void OnDestroy()
    {
        QuizNetworkRuntime.StatusChanged -= SetStatus;
        if (colorPickerPanel)
            Destroy(colorPickerPanel);
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
            "Host a lobby, then choose a quiz. Other players can join anytime.",
            13f,
            FontStyles.Normal,
            52f
        );
        statusLabel.color = new Color(0.82f, 0.88f, 0.93f, 1f);
        statusLabel.textWrappingMode = TextWrappingModes.Normal;
        statusLabel.richText = true;
        playersListLabel = CreateLabel(string.Empty, 12f, FontStyles.Normal, PlayerListMinHeight);
        playersListLabel.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        playersListLabel.textWrappingMode = TextWrappingModes.NoWrap;
        playersListLabel.richText = true;
        playersListLabel.text = string.Empty;

        nicknameInput = CreateInput("Nickname", 14);
        PlayerPrefs.DeleteKey(NicknamePrefsKey);
        PlayerPrefs.Save();
        nicknameInput.SetTextWithoutNotify(sessionNickname);
        nicknameInput.onValueChanged.AddListener(_ => ApplyInteractivity());
        selectedColorHex = QuizNetworkRuntime.SetPlayerColorHex(QuizNetworkRuntime.PlayerColorHex);
        CreateColorPickerPanel();
        hostButton = CreateButton("Host co-op lobby", OnHostClicked, HostButtonColor);
        returnToQuizButton = CreateButton("Return to co-op quiz", OnReturnToQuizClicked, JoinBadgeColor);
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
        ApplyColorPickerVisibility(IsInLobby());
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

    private void CreateColorPickerPanel()
    {
        colorPickerPanel = new GameObject("Select Color Panel", typeof(RectTransform));
        colorPickerPanel.transform.SetParent(transform.parent, false);

        var panelRt = (RectTransform)colorPickerPanel.transform;
        var selfRt = (RectTransform)transform;
        panelRt.anchorMin = selfRt.anchorMin;
        panelRt.anchorMax = selfRt.anchorMax;
        panelRt.pivot = new Vector2(0f, 1f);
        panelRt.anchoredPosition = selfRt.anchoredPosition + new Vector2(PanelWidth + 8f, 0f);
        panelRt.sizeDelta = new Vector2(ColorPanelWidth, ColorPanelHeight);

        var image = colorPickerPanel.AddComponent<Image>();
        image.color = new Color(0.03f, 0.04f, 0.05f, 0.72f);
        colorPickerCanvasGroup = colorPickerPanel.AddComponent<CanvasGroup>();

        var layout = colorPickerPanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var labelGo = new GameObject("Title", typeof(RectTransform));
        labelGo.transform.SetParent(colorPickerPanel.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = "Select color";
        label.fontSize = 11f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        var labelLayout = labelGo.AddComponent<LayoutElement>();
        labelLayout.minWidth = ColorPanelWidth - 16f;
        labelLayout.preferredWidth = ColorPanelWidth - 16f;
        labelLayout.minHeight = 24f;
        labelLayout.preferredHeight = 24f;

        var gridGo = new GameObject("Swatches", typeof(RectTransform));
        gridGo.transform.SetParent(colorPickerPanel.transform, false);

        var gridLayout = gridGo.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(ColorSwatchSize, ColorSwatchSize);
        gridLayout.spacing = new Vector2(5f, 5f);
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 3;

        var gridElement = gridGo.AddComponent<LayoutElement>();
        gridElement.minWidth = ColorPanelWidth - 16f;
        gridElement.preferredWidth = ColorPanelWidth - 16f;
        gridElement.minHeight = 4f * ColorSwatchSize + 3f * 5f;
        gridElement.preferredHeight = gridElement.minHeight;

        foreach (var colorHex in QuizNetworkRuntime.PlayerColorPalette)
            CreateColorSwatch(gridGo.transform, colorHex);

        RefreshColorSwatches();
        ApplyColorPickerVisibility(false);
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

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        PreserveSwatchTint(button);
        button.onClick.AddListener(() => SetSelectedColor(colorHex));

        var layout = go.AddComponent<LayoutElement>();
        layout.minWidth = ColorSwatchSize;
        layout.preferredWidth = ColorSwatchSize;
        layout.minHeight = ColorSwatchSize;
        layout.preferredHeight = ColorSwatchSize;

        colorButtons.Add(button);
        colorButtonOutlines.Add(outline);
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

    private async void SetSelectedColor(string colorHex)
    {
        colorHex = QuizNetworkRuntime.NormalizeColorHex(colorHex);
        if (occupiedColorHexes.Contains(colorHex))
        {
            SetStatus("That color is already taken in this lobby.");
            return;
        }

        selectedColorHex = QuizNetworkRuntime.SetPlayerColorHex(colorHex);
        RefreshColorSwatches();

        if (IsInLobby())
        {
            try
            {
                await QuizNetworkRuntime.UpdateCurrentLobbyPlayerAsync(CurrentNickname(), selectedColorHex);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus($"Color update failed: {ReadableError(ex)}");
            }
        }
    }

    private void RefreshColorSwatches()
    {
        for (int i = 0; i < colorButtonOutlines.Count; i++)
        {
            string colorHex = i < QuizNetworkRuntime.PlayerColorPalette.Length
                ? QuizNetworkRuntime.NormalizeColorHex(QuizNetworkRuntime.PlayerColorPalette[i])
                : string.Empty;
            bool selected = string.Equals(colorHex, selectedColorHex, StringComparison.OrdinalIgnoreCase);
            bool occupied = occupiedColorHexes.Contains(colorHex);
            if (colorButtons[i])
                colorButtons[i].gameObject.SetActive(!occupied || selected);
            colorButtonOutlines[i].enabled = selected;
            if (colorButtons[i])
                colorButtons[i].interactable = !operationBusy && !occupied;
        }
    }

    private void ApplyColorPickerVisibility(bool visible)
    {
        if (!colorPickerCanvasGroup)
            return;

        colorPickerCanvasGroup.alpha = overlayVisible && visible ? 1f : 0f;
        colorPickerCanvasGroup.interactable = overlayVisible && visible;
        colorPickerCanvasGroup.blocksRaycasts = overlayVisible && visible;
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
        if (!TryGetRequiredNickname(out var nickname))
            return;

        SetBusy(true);
        SetStatus("Creating co-op lobby...");

        try
        {
            await QuizNetworkRuntime.StartHostLobbyAsync(
                0,
                nickname: nickname,
                colorHex: selectedColorHex
            );
            hostingLobby = true;
            joinedLobby = false;
            lobbyBrowserAutoRefresh = false;
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

        lobbyBrowserAutoRefresh = true;
        await RefreshLobbyBrowserAsync(showStatus: true, setBusy: true);
    }

    private async Task RefreshLobbyBrowserAsync(bool showStatus, bool setBusy)
    {
        if (lobbyBrowserRefreshRunning)
            return;

        lobbyBrowserRefreshRunning = true;
        if (setBusy)
        {
            SetBusy(true);
            SetStatus("Looking for open co-op lobbies...");
        }

        try
        {
            var nickname = CurrentNickname();
            QuizNetworkRuntime.SetPlayerColorHex(selectedColorHex);
            var lobbies = await QuizNetworkRuntime.FindAvailableLobbiesAsync(nickname);
            PopulateLobbyList(lobbies);
            nextLobbyBrowserRefresh = Time.unscaledTime + LobbyBrowserRefreshInterval;

            if (showStatus)
            {
                if (QuizNetworkRuntime.LastLobbySearchHadTransientFailure)
                    SetStatus("Lobby search hit a temporary service hiccup. Retrying...");
                else if (lobbies.Count == 0)
                    SetStatus("No open co-op lobbies found.");
                else if (lobbies.Count > MaxVisibleLobbyRows)
                    SetStatus(
                        $"Found {lobbies.Count} open co-op lobbies. Showing first {MaxVisibleLobbyRows}."
                    );
                else
                    SetStatus(
                        $"Found {lobbies.Count} open co-op lobby{(lobbies.Count == 1 ? "" : "ies")}."
                    );
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            nextLobbyBrowserRefresh = Time.unscaledTime + LobbyBrowserRefreshInterval;
            if (showStatus)
                SetStatus($"Lobby search failed: {ReadableError(ex)}");
        }
        finally
        {
            if (setBusy)
                SetBusy(false);
            lobbyBrowserRefreshRunning = false;
        }
    }

    private async void OnReturnToQuizClicked()
    {
        if (operationBusy)
            return;

        SetBusy(true);
        bool returning = await QuizNetworkRuntime.ReturnToActiveQuizAsync();
        if (!returning)
        {
            SetBusy(false);
            RefreshLobbyUi();
        }
    }

    private async void OnBrowserLobbyClicked(QuizNetworkRuntime.AvailableLobby lobby)
    {
        if (operationBusy || string.IsNullOrWhiteSpace(lobby.Code))
            return;

        await JoinCodeAsync(lobby);
    }

    private async Task JoinCodeAsync(QuizNetworkRuntime.AvailableLobby lobby)
    {
        if (!TryGetRequiredNickname(out var nickname))
            return;

        SetBusy(true);
        SetStatus("Joining co-op lobby...");

        try
        {
            if (LobbyHasNickname(lobby, nickname))
            {
                SetStatus("That nickname is already taken in this lobby.");
                SetBusy(false);
                return;
            }

            selectedColorHex = FirstAvailableColor(lobby);
            QuizNetworkRuntime.SetPlayerColorHex(selectedColorHex);
            await QuizNetworkRuntime.StartClientAsync(lobby.Code, nickname, selectedColorHex);
            hostingLobby = false;
            joinedLobby = true;
            lobbyBrowserAutoRefresh = false;
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

    private static bool LobbyHasNickname(
        QuizNetworkRuntime.AvailableLobby lobby,
        string nickname
    )
    {
        nickname = QuizNetworkRuntime.NormalizeNickname(nickname);
        foreach (var takenName in lobby.TakenNames)
            if (
                string.Equals(
                    QuizNetworkRuntime.NormalizeNickname(takenName),
                    nickname,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;

        return false;
    }

    private static string FirstAvailableColor(QuizNetworkRuntime.AvailableLobby lobby)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var takenColor in lobby.TakenColors)
            taken.Add(QuizNetworkRuntime.NormalizeColorHex(takenColor));

        foreach (var colorHex in QuizNetworkRuntime.PlayerColorPalette)
        {
            string normalized = QuizNetworkRuntime.NormalizeColorHex(colorHex);
            if (!taken.Contains(normalized))
                return normalized;
        }

        return QuizNetworkRuntime.PlayerColorHex;
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
        button.onClick.AddListener(() => OnBrowserLobbyClicked(lobby));

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
        string quizText = string.IsNullOrWhiteSpace(lobby.ActiveQuizLabel)
            ? string.Empty
            : $"  <color=#CFFAFE>In: {EscapeRichText(lobby.ActiveQuizLabel)}</color>  ";
        label.text =
            $"{EscapeRichText(host)}  "
            + quizText
            + $"Players: <color={PlayerCountColor}><b>{lobby.PlayerCount}</b></color>";
        label.fontSize = 12f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
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
        lobbyBrowserAutoRefresh = false;
        SetStatus("Host a lobby, then choose a quiz. Other players can join anytime.");
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
        if (returnToQuizButton)
        {
            bool canReturn = !operationBusy && QuizNetworkRuntime.CanReturnToActiveQuiz;
            returnToQuizButton.gameObject.SetActive(canReturn);
            returnToQuizButton.interactable = canReturn;
        }
        if (refreshLobbiesButton)
        {
            refreshLobbiesButton.gameObject.SetActive(canBrowse);
            refreshLobbiesButton.interactable = !operationBusy && canBrowse;
        }
        if (lobbyListPanel)
            lobbyListPanel.SetActive(canBrowse);
        if (nicknameInput)
            nicknameInput.interactable = !operationBusy && !inLobby;
        ApplyColorPickerVisibility(inLobby);
        RefreshColorSwatches();
        if (playersListLabel)
            playersListLabel.gameObject.SetActive(inLobby);
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
        size.y = canBrowse
            ? BrowserPanelHeight
            : CompactPanelHeight
                + Mathf.Max(0f, CurrentPlayersListHeight() - PlayerListMinHeight);
        rt.sizeDelta = size;

        ApplyMenuQuizButtonInteractivity();
    }

    private float CurrentPlayersListHeight()
    {
        int rows = Mathf.Clamp(lobbyMemberDisplayRows, 1, MaxDisplayedLobbyMembers);
        return Mathf.Max(PlayerListMinHeight, rows * PlayerListLineHeight + 2f);
    }

    private void ApplyPlayersListHeight(int rows)
    {
        lobbyMemberDisplayRows = Mathf.Clamp(rows, 0, MaxDisplayedLobbyMembers);
        if (!playersListLabel)
            return;

        var layout = playersListLabel.GetComponent<LayoutElement>();
        if (!layout)
            return;

        float height = lobbyMemberDisplayRows > 0 ? CurrentPlayersListHeight() : PlayerListMinHeight;
        layout.minHeight = height;
        layout.preferredHeight = height;
    }

    private void ApplyMenuQuizButtonInteractivity()
    {
        bool canChooseQuiz = !(joinedLobby || QuizNetworkRuntime.IsMultiplayerClientOnly);

        foreach (
            var button in FindObjectsByType<Button>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            if (IsMenuQuizButton(button))
                ApplyMenuQuizButtonState(button, canChooseQuiz);
        }

        foreach (
            var controller in FindObjectsByType<MainMenuController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            if (controller && controller.fullQuizBtn)
                ApplyMenuQuizButtonState(controller.fullQuizBtn, canChooseQuiz);
        }
    }

    private void ApplyMenuQuizButtonState(Button button, bool canChooseQuiz)
    {
        if (!button)
            return;

        if (!menuQuizButtonBaseInteractivity.ContainsKey(button) && button.interactable)
            menuQuizButtonBaseInteractivity[button] = button.interactable;

        button.interactable =
            canChooseQuiz
            && (
                !menuQuizButtonBaseInteractivity.TryGetValue(button, out bool baseInteractivity)
                || baseInteractivity
            );

        if (button.TryGetComponent<UiButtonHover>(out var hover))
            hover.RefreshDisabledVisual();
    }

    private static bool IsMenuQuizButton(Button button)
    {
        if (!button)
            return false;

        int listenerCount = button.onClick.GetPersistentEventCount();
        for (int i = 0; i < listenerCount; i++)
        {
            string methodName = button.onClick.GetPersistentMethodName(i);
            if (!MenuQuizButtonMethodNames.Contains(methodName))
                continue;

            var target = button.onClick.GetPersistentTarget(i);
            if (target is MenuRouter || target is MainMenuController)
                return true;
        }

        return false;
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

        if (QuizNetworkRuntime.IsLobbySdkWrappedNullReference(ex))
            return "Unity Lobby service had a temporary response error. Try again.";

        if (ex is NullReferenceException)
            return "Unexpected setup error. Check the Unity Console for details.";

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
            QuizNetworkRuntime.Shutdown();
            hostingLobby = false;
            joinedLobby = false;
            observedLobbyMembers.Clear();
            SetStatus("Host a lobby, then choose a quiz. Other players can join anytime.");
        }

        if (hostingLobby && manager && manager.IsServer)
        {
            int players = manager.ConnectedClientsIds.Count;
            MaybeShowHostJoinNotice(players);
            string nextStep = "Choose a quiz button.";
            SetStatus($"Players: {players} | {nextStep}");
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
            occupiedColorHexes.Clear();
        }

        if (IsInLobby() && !operationBusy && Time.unscaledTime >= nextLobbyMemberRefresh)
        {
            QueueLobbyMemberSnapshot(false);
            nextLobbyMemberRefresh = Time.unscaledTime + 2.5f;
        }

        if (
            !IsInLobby()
            && lobbyBrowserAutoRefresh
            && !operationBusy
            && Time.unscaledTime >= nextLobbyBrowserRefresh
        )
        {
            _ = RefreshLobbyBrowserAsync(showStatus: false, setBusy: false);
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
                IReadOnlyList<QuizNetworkRuntime.LobbyMemberInfo> members =
                    await QuizNetworkRuntime.GetCurrentLobbyMembersAsync();
                if (!this)
                    return;

                var currentMembers = new Dictionary<string, string>();
                var currentMemberColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var currentOccupiedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var member in members)
                {
                    if (string.IsNullOrEmpty(member.Id))
                        continue;

                    currentMembers[member.Id] = member.Name;
                    currentMemberColors[member.Id] = QuizNetworkRuntime.NormalizeColorHex(member.ColorHex);
                    if (!member.IsLocalPlayer)
                    {
                        currentOccupiedColors.Add(currentMemberColors[member.Id]);
                    }
                    else
                    {
                        selectedColorHex = QuizNetworkRuntime.SetPlayerColorHex(member.ColorHex);
                    }
                }

                observedLobbyMembers.Clear();

                foreach (var member in currentMembers)
                    observedLobbyMembers[member.Key] = member.Value;

                if (playersListLabel != null)
                {
                    if (observedLobbyMembers.Count == 0)
                    {
                        playersListLabel.text = string.Empty;
                        ApplyPlayersListHeight(0);
                    }
                    else
                    {
                        var sb = new System.Text.StringBuilder();
                        var displayMembers = new List<QuizNetworkRuntime.LobbyMemberInfo>();
                        foreach (var m in members)
                        {
                            if (string.IsNullOrEmpty(m.Id))
                                continue;

                            displayMembers.Add(m);
                        }

                        int visibleMemberCount = displayMembers.Count;
                        bool hasHiddenMembers = visibleMemberCount > MaxDisplayedLobbyMembers;
                        if (hasHiddenMembers)
                            visibleMemberCount = MaxDisplayedLobbyMembers - 1;

                        for (int i = 0; i < visibleMemberCount; i++)
                            AppendLobbyMemberLine(
                                sb,
                                displayMembers[i],
                                currentMembers,
                                currentMemberColors
                            );

                        if (hasHiddenMembers)
                        {
                            int hiddenCount = displayMembers.Count - visibleMemberCount;
                            sb.Append("<color=#CBD5E1>+");
                            sb.Append(hiddenCount);
                            sb.Append(" more</color>\n");
                        }

                        playersListLabel.text = sb.ToString().TrimEnd('\n');
                        ApplyPlayersListHeight(visibleMemberCount + (hasHiddenMembers ? 1 : 0));
                    }
                }

                occupiedColorHexes.Clear();
                foreach (var colorHex in currentOccupiedColors)
                    occupiedColorHexes.Add(colorHex);
                RefreshColorSwatches();

                break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            lobbyMemberSnapshotRunning = false;
        }
    }

    private static void AppendLobbyMemberLine(
        System.Text.StringBuilder sb,
        QuizNetworkRuntime.LobbyMemberInfo member,
        Dictionary<string, string> currentMembers,
        Dictionary<string, string> currentMemberColors
    )
    {
        if (!currentMembers.TryGetValue(member.Id, out var name))
            name = member.Name;

        var hex = currentMemberColors.TryGetValue(member.Id, out var color)
            ? color
            : "#6FEA72";
        sb.Append("<color=");
        sb.Append(hex);
        sb.Append('>');
        sb.Append(EscapeRichText(QuizNetworkRuntime.NormalizeNickname(name)));
        if (member.IsHost)
            sb.Append(" (Host)");
        sb.Append("</color>\n");
    }

    private string CurrentNickname()
    {
        string rawNickname = nicknameInput ? nicknameInput.text : sessionNickname;
        if (string.IsNullOrWhiteSpace(rawNickname))
            return string.IsNullOrWhiteSpace(sessionNickname) ? "Player" : sessionNickname;

        sessionNickname = QuizNetworkRuntime.NormalizeNickname(rawNickname);
        return sessionNickname;
    }

    private bool TryGetRequiredNickname(out string nickname)
    {
        string rawNickname = nicknameInput ? nicknameInput.text : null;
        if (string.IsNullOrWhiteSpace(rawNickname))
        {
            nickname = string.Empty;
            SetStatus("Enter a nickname before joining a lobby.");
            if (nicknameInput)
            {
                nicknameInput.ActivateInputField();
                nicknameInput.Select();
            }
            return false;
        }

        nickname = QuizNetworkRuntime.NormalizeNickname(rawNickname);
        sessionNickname = nickname;
        if (nicknameInput)
            nicknameInput.SetTextWithoutNotify(nickname);
        return true;
    }

    private bool IsInLobby()
    {
        bool networkActive = NetworkManager.Singleton && NetworkManager.Singleton.IsListening;
        return hostingLobby || joinedLobby || networkActive;
    }

}

public static class WindowsAppNotifier
{
    private const float NotificationCooldownSeconds = 0.75f;
    private static float lastNotificationTime = -1000f;

    public static void NotifyLobbyJoin()
    {
        if (Time.unscaledTime - lastNotificationTime < NotificationCooldownSeconds)
            return;

        lastNotificationTime = Time.unscaledTime;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        FlashTaskbar();
        MessageBeep(MessageBeepType.Notification);
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const uint FlashwAll = 0x00000003;
    private const uint FlashwTimernofg = 0x0000000C;

    private enum MessageBeepType : uint
    {
        Notification = 0x00000040,
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool MessageBeep(MessageBeepType type);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    private static void FlashTaskbar()
    {
        IntPtr windowHandle = GetMainWindowHandle();
        if (windowHandle == IntPtr.Zero)
            return;

        var info = new FlashWindowInfo
        {
            cbSize = Convert.ToUInt32(
                System.Runtime.InteropServices.Marshal.SizeOf<FlashWindowInfo>()
            ),
            hwnd = windowHandle,
            dwFlags = FlashwAll | FlashwTimernofg,
            uCount = 0,
            dwTimeout = 0,
        };

        FlashWindowEx(ref info);
    }

    private static IntPtr GetMainWindowHandle()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
                return process.MainWindowHandle;
        }
        catch
        {
            // Fall back to Unity's current active window below.
        }

        return GetActiveWindow();
    }
#endif
}
