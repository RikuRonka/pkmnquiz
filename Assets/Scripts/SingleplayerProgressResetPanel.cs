using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SingleplayerProgressResetPanel : MonoBehaviour
{
    private const float ButtonWidth = 210f;
    private const float ButtonHeight = 34f;
    private const float BottomMargin = 84f;
    private const float ModalWidth = 540f;
    private const float ModalHeight = 420f;
    private const float ProgressListHeight = 242f;
    private const float ConfirmModalWidth = 430f;
    private const float ConfirmModalHeight = 220f;
    private static SingleplayerProgressResetPanel instance;
    private static bool overlayVisible = true;
    private Button resetButton;
    private TMP_Text statusLabel;
    private ScrollRect progressSummaryScrollRect;
    private RectTransform progressListContent;
    private GameObject dialogRoot;
    private GameObject confirmRoot;
    private TMP_Text confirmTitleText;
    private TMP_Text confirmMessageText;
    private int pendingResetGeneration;
    private string pendingResetTypeFilter;
    private string pendingResetQuizLabel;

    public static void EnsureInScene()
    {
        if (FindFirstObjectByType<SingleplayerProgressResetPanel>())
            return;

        var canvas = CreateCanvas();
        var go = new GameObject("Singleplayer Progress Reset", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.AddComponent<SingleplayerProgressResetPanel>();
    }

    public static void SetOverlayVisible(bool visible)
    {
        overlayVisible = visible;
        if (!instance)
            return;

        if (!visible)
            instance.HideDialog();

        instance.RefreshResetButtonVisibility();
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
        instance = this;
        BuildResetButton();
        BuildDialog();
        HideDialog();
        RefreshResetButtonVisibility();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void BuildResetButton()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(24f, BottomMargin);
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
            "Choose a saved quiz to reset. Only the selected quiz progress will be removed.",
            17f,
            TextAlignmentOptions.Left
        );
        message.color = new Color(0.88f, 0.92f, 0.96f, 1f);
        message.textWrappingMode = TextWrappingModes.Normal;
        message.GetComponent<LayoutElement>().preferredHeight = 48f;

        CreateProgressList(panel.transform);

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(panel.transform, false);
        var buttonsLayout = buttons.AddComponent<HorizontalLayoutGroup>();
        buttonsLayout.spacing = 10f;
        buttonsLayout.childControlWidth = true;
        buttonsLayout.childForceExpandWidth = true;
        buttonsLayout.childControlHeight = true;
        var buttonsElement = buttons.AddComponent<LayoutElement>();
        buttonsElement.preferredHeight = 38f;

        CreateButton(buttons.transform, "Close", HideDialog);
        BuildConfirmDialog();
    }

    private void ShowDialog()
    {
        if (!dialogRoot)
            return;

        statusLabel.text = " ";
        HideResetConfirmation();
        RefreshProgressRows();
        dialogRoot.SetActive(true);
    }

    private void HideDialog()
    {
        HideResetConfirmation();
        if (dialogRoot)
            dialogRoot.SetActive(false);
    }

    private void RefreshResetButtonVisibility()
    {
        bool show = overlayVisible && SingleplayerQuizProgressStore.HasAnyProgress();

        if (resetButton)
            resetButton.gameObject.SetActive(show);
        if (statusLabel)
            statusLabel.gameObject.SetActive(show && !string.IsNullOrWhiteSpace(statusLabel.text));

        if (!show)
            HideDialog();
    }

    private void RefreshProgressRows()
    {
        if (!progressListContent)
            return;

        ClearProgressRows();

        var sessions = SingleplayerQuizProgressStore
            .GetSessionsSnapshot()
            .OrderBy(s => string.IsNullOrWhiteSpace(s.typeFilter) ? 0 : 1)
            .ThenBy(s => s.generation)
            .ThenBy(s => s.typeFilter)
            .ToList();

        if (sessions.Count == 0)
        {
            CreateEmptyProgressRow();
            ResetProgressScroll();
            return;
        }

        foreach (var session in sessions)
            CreateProgressRow(session);

        ResetProgressScroll();
    }

    private void ClearProgressRows()
    {
        for (int i = progressListContent.childCount - 1; i >= 0; i--)
        {
            var child = progressListContent.GetChild(i).gameObject;
            child.SetActive(false);
            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }
    }

    private void CreateProgressRow(SingleplayerQuizProgressStore.Session session)
    {
        string quizLabel = DescribeQuiz(session.generation, session.typeFilter);
        int generation = session.generation;
        string typeFilter = session.typeFilter;

        var row = new GameObject("Progress Row", typeof(RectTransform));
        row.transform.SetParent(progressListContent, false);

        var image = row.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.06f);

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 8, 6, 6);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = 56f;
        rowElement.preferredHeight = 56f;

        var label = CreateText(
            row.transform,
            $"<b>{quizLabel}</b>\n{DescribeSessionProgress(session)}",
            14f,
            TextAlignmentOptions.Left
        );
        label.color = new Color(0.94f, 0.97f, 1f, 1f);
        label.textWrappingMode = TextWrappingModes.Normal;
        var labelElement = label.GetComponent<LayoutElement>();
        labelElement.flexibleWidth = 1f;
        labelElement.preferredHeight = 44f;

        CreateSmallResetButton(row.transform, () =>
            ShowResetConfirmation(quizLabel, generation, typeFilter)
        );
    }

    private void BuildConfirmDialog()
    {
        confirmRoot = new GameObject("Confirm Reset Progress", typeof(RectTransform));
        confirmRoot.transform.SetParent(dialogRoot.transform, false);
        var rootRt = (RectTransform)confirmRoot.transform;
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var blocker = confirmRoot.AddComponent<Image>();
        blocker.color = new Color(0f, 0f, 0f, 0.42f);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(confirmRoot.transform, false);
        var panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(ConfirmModalWidth, ConfirmModalHeight);

        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.10f, 0.12f, 0.15f, 1f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 20, 20);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        confirmTitleText = CreateText(panel.transform, string.Empty, 23f, TextAlignmentOptions.Left);
        confirmTitleText.fontStyle = FontStyles.Bold;
        confirmTitleText.color = Color.white;
        confirmTitleText.GetComponent<LayoutElement>().preferredHeight = 32f;

        confirmMessageText = CreateText(panel.transform, string.Empty, 16f, TextAlignmentOptions.Left);
        confirmMessageText.color = new Color(0.88f, 0.92f, 0.96f, 1f);
        confirmMessageText.textWrappingMode = TextWrappingModes.Normal;
        confirmMessageText.GetComponent<LayoutElement>().preferredHeight = 68f;

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(panel.transform, false);
        var buttonsLayout = buttons.AddComponent<HorizontalLayoutGroup>();
        buttonsLayout.spacing = 10f;
        buttonsLayout.childControlWidth = true;
        buttonsLayout.childForceExpandWidth = true;
        buttonsLayout.childControlHeight = true;
        buttons.AddComponent<LayoutElement>().preferredHeight = 38f;

        CreateButton(buttons.transform, "Reset", ConfirmPendingReset);
        CreateButton(buttons.transform, "Cancel", HideResetConfirmation);
        HideResetConfirmation();
    }

    private void ShowResetConfirmation(string quizLabel, int generation, string typeFilter)
    {
        pendingResetQuizLabel = quizLabel;
        pendingResetGeneration = generation;
        pendingResetTypeFilter = typeFilter;

        if (confirmTitleText)
            confirmTitleText.text = $"Reset {quizLabel}?";
        if (confirmMessageText)
        {
            confirmMessageText.text =
                "This removes only this saved singleplayer quiz progress. This cannot be undone.";
        }

        if (confirmRoot)
        {
            confirmRoot.transform.SetAsLastSibling();
            confirmRoot.SetActive(true);
        }
    }

    private void HideResetConfirmation()
    {
        if (confirmRoot)
            confirmRoot.SetActive(false);
    }

    private void ConfirmPendingReset()
    {
        if (string.IsNullOrWhiteSpace(pendingResetQuizLabel))
            return;

        SingleplayerQuizProgressStore.Remove(pendingResetGeneration, pendingResetTypeFilter);
        HideResetConfirmation();
        statusLabel.text = $"{pendingResetQuizLabel} progress reset.";
        pendingResetQuizLabel = null;
        pendingResetTypeFilter = null;
        RefreshProgressRows();
        RefreshResetButtonVisibility();
    }

    private void CreateEmptyProgressRow()
    {
        var label = CreateText(
            progressListContent,
            "No saved quiz progress.",
            16f,
            TextAlignmentOptions.Center
        );
        label.color = new Color(0.88f, 0.92f, 0.96f, 1f);
        label.GetComponent<LayoutElement>().preferredHeight = 52f;
    }

    private void ResetProgressScroll()
    {
        if (!progressSummaryScrollRect)
            return;

        Canvas.ForceUpdateCanvases();
        progressSummaryScrollRect.verticalNormalizedPosition = 1f;
    }

    private static string DescribeSessionProgress(SingleplayerQuizProgressStore.Session session)
    {
        int guessed = session.solvedIds?.Count ?? 0;
        int total = CountQuizTargets(session.generation, session.typeFilter);
        var parts = new List<string>
        {
            $"{guessed}/{(total > 0 ? total.ToString(CultureInfo.InvariantCulture) : "?")} guessed",
            $"{FormatElapsed(session.elapsed)} elapsed",
        };

        if (session.hintedIds != null && session.hintedIds.Count > 0)
            parts.Add($"{session.hintedIds.Count} hints");

        if (session.shadowedIds != null && session.shadowedIds.Count > 0)
            parts.Add($"{session.shadowedIds.Count} shadows");

        return string.Join(", ", parts);
    }

    private static string DescribeQuiz(int generation, string typeFilter)
    {
        if (!string.IsNullOrWhiteSpace(typeFilter))
            return $"{ToTitleCase(typeFilter)} type quiz";

        return Helpers.GetGenTitle(generation);
    }

    private static string ToTitleCase(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        if (value.Length == 0)
            return "Unknown";

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value);
    }

    private static string FormatElapsed(float elapsedSeconds)
    {
        var elapsed = TimeSpan.FromSeconds(Mathf.Max(0f, elapsedSeconds));
        if (elapsed.TotalHours >= 1d)
            return $"{(int)elapsed.TotalHours:0}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";

        return $"{elapsed.Minutes:0}:{elapsed.Seconds:00}";
    }

    private static int CountQuizTargets(int generation, string typeFilter)
    {
        try
        {
            PokemonDatabase.Instance.LoadIfNeeded();
            IEnumerable<Pokemon> all = PokemonDatabase.Instance.All();

            if (generation == 0)
            {
                all = all.Where(p =>
                    p.generation >= 1
                    && p.generation <= 9
                    && !Helpers.IsMega(p)
                    && !Helpers.IsGmax(p)
                    && !Helpers.IsHisui(p)
                    && !Helpers.IsPaldeaExpedition(p)
                    && !Helpers.IsLumioseMega(p)
                    && !Helpers.IsHyperspaceMega(p)
                );
            }
            else if (generation == 10)
            {
                all = all.Where(p =>
                    (Helpers.IsMega(p) || Helpers.IsLumioseMega(p) || Helpers.IsHyperspaceMega(p))
                    && !Helpers.IsGmax(p)
                );
            }
            else if (generation > 0)
            {
                var fullList = all.ToList();
                var genSet = fullList.Where(p =>
                    p.generation == generation
                    && !Helpers.IsMega(p)
                    && !Helpers.IsLumioseMega(p)
                    && !Helpers.IsHyperspaceMega(p)
                );
                IEnumerable<Pokemon> extras = Enumerable.Empty<Pokemon>();

                if (generation == 6)
                {
                    genSet = genSet.Where(p =>
                        !Helpers.IsMega(p)
                        && !Helpers.IsLumioseMega(p)
                        && !Helpers.IsHyperspaceMega(p)
                    );
                }
                else if (generation == 8)
                {
                    extras = fullList.Where(p =>
                        Helpers.IsGmax(p)
                        || (Helpers.IsHisui(p) && !Helpers.IsPaldeaExpeditionOrBloodmoon(p))
                    );
                }
                else if (generation == 9)
                {
                    extras = fullList.Where(Helpers.IsPaldeaExpedition);
                }

                all = genSet.Concat(extras).Distinct();
            }

            if (!string.IsNullOrWhiteSpace(typeFilter))
            {
                string key = typeFilter.Trim().ToLowerInvariant();
                all = all.Where(p =>
                    p.types != null
                    && p.types.Any(t => string.Equals(t, key, StringComparison.OrdinalIgnoreCase))
                );
            }

            var targets = all.ToList();
            if (generation == 9)
            {
                var taurosForms = targets.Where(Helpers.IsPaldeaTauros).ToList();
                if (taurosForms.Count > 0)
                {
                    targets.RemoveAll(Helpers.IsPaldeaTauros);
                    targets.Add(taurosForms[0]);
                }
            }

            return targets.Count;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Progress] Failed to count quiz targets: {ex.Message}");
            return 0;
        }
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

    private void CreateProgressList(Transform parent)
    {
        var go = new GameObject("Saved Progress", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.20f);

        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = ProgressListHeight;
        layout.minHeight = ProgressListHeight;

        progressSummaryScrollRect = go.AddComponent<ScrollRect>();
        progressSummaryScrollRect.horizontal = false;
        progressSummaryScrollRect.vertical = true;
        progressSummaryScrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(go.transform, false);
        var viewport = (RectTransform)viewportGo.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(8f, 5f);
        viewport.offsetMax = new Vector2(-8f, -5f);
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRt = (RectTransform)contentGo.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        var contentLayout = contentGo.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 6f;
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandHeight = false;
        contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        progressSummaryScrollRect.viewport = viewport;
        progressSummaryScrollRect.content = contentRt;
        progressListContent = contentRt;
    }

    private static Button CreateSmallResetButton(Transform parent, Action onClick)
    {
        var go = new GameObject("Reset Quiz", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.84f, 0.08f, 0.10f, 0.96f);

        var layout = go.AddComponent<LayoutElement>();
        layout.minWidth = 34f;
        layout.preferredWidth = 34f;
        layout.minHeight = 34f;
        layout.preferredHeight = 34f;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick?.Invoke());

        var label = CreateText(go.transform, "X", 18f, TextAlignmentOptions.Center);
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        var labelRt = (RectTransform)label.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        return button;
    }
}
