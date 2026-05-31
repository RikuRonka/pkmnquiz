using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class SingleplayerScoreboardPanel : MonoBehaviour
{
    private enum ScoreboardSortField
    {
        Date,
        CompletionTime,
        TypeReveals,
        Shadows,
        FillUsed,
    }

    private const float ButtonWidth = 210f;
    private const float ButtonHeight = 34f;
    private const float BottomMargin = 154f;
    private const float ModalWidth = 650f;
    private const float ModalHeight = 500f;
    private const float RecordListHeight = 372f;
    private const float RecordScrollSensitivity = 19f;
    private const float RecordScrollbarWidth = 12f;
    private const float RecordScrollbarMargin = 6f;
    private static readonly int[] GenerationRows = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    private static readonly Color StandardQuizColor = new(0.34f, 0.36f, 0.40f, 1f);
    private static readonly string[] TypeRows =
    {
        "bug",
        "dark",
        "dragon",
        "electric",
        "fairy",
        "fighting",
        "fire",
        "flying",
        "ghost",
        "grass",
        "ground",
        "ice",
        "normal",
        "poison",
        "psychic",
        "rock",
        "steel",
        "water",
    };
    private static SingleplayerScoreboardPanel instance;
    private static bool overlayVisible = true;

    private Button scoreboardButton;
    private GameObject dialogRoot;
    private GameObject deleteConfirmRoot;
    private TMP_Text deleteConfirmTitle;
    private TMP_Text deleteConfirmMessage;
    private ScrollRect recordsScrollRect;
    private RectTransform recordsListContent;
    private SingleplayerScoreboardStore.Record pendingDeleteRecord;
    private string pendingDeleteLabel;
    private readonly Dictionary<ScoreboardSortField, Button> sortButtons = new();
    private Button sortDirectionButton;
    private ScoreboardSortField sortField = ScoreboardSortField.Date;
    private bool sortAscending;

    public static void EnsureInScene()
    {
        if (FindFirstObjectByType<SingleplayerScoreboardPanel>())
            return;

        var canvas = CreateCanvas();
        var go = new GameObject("Singleplayer Scoreboard", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.AddComponent<SingleplayerScoreboardPanel>();
    }

    public static void SetOverlayVisible(bool visible)
    {
        overlayVisible = visible;
        if (!instance)
            return;

        if (!visible)
            instance.HideDialog();

        instance.RefreshButtonVisibility();
    }

    private static Canvas CreateCanvas()
    {
        var existing = GameObject.Find("Singleplayer Scoreboard Canvas");
        if (existing && existing.TryGetComponent(out Canvas existingCanvas))
        {
            ConfigureCanvas(existingCanvas);
            return existingCanvas;
        }

        var go = new GameObject("Singleplayer Scoreboard Canvas", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        ConfigureCanvas(canvas);
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static void ConfigureCanvas(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 555;

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
        BuildButton();
        BuildDialog();
        BuildDeleteConfirmationDialog();
        HideDialog();
        RefreshButtonVisibility();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void BuildButton()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(24f, BottomMargin);
        rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

        scoreboardButton = CreateButton(transform, "Scoreboard", ShowDialog);
        var buttonRt = (RectTransform)scoreboardButton.transform;
        buttonRt.anchorMin = Vector2.zero;
        buttonRt.anchorMax = Vector2.one;
        buttonRt.offsetMin = Vector2.zero;
        buttonRt.offsetMax = Vector2.zero;
    }

    private void BuildDialog()
    {
        dialogRoot = new GameObject("Scoreboard Dialog", typeof(RectTransform));
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
        panelRt.anchorMin = new Vector2(0.5f, 0f);
        panelRt.anchorMax = new Vector2(0.5f, 1f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(ModalWidth, 0f);
        panelRt.anchoredPosition = Vector2.zero;

        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.10f, 0.12f, 0.15f, 0.98f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 20, 20);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var title = CreateText(panel.transform, "Scoreboard", 26f, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<LayoutElement>().preferredHeight = 34f;

        var subtitle = CreateText(
            panel.transform,
            "Singleplayer personal bests",
            17f,
            TextAlignmentOptions.Left
        );
        subtitle.color = new Color(0.88f, 0.92f, 0.96f, 1f);
        subtitle.GetComponent<LayoutElement>().preferredHeight = 24f;

        CreateSortControls(panel.transform);
        CreateRecordList(panel.transform);

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(panel.transform, false);
        var buttonsLayout = buttons.AddComponent<HorizontalLayoutGroup>();
        buttonsLayout.spacing = 10f;
        buttonsLayout.childControlWidth = true;
        buttonsLayout.childForceExpandWidth = false;
        buttonsLayout.childControlHeight = true;
        buttonsLayout.childForceExpandHeight = false;
        buttonsLayout.childAlignment = TextAnchor.MiddleCenter;
        buttons.AddComponent<LayoutElement>().preferredHeight = ButtonHeight;

        var closeButton = CreateButton(buttons.transform, "Close", HideDialog);
        var closeLayout =
            closeButton.gameObject.GetComponent<LayoutElement>()
            ?? closeButton.gameObject.AddComponent<LayoutElement>();
        closeLayout.preferredWidth = ButtonWidth;
        closeLayout.preferredHeight = ButtonHeight;
        closeLayout.minWidth = ButtonWidth;
        closeLayout.minHeight = ButtonHeight;
        closeLayout.flexibleWidth = 0f;
        closeLayout.flexibleHeight = 0f;
    }

    private void ShowDialog()
    {
        if (!dialogRoot)
            return;

        RefreshRecordRows();
        dialogRoot.SetActive(true);
        dialogRoot.transform.SetAsLastSibling();
    }

    private void HideDialog()
    {
        HideDeleteConfirmation();
        if (dialogRoot)
            dialogRoot.SetActive(false);
    }

    private void RefreshButtonVisibility()
    {
        if (scoreboardButton)
            scoreboardButton.gameObject.SetActive(overlayVisible);

        if (!overlayVisible)
            HideDialog();
    }

    private void RefreshRecordRows()
    {
        if (!recordsListContent)
            return;

        ClearRows();

        var records = SingleplayerScoreboardStore.GetRecordsSnapshot();
        var standardGroups = records
            .Where(r => string.IsNullOrWhiteSpace(r.typeFilter))
            .GroupBy(r => r.generation)
            .ToDictionary(g => g.Key, g => SortRecords(g));

        CreateSectionRow("Quizzes");
        foreach (int generation in GenerationRows)
        {
            standardGroups.TryGetValue(generation, out var generationRecords);
            CreateQuizRecordGroup(
                Helpers.GetGenTitle(generation),
                generationRecords,
                StandardQuizColor
            );
        }

        var typeGroups = records
            .Where(r => !string.IsNullOrWhiteSpace(r.typeFilter))
            .GroupBy(r => r.typeFilter.Trim().ToLowerInvariant())
            .ToDictionary(
                g => g.Key,
                g => SortRecords(g),
                StringComparer.OrdinalIgnoreCase
            );

        CreateSectionRow("Type quizzes");

        foreach (var typeFilter in TypeRows)
        {
            typeGroups.TryGetValue(typeFilter, out var typeRecords);
            CreateQuizRecordGroup(
                $"{ToTitleCase(typeFilter)} type quiz",
                typeRecords,
                GetTypeColor(typeFilter)
            );
        }

        ResetRecordScroll();
    }

    private List<SingleplayerScoreboardStore.Record> SortRecords(
        IEnumerable<SingleplayerScoreboardStore.Record> records
    )
    {
        var validRecords = records?.Where(r => r != null).ToList()
            ?? new List<SingleplayerScoreboardStore.Record>();

        IOrderedEnumerable<SingleplayerScoreboardStore.Record> ordered = sortField switch
        {
            ScoreboardSortField.CompletionTime
                => sortAscending
                    ? validRecords.OrderBy(r => r.elapsedSeconds)
                    : validRecords.OrderByDescending(r => r.elapsedSeconds),
            ScoreboardSortField.TypeReveals
                => sortAscending
                    ? validRecords.OrderBy(r => r.typeRevealsUsed)
                    : validRecords.OrderByDescending(r => r.typeRevealsUsed),
            ScoreboardSortField.Shadows
                => sortAscending
                    ? validRecords.OrderBy(r => r.shadowsUsed)
                    : validRecords.OrderByDescending(r => r.shadowsUsed),
            ScoreboardSortField.FillUsed
                => sortAscending
                    ? validRecords.OrderBy(r => r.usedFillQuiz ? 1 : 0)
                    : validRecords.OrderByDescending(r => r.usedFillQuiz ? 1 : 0),
            _ => sortAscending
                ? validRecords.OrderBy(r => r.CompletedAtUtcValue)
                : validRecords.OrderByDescending(r => r.CompletedAtUtcValue),
        };

        return ordered
            .ThenByDescending(r => r.CompletedAtUtcValue)
            .ThenBy(r => r.elapsedSeconds)
            .ThenBy(r => r.typeRevealsUsed)
            .ThenBy(r => r.shadowsUsed)
            .ToList();
    }

    private void ClearRows()
    {
        for (int i = recordsListContent.childCount - 1; i >= 0; i--)
        {
            var child = recordsListContent.GetChild(i).gameObject;
            child.SetActive(false);
            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }
    }

    private void CreateSectionRow(string title)
    {
        var label = CreateText(recordsListContent, title, 16f, TextAlignmentOptions.Left);
        label.fontStyle = FontStyles.Bold;
        label.color = new Color(0.82f, 0.90f, 1f, 1f);
        label.GetComponent<LayoutElement>().preferredHeight = 28f;
    }

    private void CreateQuizRecordGroup(
        string title,
        IReadOnlyList<SingleplayerScoreboardStore.Record> records,
        Color color
    )
    {
        int entryCount = records?.Count ?? 0;
        CreateQuizHeaderRow(title, entryCount, color);

        if (entryCount <= 0)
        {
            CreateEmptyRow(color);
            return;
        }

        foreach (var record in records)
            CreateEntryRow(title, record, color);
    }

    private void CreateQuizHeaderRow(string titleText, int entryCount, Color color)
    {
        var row = new GameObject("Scoreboard Header", typeof(RectTransform));
        row.transform.SetParent(recordsListContent, false);

        var image = row.AddComponent<Image>();
        image.color = WithAlpha(color, 0.78f);

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 5, 5);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = 34f;
        rowElement.preferredHeight = 34f;

        var title = CreateText(row.transform, titleText, 15f, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        var titleLayout = title.GetComponent<LayoutElement>();
        titleLayout.flexibleWidth = 1f;
        titleLayout.preferredHeight = 24f;

        var count = CreateText(
            row.transform,
            $"{entryCount} entr{(entryCount == 1 ? "y" : "ies")}",
            13f,
            TextAlignmentOptions.Right
        );
        count.color = new Color(0.96f, 0.98f, 1f, 1f);
        var countLayout = count.GetComponent<LayoutElement>();
        countLayout.preferredWidth = 82f;
        countLayout.preferredHeight = 24f;
    }

    private void CreateEntryRow(string quizLabel, SingleplayerScoreboardStore.Record record, Color color)
    {
        var row = new GameObject("Scoreboard Entry", typeof(RectTransform));
        row.transform.SetParent(recordsListContent, false);

        var image = row.AddComponent<Image>();
        image.color = WithAlpha(color, 0.36f);

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(22, 10, 5, 6);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = 58f;
        rowElement.preferredHeight = record.usedFillQuiz ? 76f : 58f;

        var textColumn = new GameObject("Entry Text", typeof(RectTransform));
        textColumn.transform.SetParent(row.transform, false);
        var textLayout = textColumn.AddComponent<VerticalLayoutGroup>();
        textLayout.spacing = 1f;
        textLayout.childAlignment = TextAnchor.MiddleLeft;
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        var textElement = textColumn.AddComponent<LayoutElement>();
        textElement.flexibleWidth = 1f;
        textElement.preferredHeight = record.usedFillQuiz ? 64f : 46f;

        var stats = CreateText(textColumn.transform, FormatRecord(record), 14f, TextAlignmentOptions.Left);
        stats.color = new Color(0.95f, 0.98f, 1f, 1f);
        stats.GetComponent<LayoutElement>().preferredHeight = 22f;

        if (record.usedFillQuiz)
        {
            var fillQuiz = CreateText(
                textColumn.transform,
                "Fill quiz button used",
                12f,
                TextAlignmentOptions.Left
            );
            fillQuiz.color = new Color(1f, 0.88f, 0.32f, 1f);
            fillQuiz.GetComponent<LayoutElement>().preferredHeight = 18f;
        }

        var date = CreateText(
            textColumn.transform,
            FormatCompletedAt(record),
            12f,
            TextAlignmentOptions.Left
        );
        date.color = new Color(0.82f, 0.88f, 0.96f, 1f);
        date.GetComponent<LayoutElement>().preferredHeight = 18f;

        CreateDeleteButton(row.transform, () => ShowDeleteConfirmation(quizLabel, record));
    }

    private void CreateEmptyRow(Color color)
    {
        var row = new GameObject("Scoreboard Empty Entry", typeof(RectTransform));
        row.transform.SetParent(recordsListContent, false);

        var image = row.AddComponent<Image>();
        image.color = WithAlpha(color, 0.20f);

        var layout = row.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 10, 6, 6);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = 38f;
        rowElement.preferredHeight = 38f;

        var label = CreateText(row.transform, "No record", 14f, TextAlignmentOptions.Left);
        label.color = new Color(0.82f, 0.88f, 0.96f, 1f);
        label.GetComponent<LayoutElement>().preferredHeight = 24f;
    }

    private static string FormatRecord(SingleplayerScoreboardStore.Record record)
    {
        if (record == null)
            return "No record";

        return $"Time {FormatElapsed(record.elapsedSeconds)}  |  Type reveals {record.typeRevealsUsed}  |  Shadows {record.shadowsUsed}";
    }

    private static string FormatCompletedAt(SingleplayerScoreboardStore.Record record)
    {
        if (record == null)
            return string.Empty;

        var local = record.CompletedAtUtcValue.ToLocalTime();
        return local.ToString("d.M.yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    private static Color GetTypeColor(string typeFilter)
    {
        if (string.IsNullOrWhiteSpace(typeFilter))
            return new Color(0.68f, 0.72f, 0.78f);

        typeFilter = typeFilter.Trim().ToLowerInvariant();
        if (TryGetTypeButtonColor(typeFilter, out var buttonColor))
            return buttonColor;

        switch (typeFilter)
        {
            case "bug":
                return new Color(0.65f, 0.73f, 0.21f);
            case "dark":
                return new Color(0.35f, 0.34f, 0.39f);
            case "dragon":
                return new Color(0.42f, 0.22f, 0.87f);
            case "electric":
                return new Color(0.94f, 0.82f, 0.20f);
            case "fairy":
                return new Color(0.91f, 0.62f, 0.70f);
            case "fighting":
                return new Color(0.77f, 0.25f, 0.26f);
            case "fire":
                return new Color(0.93f, 0.46f, 0.22f);
            case "flying":
                return new Color(0.71f, 0.62f, 0.96f);
            case "ghost":
                return new Color(0.44f, 0.36f, 0.61f);
            case "grass":
                return new Color(0.48f, 0.76f, 0.29f);
            case "ground":
                return new Color(0.86f, 0.74f, 0.37f);
            case "ice":
                return new Color(0.56f, 0.82f, 0.89f);
            case "normal":
                return new Color(0.67f, 0.66f, 0.58f);
            case "poison":
                return new Color(0.63f, 0.33f, 0.65f);
            case "psychic":
                return new Color(0.96f, 0.49f, 0.64f);
            case "rock":
                return new Color(0.70f, 0.63f, 0.33f);
            case "steel":
                return new Color(0.69f, 0.70f, 0.79f);
            case "water":
                return new Color(0.26f, 0.52f, 0.93f);
            default:
                return new Color(0.55f, 0.65f, 0.80f);
        }
    }

    private static bool TryGetTypeButtonColor(string typeFilter, out Color color)
    {
        color = default;

        foreach (
            var typeButton in FindObjectsByType<TypeFilterButton>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            if (
                !typeButton
                || !string.Equals(typeButton.TypeName, typeFilter, StringComparison.OrdinalIgnoreCase)
                || !typeButton.TryGetComponent(out Button button)
            )
            {
                continue;
            }

            if (TryGetButtonDisplayColor(button, out color))
                return true;
        }

        var namedButton = GameObject.Find(ToTitleCase(typeFilter))?.GetComponent<Button>();
        if (!namedButton)
            return false;

        return TryGetButtonDisplayColor(namedButton, out color);
    }

    private static bool TryGetButtonDisplayColor(Button button, out Color color)
    {
        color = default;

        var normal = button.colors.normalColor;
        if (!IsDefaultWhite(normal))
        {
            color = normal;
            return true;
        }

        if (button.targetGraphic && !IsDefaultWhite(button.targetGraphic.color))
        {
            color = button.targetGraphic.color;
            return true;
        }

        return false;
    }

    private static bool IsDefaultWhite(Color color)
    {
        return Mathf.Approximately(color.r, 1f)
            && Mathf.Approximately(color.g, 1f)
            && Mathf.Approximately(color.b, 1f)
            && Mathf.Approximately(color.a, 1f);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private void ResetRecordScroll()
    {
        if (!recordsScrollRect)
            return;

        Canvas.ForceUpdateCanvases();
        recordsScrollRect.verticalNormalizedPosition = 1f;
    }

    private static string FormatElapsed(float elapsedSeconds)
    {
        var elapsed = TimeSpan.FromSeconds(Mathf.Max(0f, elapsedSeconds));
        if (elapsed.TotalHours >= 1d)
            return $"{(int)elapsed.TotalHours:0}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";

        return $"{elapsed.Minutes:0}:{elapsed.Seconds:00}";
    }

    private static string ToTitleCase(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        if (value.Length == 0)
            return "Unknown";

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value);
    }

    private void BuildDeleteConfirmationDialog()
    {
        deleteConfirmRoot = new GameObject("Confirm Delete Scoreboard Entry", typeof(RectTransform));
        deleteConfirmRoot.transform.SetParent(dialogRoot.transform, false);
        var rootRt = (RectTransform)deleteConfirmRoot.transform;
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var blocker = deleteConfirmRoot.AddComponent<Image>();
        blocker.color = new Color(0f, 0f, 0f, 0.46f);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(deleteConfirmRoot.transform, false);
        var panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(440f, 220f);

        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.10f, 0.12f, 0.15f, 1f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 20, 20);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        deleteConfirmTitle = CreateText(panel.transform, "Delete scoreboard entry?", 23f, TextAlignmentOptions.Left);
        deleteConfirmTitle.fontStyle = FontStyles.Bold;
        deleteConfirmTitle.color = Color.white;
        deleteConfirmTitle.GetComponent<LayoutElement>().preferredHeight = 32f;

        deleteConfirmMessage = CreateText(panel.transform, string.Empty, 16f, TextAlignmentOptions.Left);
        deleteConfirmMessage.color = new Color(0.88f, 0.92f, 0.96f, 1f);
        deleteConfirmMessage.textWrappingMode = TextWrappingModes.Normal;
        deleteConfirmMessage.GetComponent<LayoutElement>().preferredHeight = 68f;

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(panel.transform, false);
        var buttonsLayout = buttons.AddComponent<HorizontalLayoutGroup>();
        buttonsLayout.spacing = 10f;
        buttonsLayout.childControlWidth = true;
        buttonsLayout.childForceExpandWidth = true;
        buttonsLayout.childControlHeight = true;
        buttons.AddComponent<LayoutElement>().preferredHeight = ButtonHeight;

        CreateButton(buttons.transform, "Delete", ConfirmPendingDelete, new Color(0.82f, 0.08f, 0.10f, 0.96f));
        CreateButton(buttons.transform, "Cancel", HideDeleteConfirmation);
        HideDeleteConfirmation();
    }

    private void ShowDeleteConfirmation(
        string quizLabel,
        SingleplayerScoreboardStore.Record record
    )
    {
        pendingDeleteRecord = record?.Clone();
        pendingDeleteLabel = quizLabel;

        if (deleteConfirmTitle)
            deleteConfirmTitle.text = "Delete scoreboard entry?";

        if (deleteConfirmMessage)
        {
            string completedAt = record != null ? FormatCompletedAt(record) : "this entry";
            deleteConfirmMessage.text =
                $"Remove {quizLabel} score from {completedAt}? This also updates the saved scoreboard JSON.";
        }

        if (deleteConfirmRoot)
        {
            deleteConfirmRoot.transform.SetAsLastSibling();
            deleteConfirmRoot.SetActive(true);
        }
    }

    private void HideDeleteConfirmation()
    {
        pendingDeleteRecord = null;
        pendingDeleteLabel = null;

        if (deleteConfirmRoot)
            deleteConfirmRoot.SetActive(false);
    }

    private void ConfirmPendingDelete()
    {
        if (pendingDeleteRecord == null)
            return;

        SingleplayerScoreboardStore.Remove(pendingDeleteRecord);
        HideDeleteConfirmation();
        RefreshRecordRows();
    }

    private static Button CreateButton(
        Transform parent,
        string text,
        Action onClick,
        Color? color = null
    )
    {
        var go = new GameObject(text, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = color ?? new Color(0.14f, 0.37f, 0.68f, 0.96f);

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

    private static Button CreateDeleteButton(Transform parent, Action onClick)
    {
        var button = CreateButton(
            parent,
            "X",
            onClick,
            new Color(0.84f, 0.08f, 0.10f, 0.96f)
        );

        var layout =
            button.gameObject.GetComponent<LayoutElement>()
            ?? button.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = 28f;
        layout.preferredWidth = 28f;
        layout.minHeight = 28f;
        layout.preferredHeight = 28f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label)
            label.fontSize = 16f;

        return button;
    }

    private void CreateSortControls(Transform parent)
    {
        var row = new GameObject("Sort Controls", typeof(RectTransform));
        row.transform.SetParent(parent, false);

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        row.AddComponent<LayoutElement>().preferredHeight = 30f;

        var label = CreateText(row.transform, "Sort", 13f, TextAlignmentOptions.Left);
        label.color = new Color(0.82f, 0.90f, 1f, 1f);
        label.fontStyle = FontStyles.Bold;
        var labelLayout = label.GetComponent<LayoutElement>();
        labelLayout.preferredWidth = 42f;
        labelLayout.preferredHeight = 28f;

        sortButtons.Clear();
        AddSortButton(row.transform, ScoreboardSortField.Date, "Date", 58f);
        AddSortButton(row.transform, ScoreboardSortField.CompletionTime, "Time", 58f);
        AddSortButton(row.transform, ScoreboardSortField.TypeReveals, "Type reveals", 102f);
        AddSortButton(row.transform, ScoreboardSortField.Shadows, "Shadows", 82f);
        AddSortButton(row.transform, ScoreboardSortField.FillUsed, "Fill used", 78f);

        sortDirectionButton = CreateButton(row.transform, "Desc", ToggleSortDirection);
        ConfigureSortButtonLayout(sortDirectionButton, 62f);
        RefreshSortButtons();
    }

    private void AddSortButton(
        Transform parent,
        ScoreboardSortField field,
        string label,
        float width
    )
    {
        var button = CreateButton(parent, label, () => SetSortField(field));
        ConfigureSortButtonLayout(button, width);
        sortButtons[field] = button;
    }

    private static void ConfigureSortButtonLayout(Button button, float width)
    {
        var layout =
            button.gameObject.GetComponent<LayoutElement>()
            ?? button.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.minHeight = 26f;
        layout.preferredHeight = 26f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label)
            label.fontSize = 12f;
    }

    private void SetSortField(ScoreboardSortField field)
    {
        if (sortField == field)
            sortAscending = !sortAscending;
        else
        {
            sortField = field;
            sortAscending = DefaultSortAscending(field);
        }

        RefreshSortButtons();
        RefreshRecordRows();
    }

    private void ToggleSortDirection()
    {
        sortAscending = !sortAscending;
        RefreshSortButtons();
        RefreshRecordRows();
    }

    private static bool DefaultSortAscending(ScoreboardSortField field)
    {
        return field is ScoreboardSortField.CompletionTime
            or ScoreboardSortField.TypeReveals
            or ScoreboardSortField.Shadows;
    }

    private void RefreshSortButtons()
    {
        foreach (var pair in sortButtons)
            SetButtonColor(pair.Value, pair.Key == sortField ? ActiveSortColor() : InactiveSortColor());

        if (sortDirectionButton)
        {
            SetButtonColor(sortDirectionButton, new Color(0.18f, 0.31f, 0.46f, 0.96f));
            var label = sortDirectionButton.GetComponentInChildren<TMP_Text>(true);
            if (label)
                label.text = sortAscending ? "Asc" : "Desc";
        }
    }

    private static Color ActiveSortColor()
    {
        return new Color(0.18f, 0.46f, 0.78f, 0.96f);
    }

    private static Color InactiveSortColor()
    {
        return new Color(0.22f, 0.25f, 0.31f, 0.96f);
    }

    private static void SetButtonColor(Button button, Color color)
    {
        if (button && button.targetGraphic)
            button.targetGraphic.color = color;
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

    private void CreateRecordList(Transform parent)
    {
        var go = new GameObject("Scoreboard Records", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.20f);

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 120f;
        layout.flexibleHeight = 1f;

        recordsScrollRect = go.AddComponent<ScoreboardSmoothScrollRect>();
        recordsScrollRect.horizontal = false;
        recordsScrollRect.vertical = true;
        recordsScrollRect.movementType = ScrollRect.MovementType.Clamped;
        recordsScrollRect.inertia = false;
        recordsScrollRect.scrollSensitivity = RecordScrollSensitivity;

        var scrollbar = CreateRecordScrollbar(go.transform);
        recordsScrollRect.verticalScrollbar = scrollbar;
        recordsScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        recordsScrollRect.verticalScrollbarSpacing = 0f;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(go.transform, false);
        var viewport = (RectTransform)viewportGo.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(8f, 5f);
        viewport.offsetMax = new Vector2(
            -(8f + RecordScrollbarWidth + RecordScrollbarMargin),
            -5f
        );
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
        contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter
            .FitMode
            .PreferredSize;

        recordsScrollRect.viewport = viewport;
        recordsScrollRect.content = contentRt;
        recordsListContent = contentRt;
    }

    private static Scrollbar CreateRecordScrollbar(Transform parent)
    {
        var trackGo = new GameObject("Scrollbar", typeof(RectTransform));
        trackGo.transform.SetParent(parent, false);
        var trackRt = (RectTransform)trackGo.transform;
        trackRt.anchorMin = new Vector2(1f, 0f);
        trackRt.anchorMax = new Vector2(1f, 1f);
        trackRt.offsetMin = new Vector2(
            -(RecordScrollbarWidth + RecordScrollbarMargin),
            5f
        );
        trackRt.offsetMax = new Vector2(-RecordScrollbarMargin, -5f);

        var trackImage = trackGo.AddComponent<Image>();
        trackImage.color = new Color(0f, 0f, 0f, 0.35f);

        var scrollbar = trackGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.transition = Selectable.Transition.None;

        var slidingGo = new GameObject("Sliding Area", typeof(RectTransform));
        slidingGo.transform.SetParent(trackGo.transform, false);
        var slidingRt = (RectTransform)slidingGo.transform;
        slidingRt.anchorMin = Vector2.zero;
        slidingRt.anchorMax = Vector2.one;
        slidingRt.offsetMin = new Vector2(2f, 2f);
        slidingRt.offsetMax = new Vector2(-2f, -2f);

        var handleGo = new GameObject("Handle", typeof(RectTransform));
        handleGo.transform.SetParent(slidingGo.transform, false);
        var handleRt = (RectTransform)handleGo.transform;
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.offsetMin = Vector2.zero;
        handleRt.offsetMax = Vector2.zero;

        var handleImage = handleGo.AddComponent<Image>();
        handleImage.color = new Color(0.92f, 0.95f, 1f, 0.70f);

        scrollbar.handleRect = handleRt;
        scrollbar.targetGraphic = handleImage;
        return scrollbar;
    }
}

internal sealed class ScoreboardSmoothScrollRect : ScrollRect
{
    private const float SmoothTime = 0.08f;
    private bool hasScrollTarget;
    private float scrollTarget;
    private float scrollVelocity;

    protected override void OnEnable()
    {
        base.OnEnable();
        ResetSmoothScroll();
    }

    public override void OnScroll(PointerEventData data)
    {
        if (!IsActive() || !vertical || !content || !viewport)
        {
            base.OnScroll(data);
            return;
        }

        float scrollDelta = data.scrollDelta.y;
        if (Mathf.Approximately(scrollDelta, 0f))
            return;

        float scrollableHeight = Mathf.Max(1f, content.rect.height - viewport.rect.height);
        float start = hasScrollTarget ? scrollTarget : verticalNormalizedPosition;
        scrollTarget = Mathf.Clamp01(start + scrollDelta * scrollSensitivity / scrollableHeight);
        hasScrollTarget = true;
        data.Use();
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();

        if (!hasScrollTarget)
            return;

        float next = Mathf.SmoothDamp(
            verticalNormalizedPosition,
            scrollTarget,
            ref scrollVelocity,
            SmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime
        );

        verticalNormalizedPosition = Mathf.Clamp01(next);
        if (Mathf.Abs(verticalNormalizedPosition - scrollTarget) <= 0.001f)
        {
            verticalNormalizedPosition = scrollTarget;
            ResetSmoothScroll();
        }
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        ResetSmoothScroll();
        base.OnBeginDrag(eventData);
    }

    private void ResetSmoothScroll()
    {
        hasScrollTarget = false;
        scrollTarget = verticalNormalizedPosition;
        scrollVelocity = 0f;
    }
}
