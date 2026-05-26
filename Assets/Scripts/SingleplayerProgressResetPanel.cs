using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SingleplayerProgressResetPanel : MonoBehaviour
{
    private const float ButtonWidth = 210f;
    private const float ButtonHeight = 34f;
    private const float ModalWidth = 430f;
    private const float ModalHeight = 250f;
    private Button resetButton;
    private TMP_Text statusLabel;
    private GameObject dialogRoot;
    private TMP_InputField confirmInput;
    private Button okButton;

    public static void EnsureInScene()
    {
        if (FindFirstObjectByType<SingleplayerProgressResetPanel>())
            return;

        var canvas = CreateCanvas();
        var go = new GameObject("Singleplayer Progress Reset", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.AddComponent<SingleplayerProgressResetPanel>();
    }

    private static Canvas CreateCanvas()
    {
        var existing = GameObject.Find("Singleplayer Progress Canvas");
        if (existing && existing.TryGetComponent(out Canvas existingCanvas))
        {
            ConfigureCanvas(existingCanvas);
            return existingCanvas;
        }

        var go = new GameObject("Singleplayer Progress Canvas", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        ConfigureCanvas(canvas);
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static void ConfigureCanvas(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 560;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (!scaler)
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
    }

    private void Awake()
    {
        BuildResetButton();
        BuildDialog();
        HideDialog();
    }

    private void BuildResetButton()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(24f, 24f);
        rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight + 20f);

        resetButton = CreateButton(transform, "Reset quiz progress", ShowDialog);
        var buttonRt = (RectTransform)resetButton.transform;
        buttonRt.anchorMin = new Vector2(0f, 1f);
        buttonRt.anchorMax = new Vector2(0f, 1f);
        buttonRt.pivot = new Vector2(0f, 1f);
        buttonRt.anchoredPosition = Vector2.zero;
        buttonRt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

        statusLabel = CreateText(transform, " ", 14f, TextAlignmentOptions.Left);
        var statusRt = (RectTransform)statusLabel.transform;
        statusRt.anchorMin = new Vector2(0f, 0f);
        statusRt.anchorMax = new Vector2(0f, 0f);
        statusRt.pivot = new Vector2(0f, 0f);
        statusRt.anchoredPosition = Vector2.zero;
        statusRt.sizeDelta = new Vector2(ButtonWidth, 18f);
        statusLabel.color = new Color(0.9f, 1f, 0.9f, 1f);
    }

    private void BuildDialog()
    {
        dialogRoot = new GameObject("Reset Progress Dialog", typeof(RectTransform));
        dialogRoot.transform.SetParent(transform.parent, false);
        var rootRt = (RectTransform)dialogRoot.transform;
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var blocker = dialogRoot.AddComponent<Image>();
        blocker.color = new Color(0f, 0f, 0f, 0.56f);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(dialogRoot.transform, false);
        var panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(ModalWidth, ModalHeight);

        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.10f, 0.12f, 0.15f, 0.98f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 20, 20);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var title = CreateText(panel.transform, "Reset Quiz Progress", 26f, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<LayoutElement>().preferredHeight = 34f;

        var message = CreateText(
            panel.transform,
            "This removes all saved singleplayer quiz progress. Type reset to confirm.",
            17f,
            TextAlignmentOptions.Left
        );
        message.color = new Color(0.88f, 0.92f, 0.96f, 1f);
        message.textWrappingMode = TextWrappingModes.Normal;
        message.GetComponent<LayoutElement>().preferredHeight = 50f;

        confirmInput = CreateInput(panel.transform);
        confirmInput.onValueChanged.AddListener(_ => RefreshOkButton());
        confirmInput.onSubmit.AddListener(_ =>
        {
            if (CanReset())
                ConfirmReset();
        });

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(panel.transform, false);
        var buttonsLayout = buttons.AddComponent<HorizontalLayoutGroup>();
        buttonsLayout.spacing = 10f;
        buttonsLayout.childControlWidth = true;
        buttonsLayout.childForceExpandWidth = true;
        buttonsLayout.childControlHeight = true;
        var buttonsElement = buttons.AddComponent<LayoutElement>();
        buttonsElement.preferredHeight = 38f;

        okButton = CreateButton(buttons.transform, "OK", ConfirmReset);
        CreateButton(buttons.transform, "Cancel", HideDialog);
        RefreshOkButton();
    }

    private void ShowDialog()
    {
        if (!dialogRoot)
            return;

        statusLabel.text = " ";
        confirmInput.SetTextWithoutNotify(string.Empty);
        RefreshOkButton();
        dialogRoot.SetActive(true);
        confirmInput.ActivateInputField();
        confirmInput.Select();
    }

    private void HideDialog()
    {
        if (dialogRoot)
            dialogRoot.SetActive(false);
    }

    private void ConfirmReset()
    {
        if (!CanReset())
            return;

        SingleplayerQuizProgressStore.ClearAll();
        HideDialog();
        statusLabel.text = "Saved progress reset.";
    }

    private bool CanReset()
    {
        return string.Equals(
            confirmInput ? confirmInput.text.Trim() : string.Empty,
            "reset",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private void RefreshOkButton()
    {
        if (okButton)
            okButton.interactable = CanReset();
    }

    private static Button CreateButton(Transform parent, string text, Action onClick)
    {
        var go = new GameObject(text, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.62f, 0.16f, 0.18f, 0.95f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick?.Invoke());

        var label = CreateText(go.transform, text, 18f, TextAlignmentOptions.Center);
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        var labelRt = (RectTransform)label.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        return button;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string text,
        float fontSize,
        TextAlignmentOptions alignment
    )
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.raycastTarget = false;
        go.AddComponent<LayoutElement>();
        return label;
    }

    private static TMP_InputField CreateInput(Transform parent)
    {
        var go = new GameObject("Confirm Input", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.95f, 0.96f, 0.98f, 1f);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = 38f;

        var input = go.AddComponent<TMP_InputField>();
        input.contentType = TMP_InputField.ContentType.Standard;
        input.lineType = TMP_InputField.LineType.SingleLine;

        var viewportGo = new GameObject("Text Area", typeof(RectTransform));
        viewportGo.transform.SetParent(go.transform, false);
        var viewport = (RectTransform)viewportGo.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(10f, 0f);
        viewport.offsetMax = new Vector2(-10f, 0f);
        viewportGo.AddComponent<RectMask2D>();

        var text = CreateText(viewportGo.transform, string.Empty, 20f, TextAlignmentOptions.MidlineLeft);
        text.color = new Color(0.08f, 0.10f, 0.13f, 1f);
        var textRt = (RectTransform)text.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        input.textComponent = text;

        var placeholder = CreateText(viewportGo.transform, "reset", 20f, TextAlignmentOptions.MidlineLeft);
        placeholder.color = new Color(0.40f, 0.42f, 0.46f, 0.75f);
        placeholder.fontStyle = FontStyles.Italic;
        var placeholderRt = (RectTransform)placeholder.transform;
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;
        input.textViewport = viewport;
        input.placeholder = placeholder;

        return input;
    }
}
