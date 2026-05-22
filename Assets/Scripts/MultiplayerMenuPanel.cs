using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class MultiplayerMenuPanel : MonoBehaviour
{
    private TMP_InputField joinCodeInput;
    private TMP_InputField nicknameInput;
    private TMP_Text statusLabel;
    private Button hostButton;
    private Button joinButton;
    private bool hostingLobby;
    private bool joinedLobby;
    private bool operationBusy;
    private float nextStatusRefresh;

    public static void EnsureInScene()
    {
        if (FindFirstObjectByType<MultiplayerMenuPanel>())
            return;

        var canvas = CreateCanvas();

        var go = new GameObject("Multiplayer Menu Panel", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.AddComponent<MultiplayerMenuPanel>();
    }

    private static Canvas CreateCanvas()
    {
        var existing = GameObject.Find("Multiplayer Overlay Canvas");
        if (existing && existing.TryGetComponent(out Canvas existingCanvas))
            return existingCanvas;

        var go = new GameObject("Multiplayer Overlay Canvas", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
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
        rt.sizeDelta = new Vector2(282f, 292f);

        var image = gameObject.AddComponent<Image>();
        image.color = new Color(0.03f, 0.04f, 0.05f, 0.72f);

        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 12, 12);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateLabel("Co-op lobby", 18f, FontStyles.Bold);
        statusLabel = CreateLabel("Host a lobby, then choose a quiz after player 2 joins.", 14f, FontStyles.Normal);
        statusLabel.color = new Color(0.82f, 0.88f, 0.93f, 1f);
        statusLabel.textWrappingMode = TextWrappingModes.Normal;
        statusLabel.GetComponent<LayoutElement>().minHeight = 44f;

        nicknameInput = CreateInput("Nickname", 14);
        nicknameInput.SetTextWithoutNotify(PlayerPrefs.GetString("coop_nickname", "Player"));
        hostButton = CreateButton("Host co-op lobby", OnHostClicked);
        joinCodeInput = CreateInput("4-digit code", 4, digitsOnly: true);
        joinButton = CreateButton("Join co-op", OnJoinClicked);
    }

    private TMP_Text CreateLabel(string text, float size, FontStyles style)
    {
        var go = UiObject("Label");
        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Left;

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = Mathf.Ceil(size + 8f);
        return label;
    }

    private Button CreateButton(string labelText, Action onClick)
    {
        var go = UiObject(labelText);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.16f, 0.35f, 0.70f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 38f;
        layout.preferredHeight = 38f;

        var labelGo = new GameObject("Text", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(8f, 0f);
        labelRt.offsetMax = new Vector2(-8f, 0f);

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 15f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        return button;
    }

    private TMP_InputField CreateInput(
        string placeholderText,
        int characterLimit,
        bool digitsOnly = false
    )
    {
        var go = UiObject("Join Code Input");
        var image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.94f);

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 38f;
        layout.preferredHeight = 38f;

        var input = go.AddComponent<TMP_InputField>();
        input.characterLimit = characterLimit;
        input.contentType = digitsOnly
            ? TMP_InputField.ContentType.IntegerNumber
            : TMP_InputField.ContentType.Alphanumeric;
        input.lineType = TMP_InputField.LineType.SingleLine;

        var viewportGo = new GameObject("Text Area", typeof(RectTransform));
        viewportGo.transform.SetParent(go.transform, false);
        var viewport = (RectTransform)viewportGo.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(10f, 4f);
        viewport.offsetMax = new Vector2(-10f, -4f);
        viewportGo.AddComponent<RectMask2D>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(viewportGo.transform, false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.fontSize = 16f;
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
        placeholder.fontSize = 16f;
        placeholder.color = new Color(0.2f, 0.25f, 0.3f, 0.55f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;

        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;
        if (digitsOnly)
            input.onValueChanged.AddListener(NormalizeJoinCode);

        return input;
    }

    private GameObject UiObject(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        return go;
    }

    private void NormalizeJoinCode(string value)
    {
        var normalized = OnlyDigits(value);
        if (normalized == value)
            return;

        joinCodeInput.SetTextWithoutNotify(normalized);
        joinCodeInput.caretPosition = normalized.Length;
        joinCodeInput.stringPosition = normalized.Length;
    }

    private async void OnHostClicked()
    {
        SetBusy(true);
        SetStatus("Creating co-op lobby...");

        try
        {
            var nickname = CurrentNickname();
            await QuizNetworkRuntime.StartHostLobbyAsync(0, nickname: nickname);
            SaveNickname(nickname);
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

    private async void OnJoinClicked()
    {
        var code = joinCodeInput ? OnlyDigits(joinCodeInput.text) : string.Empty;
        if (string.IsNullOrEmpty(code))
        {
            SetStatus("Enter the 4-digit host code first.");
            return;
        }
        if (code.Length != 4)
        {
            SetStatus("Join code must be 4 numbers.");
            return;
        }

        SetBusy(true);
        SetStatus("Joining co-op lobby...");

        try
        {
            var nickname = CurrentNickname();
            await QuizNetworkRuntime.StartClientAsync(code, nickname);
            SaveNickname(nickname);
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

    private void SetBusy(bool busy)
    {
        operationBusy = busy;
        ApplyInteractivity();
    }

    private void ApplyInteractivity()
    {
        if (hostButton)
            hostButton.interactable = !operationBusy && !hostingLobby && !joinedLobby;
        if (joinButton)
            joinButton.interactable = !operationBusy && !hostingLobby && !joinedLobby;
        if (joinCodeInput)
            joinCodeInput.interactable = !operationBusy && !hostingLobby && !joinedLobby;
        if (nicknameInput)
            nicknameInput.interactable = !operationBusy && !hostingLobby && !joinedLobby;
    }

    private void SetStatus(string message)
    {
        if (statusLabel && !string.IsNullOrWhiteSpace(message))
            statusLabel.text = message;
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
        if (Time.unscaledTime < nextStatusRefresh)
            return;

        RefreshLobbyUi();
        nextStatusRefresh = Time.unscaledTime + 0.5f;
    }

    private void RefreshLobbyUi()
    {
        var manager = NetworkManager.Singleton;

        if (hostingLobby && manager && manager.IsServer)
        {
            int players = manager.ConnectedClientsIds.Count;
            string nextStep =
                players >= QuizNetworkRuntime.RequiredPlayerCount
                    ? "Choose a quiz button."
                    : "Share the code.";
            SetStatus($"Co-op code: {QuizNetworkRuntime.JoinCode} | Players {players}/2 | {nextStep}");
        }
        else if (joinedLobby && manager && manager.IsClient && !manager.IsServer)
        {
            SetStatus("Joined co-op. Waiting for host...");
        }

        ApplyInteractivity();
    }

    private string CurrentNickname()
    {
        return QuizNetworkRuntime.NormalizeNickname(nicknameInput ? nicknameInput.text : null);
    }

    private static void SaveNickname(string nickname)
    {
        PlayerPrefs.SetString("coop_nickname", QuizNetworkRuntime.NormalizeNickname(nickname));
        PlayerPrefs.Save();
    }

    private static string OnlyDigits(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        System.Text.StringBuilder sb = new();
        foreach (var ch in value)
            if (char.IsDigit(ch))
                sb.Append(ch);

        return sb.ToString();
    }
}
