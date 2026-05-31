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
    private const float HeaderTextMinSize = 14f;
    private const float HeaderHeight = 52f;

    [SerializeField]
    float mainHeaderIconSpacing = 24f;

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
        titleText.enableAutoSizing = false;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Overflow;
        titleText.text = text ?? "";
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

    public void RefreshPreferredHeight()
    {
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
