using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(VerticalLayoutGroup))]
[RequireComponent(typeof(ContentSizeFitter))]
public class SectionGroup : MonoBehaviour
{
    [Header("Refs (assign in prefab OR created at runtime)")]
    public RectTransform headerRect;
    public RectTransform gridRoot;

    [SerializeField]
    Image typeIcon;

    [SerializeField]
    TMP_Text titleText;

    [SerializeField]
    RectTransform headerSpacer;

    [SerializeField]
    float normalGap = 16f;

    [SerializeField]
    float mainOnlyGap = 64f;
    public int CardCount { get; private set; }

    [Header("Scaling")]
    [SerializeField]
    float fontSizeLarge = 40f;

    [SerializeField]
    float fontSizeSmall = 22f;
    const float LEFT_MARGIN = 0f;

    [SerializeField]
    float baseFontSize = 36f;
    private bool _isMainHeader;
    private string _baseTitle = "";
    private const float HeaderTextMinSize = 14f;
    private const float HeaderHeight = 52f;

    [SerializeField]
    float mainHeaderIconSpacing = 24f;
    private bool _mainOnlyScreen;
    private bool _fitStateCaptured;
    private ContentSizeFitter.FitMode _normalHorizontalFit;
    private ContentSizeFitter.FitMode _normalVerticalFit;
    private Vector2 _normalRootAnchorMin;
    private Vector2 _normalRootAnchorMax;
    private Vector2 _normalRootPivot;
    private Vector2 _normalRootSizeDelta;
    private Vector2 _normalRootAnchoredPosition;
    private const float FitMinTitleFont = 5f;
    private const float FitMinHeaderHeight = 7f;
    private const float FitMinSpacing = 0f;

    public void SetCardCount(int n)
    {
        CardCount = Mathf.Max(0, n);
    }

    public void SetTitleColor(Color color)
    {
        if (titleText)
            titleText.color = color;

        if (!headerRect)
            return;

        foreach (var text in headerRect.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text)
                text.color = color;
        }
    }

    public void UpdateHeaderForCols(int cols, int minCols, int maxCols)
    {
        if (!titleText)
            return;

        if (_isMainHeader)
            return;

        float t = Mathf.InverseLerp(minCols, maxCols, cols);

        float size = Mathf.Lerp(fontSizeLarge, fontSizeSmall, t);
        ConfigureSingleLineTitle(titleText.rectTransform, size);
        titleText.fontSize = size;
        titleText.fontSizeMax = size;
    }

    public void EnsureLayout()
    {
        var vlg = GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 16;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var groupLayout = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        groupLayout.flexibleHeight = 0f;

        if (!headerRect && titleText)
            headerRect = titleText.rectTransform.parent as RectTransform;

        if (!headerRect)
        {
            var hdrGO = new GameObject("Header", typeof(RectTransform), typeof(Image));
            hdrGO.transform.SetParent(transform, false);
            headerRect = hdrGO.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1);
            headerRect.sizeDelta = new Vector2(0, 44);
            var img = hdrGO.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0.3f);
        }

        headerRect.SetSiblingIndex(0);
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(0f, HeaderHeight);
        var headerImage = headerRect.GetComponent<Image>();
        if (headerImage)
            headerImage.color = new Color(0f, 0f, 0f, 0f);
        var headerLayout = headerRect.GetComponent<LayoutElement>()
            ?? headerRect.gameObject.AddComponent<LayoutElement>();
        headerLayout.minHeight = HeaderHeight;
        headerLayout.preferredHeight = HeaderHeight;
        headerLayout.flexibleHeight = 0f;

        if (!titleText)
        {
            var lblGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGO.transform.SetParent(headerRect, false);
            var lrt = (RectTransform)lblGO.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0, 0.5f);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = new Vector2(16, 0);
            titleText = lblGO.GetComponent<TextMeshProUGUI>();
            titleText.alignment = TextAlignmentOptions.TopLeft;
            titleText.fontSize = 36;
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 18;
            titleText.fontSizeMax = 42;
            titleText.color = Color.black;
            titleText.text = "Header";
        }

        if (titleText && titleText.rectTransform.parent != headerRect)
            titleText.rectTransform.SetParent(headerRect, false);

        if (typeIcon)
            typeIcon.rectTransform.SetParent(headerRect, false);

        if (!gridRoot)
        {
            var gridGO = new GameObject("GridRoot", typeof(RectTransform));
            gridGO.transform.SetParent(transform, false);
            gridRoot = gridGO.GetComponent<RectTransform>();
            gridRoot.anchorMin = new Vector2(0, 1);
            gridRoot.anchorMax = new Vector2(1, 1);
            gridRoot.pivot = new Vector2(0.5f, 1);
            gridRoot.sizeDelta = new Vector2(0, 100);
        }

        if (headerSpacer && headerSpacer != headerRect)
        {
            headerSpacer.SetSiblingIndex(1);
            var spacerLayout = headerSpacer.GetComponent<LayoutElement>()
                ?? headerSpacer.gameObject.AddComponent<LayoutElement>();
            spacerLayout.minHeight = normalGap;
            spacerLayout.preferredHeight = normalGap;
            spacerLayout.flexibleHeight = 0f;
        }
        gridRoot.SetSiblingIndex(headerSpacer && headerSpacer != headerRect ? 2 : 1);

        var grid = gridRoot.GetComponent<GridLayoutGroup>();
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.spacing = new Vector2(12, 12);
        grid.padding = new RectOffset(0, 0, 0, 0);

        var le = gridRoot.GetComponent<LayoutElement>();
        le.minHeight = 0;
        le.preferredHeight = 100;
        RefreshPreferredHeight();
    }

    public void SetTitle(string text, bool isMain, Sprite icon = null)
    {
        if (!titleText)
            return;

        _isMainHeader = isMain;
        _baseTitle = text ?? "";
        titleText.enableAutoSizing = false;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Overflow;
        titleText.text = _baseTitle;
        titleText.color = Color.black;

        var titleRT = titleText.rectTransform;

        if (isMain)
        {
            if (!Helpers.IsGenTitle(text))
            {
                gridRoot.gameObject.SetActive(false);
            }

            titleText.alignment = TextAlignmentOptions.Center;
            ConfigureSingleLineTitle(titleRT, baseFontSize);

            RectTransform iconRT = null;
            float iconW = 0f;

            if (typeIcon)
            {
                typeIcon.sprite = icon;
                typeIcon.enabled = icon != null;

                iconRT = typeIcon.rectTransform;
                iconRT.anchorMin = iconRT.anchorMax = new Vector2(0.5f, 0.5f);
                iconRT.pivot = new Vector2(0.5f, 0.5f);
                iconW = iconRT.rect.width;
            }

            titleRT.offsetMin = new Vector2(8f, 0f);
            titleRT.offsetMax = new Vector2(-8f, 0f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(titleRT);
            float textW = titleText.preferredWidth;

            float spacing = (iconW > 0f && textW > 0f) ? mainHeaderIconSpacing : 0f;
            float totalW = iconW + spacing + textW;

            float left = -totalW * 0.5f;

            if (iconRT && iconW > 0f)
            {
                float iconCenterX = left + iconW * 0.5f;
                iconRT.anchoredPosition = new Vector2(iconCenterX, 0f);
            }

            float titleCenterX = left + iconW + spacing + textW * 0.5f;
            if (iconRT && iconW > 0f)
            {
                titleRT.anchorMin = titleRT.anchorMax = new Vector2(0.5f, 0.5f);
                titleRT.pivot = new Vector2(0.5f, 0.5f);
                titleRT.sizeDelta = new Vector2(textW, HeaderHeight);
                titleRT.anchoredPosition = new Vector2(titleCenterX, 0f);
            }

            titleText.fontSize = baseFontSize;
            titleText.fontSizeMin = HeaderTextMinSize;
            titleText.fontSizeMax = baseFontSize;

            return;
        }

        titleText.alignment = TextAlignmentOptions.MidlineLeft;
        ConfigureSingleLineTitle(titleRT, titleText.fontSize);

        if (typeIcon)
        {
            typeIcon.sprite = icon;
            typeIcon.enabled = icon != null;
        }
    }

    public void SetSectionProgress(int guessed, int total)
    {
        if (!titleText || _isMainHeader)
            return;

        string baseTitle = string.IsNullOrEmpty(_baseTitle) ? titleText.text : _baseTitle;
        guessed = Mathf.Clamp(guessed, 0, Mathf.Max(0, total));
        total = Mathf.Max(0, total);
        titleText.text = $"{baseTitle} - {guessed}/{total}";
    }

    public void ClearSectionProgress()
    {
        if (!titleText || _isMainHeader || string.IsNullOrEmpty(_baseTitle))
            return;

        titleText.text = _baseTitle;
    }

    private void ConfigureSingleLineTitle(RectTransform titleRT, float maxFontSize)
    {
        if (!titleText || !titleRT)
            return;

        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = HeaderTextMinSize;
        titleText.fontSizeMax = Mathf.Max(HeaderTextMinSize, maxFontSize);
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Overflow;

        titleRT.anchorMin = new Vector2(0f, 0f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0f, 0.5f);
        titleRT.offsetMin = new Vector2(LEFT_MARGIN, 0f);
        titleRT.offsetMax = Vector2.zero;
    }

    public void SetHeaderGap(bool mainOnlyScreen)
    {
        _mainOnlyScreen = mainOnlyScreen;
        if (!headerSpacer)
            return;
        if (headerSpacer.GetComponent<LayoutElement>() == null)
        {
            headerSpacer.AddComponent<LayoutElement>();
        }
        var le = headerSpacer.GetComponent<LayoutElement>();
        float gap = mainOnlyScreen ? mainOnlyGap : normalGap;
        le.minHeight = gap;
        le.preferredHeight = gap;
        le.flexibleHeight = 0f;
        RefreshPreferredHeight();
    }

    public float GetFitSectionHeight(float cell, float gap, int columns, float scale)
    {
        float total = 0f;
        int activeParts = 0;

        if (headerRect && headerRect.gameObject.activeSelf)
        {
            total += FitHeaderHeight(scale);
            activeParts++;
        }

        if (headerSpacer && headerSpacer != headerRect && headerSpacer.gameObject.activeSelf)
        {
            total += FitHeaderGap(scale);
            activeParts++;
        }

        if (HasActiveGrid())
        {
            total += FitGridHeight(cell, gap, columns);
            activeParts++;
        }

        if (activeParts > 1)
            total += FitGroupSpacing(scale) * (activeParts - 1);

        return Mathf.Max(0f, total);
    }

    public void ApplyFitLayout(float cell, float gap, int columns, float scale)
    {
        scale = Mathf.Max(0.001f, scale);
        CaptureFitState();
        ApplyFitRootGeometry();

        var vlg = GetComponent<VerticalLayoutGroup>();
        if (vlg)
        {
            vlg.spacing = FitGroupSpacing(scale);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        ApplyHeaderFit(scale);
        ApplyHeaderSpacerFit(scale);
        ApplyGridFit(cell, gap, columns);
        RefreshPreferredHeight();
    }

    public void RestoreNormalLayout()
    {
        RestoreFitState();

        var vlg = GetComponent<VerticalLayoutGroup>();
        if (vlg)
        {
            vlg.spacing = 16f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        if (headerRect)
        {
            headerRect.sizeDelta = new Vector2(headerRect.sizeDelta.x, HeaderHeight);
            var headerLayout =
                headerRect.GetComponent<LayoutElement>()
                ?? headerRect.gameObject.AddComponent<LayoutElement>();
            headerLayout.minHeight = HeaderHeight;
            headerLayout.preferredHeight = HeaderHeight;
            headerLayout.flexibleHeight = 0f;
        }

        if (titleText)
        {
            if (_isMainHeader)
            {
                Sprite icon = typeIcon && typeIcon.enabled ? typeIcon.sprite : null;
                SetTitle(titleText.text, true, icon);
            }

            float maxFont = _isMainHeader ? baseFontSize : titleText.fontSizeMax;
            if (!_isMainHeader)
                ConfigureSingleLineTitle(titleText.rectTransform, maxFont);
            titleText.fontSize = maxFont;
            titleText.fontSizeMax = maxFont;
            titleText.fontSizeMin = HeaderTextMinSize;
        }

        if (typeIcon)
            typeIcon.rectTransform.sizeDelta = new Vector2(38f, 38f);

        if (gridRoot)
        {
            var grid = gridRoot.GetComponent<GridLayoutGroup>();
            if (grid)
            {
                grid.childAlignment = TextAnchor.UpperLeft;
                grid.spacing = new Vector2(12f, 12f);
                grid.padding = new RectOffset(0, 0, 0, 0);
            }
        }

        SetHeaderGap(_mainOnlyScreen);
    }

    private void CaptureFitState()
    {
        if (_fitStateCaptured)
            return;

        var fitter = GetComponent<ContentSizeFitter>();
        if (fitter)
        {
            _normalHorizontalFit = fitter.horizontalFit;
            _normalVerticalFit = fitter.verticalFit;
        }

        if (transform is RectTransform rt)
        {
            _normalRootAnchorMin = rt.anchorMin;
            _normalRootAnchorMax = rt.anchorMax;
            _normalRootPivot = rt.pivot;
            _normalRootSizeDelta = rt.sizeDelta;
            _normalRootAnchoredPosition = rt.anchoredPosition;
        }

        _fitStateCaptured = true;
    }

    private void RestoreFitState()
    {
        if (!_fitStateCaptured)
            return;

        var fitter = GetComponent<ContentSizeFitter>();
        if (fitter)
        {
            fitter.horizontalFit = _normalHorizontalFit;
            fitter.verticalFit = _normalVerticalFit;
        }

        if (transform is RectTransform rt)
        {
            rt.anchorMin = _normalRootAnchorMin;
            rt.anchorMax = _normalRootAnchorMax;
            rt.pivot = _normalRootPivot;
            rt.sizeDelta = _normalRootSizeDelta;
            rt.anchoredPosition = _normalRootAnchoredPosition;
        }

        _fitStateCaptured = false;
    }

    private void ApplyFitRootGeometry()
    {
        var fitter = GetComponent<ContentSizeFitter>();
        if (fitter)
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        if (transform is RectTransform rt)
        {
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
            rt.pivot = new Vector2(0.5f, rt.pivot.y);
            rt.sizeDelta = new Vector2(0f, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);
        }
    }

    private bool HasActiveGrid()
    {
        return gridRoot && gridRoot.gameObject.activeSelf && CardCount > 0;
    }

    private float FitHeaderHeight(float scale)
    {
        return Mathf.Max(FitMinHeaderHeight, HeaderHeight * scale);
    }

    private float FitHeaderGap(float scale)
    {
        float gap = _mainOnlyScreen ? mainOnlyGap : normalGap;
        return Mathf.Max(FitMinSpacing, gap * scale);
    }

    private float FitGroupSpacing(float scale)
    {
        return Mathf.Max(FitMinSpacing, 16f * scale);
    }

    private float FitGridHeight(float cell, float gap, int columns)
    {
        int cols = Mathf.Max(1, columns);
        int rows = Mathf.CeilToInt((float)CardCount / cols);
        return rows * cell + Mathf.Max(0, rows - 1) * gap;
    }

    private void ApplyHeaderFit(float scale)
    {
        if (!headerRect)
            return;

        float headerHeight = FitHeaderHeight(scale);
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0f, headerRect.anchoredPosition.y);
        headerRect.sizeDelta = new Vector2(0f, headerHeight);

        var headerLayout =
            headerRect.GetComponent<LayoutElement>()
            ?? headerRect.gameObject.AddComponent<LayoutElement>();
        headerLayout.minHeight = headerHeight;
        headerLayout.preferredHeight = headerHeight;
        headerLayout.flexibleHeight = 0f;

        if (titleText)
        {
            float maxFont = Mathf.Max(FitMinTitleFont, baseFontSize * scale);
            ConfigureSingleLineTitle(titleText.rectTransform, maxFont);
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = Mathf.Min(FitMinTitleFont, maxFont);
            titleText.fontSizeMax = maxFont;
            titleText.fontSize = maxFont;
        }

        if (typeIcon)
        {
            var iconRt = typeIcon.rectTransform;
            float iconSide = Mathf.Max(4f, 38f * scale);
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = Vector2.zero;
            iconRt.sizeDelta = new Vector2(iconSide, iconSide);

            if (titleText && typeIcon.enabled)
            {
                float iconGap = Mathf.Max(2f, mainHeaderIconSpacing * scale * 0.5f);
                titleText.rectTransform.offsetMin = new Vector2(iconSide + iconGap, 0f);
            }
        }
    }

    private void ApplyHeaderSpacerFit(float scale)
    {
        if (!headerSpacer || headerSpacer == headerRect)
            return;

        var spacerLayout =
            headerSpacer.GetComponent<LayoutElement>()
            ?? headerSpacer.gameObject.AddComponent<LayoutElement>();
        float gap = FitHeaderGap(scale);
        spacerLayout.minHeight = gap;
        spacerLayout.preferredHeight = gap;
        spacerLayout.flexibleHeight = 0f;
    }

    private void ApplyGridFit(float cell, float gap, int columns)
    {
        if (!gridRoot)
            return;

        gridRoot.anchorMin = new Vector2(0f, 1f);
        gridRoot.anchorMax = new Vector2(1f, 1f);
        gridRoot.pivot = new Vector2(0.5f, 1f);
        gridRoot.anchoredPosition = new Vector2(0f, gridRoot.anchoredPosition.y);
        gridRoot.sizeDelta = new Vector2(0f, gridRoot.sizeDelta.y);

        var grid = gridRoot.GetComponent<GridLayoutGroup>();
        if (grid)
        {
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, columns);
            grid.cellSize = new Vector2(cell, cell);
            grid.spacing = new Vector2(gap, gap);
            grid.padding = new RectOffset(0, 0, 0, 0);
        }

        var layout = gridRoot.GetComponent<LayoutElement>();
        if (layout)
        {
            float gridHeight = HasActiveGrid()
                ? FitGridHeight(cell, gap, Mathf.Max(1, columns))
                : 0f;
            layout.minHeight = gridHeight;
            layout.preferredHeight = gridHeight;
            layout.flexibleHeight = 0f;
        }
    }

    public void RefreshPreferredHeight()
    {
        RefreshGridPreferredHeight();

        float total = 0f;
        int activeParts = 0;

        AddLayoutHeight(headerRect, HeaderHeight, ref total, ref activeParts);
        if (headerSpacer && headerSpacer != headerRect)
            AddLayoutHeight(headerSpacer, normalGap, ref total, ref activeParts);
        AddLayoutHeight(gridRoot, 100f, ref total, ref activeParts);

        var vlg = GetComponent<VerticalLayoutGroup>();
        if (vlg && activeParts > 1)
            total += vlg.spacing * (activeParts - 1);

        var groupLayout = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        groupLayout.minHeight = total;
        groupLayout.preferredHeight = total;
        groupLayout.flexibleHeight = 0f;

        if (transform is RectTransform rt)
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, total);
    }

    private void RefreshGridPreferredHeight()
    {
        if (!gridRoot || !gridRoot.gameObject.activeSelf)
            return;

        var grid = gridRoot.GetComponent<GridLayoutGroup>();
        if (!grid)
            return;

        var layout = gridRoot.GetComponent<LayoutElement>()
            ?? gridRoot.gameObject.AddComponent<LayoutElement>();
        int cols = Mathf.Max(1, grid.constraintCount);
        int count = ActiveGridChildCount();
        int rows = Mathf.CeilToInt((float)count / cols);
        float height =
            grid.padding.top
            + grid.padding.bottom
            + rows * grid.cellSize.y
            + Mathf.Max(0, rows - 1) * grid.spacing.y;

        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleHeight = 0f;
        gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    private int ActiveGridChildCount()
    {
        if (!gridRoot)
            return 0;

        int count = 0;
        for (int i = 0; i < gridRoot.childCount; i++)
        {
            var child = gridRoot.GetChild(i);
            if (child && child.gameObject.activeSelf)
                count++;
        }

        return count;
    }

    private static void AddLayoutHeight(
        RectTransform rt,
        float fallback,
        ref float total,
        ref int activeParts
    )
    {
        if (!rt || !rt.gameObject.activeSelf)
            return;

        float h = fallback;
        var le = rt.GetComponent<LayoutElement>();
        if (le && le.preferredHeight >= 0f)
            h = le.preferredHeight;
        else if (rt.rect.height > 0f)
            h = rt.rect.height;

        total += Mathf.Max(0f, h);
        activeParts++;
    }
}
