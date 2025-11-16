using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SectionGroup : MonoBehaviour
{
    [Header("Refs (assign in prefab OR created at runtime)")]
    public RectTransform headerRect;
    public RectTransform gridRoot;

    [SerializeField]
    Image typeIcon; // small square image prefab

    [SerializeField]
    TMP_Text titleText;

    [SerializeField]
    RectTransform headerSpacer; // <- assign the spacer

    [SerializeField]
    float normalGap = 16f;

    [SerializeField]
    Vector2 mainIconSize = new(48, 48);

    [SerializeField]
    float mainOnlyGap = 64f;
    public int CardCount { get; private set; }

    [Header("Scaling")]
    [SerializeField]
    float fontSizeLarge = 40f; // big cards (few cols)

    [SerializeField]
    float fontSizeSmall = 22f; // tiny cards (many cols)

    public void SetCardCount(int n)
    {
        CardCount = Mathf.Max(0, n);
    }

    public void UpdateHeaderForCols(int cols, int minCols, int maxCols)
    {
        if (!titleText)
            return;

        float t = Mathf.InverseLerp(minCols, maxCols, cols);

        float size = Mathf.Lerp(fontSizeLarge, fontSizeSmall, t);
        titleText.fontSize = size;
    }

    public void EnsureLayout()
    {
        var vlg =
            GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 16;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = GetComponent<ContentSizeFitter>() ?? gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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
            titleText.color = Color.white;
            titleText.text = "Header";
        }

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

        var grid =
            gridRoot.GetComponent<GridLayoutGroup>()
            ?? gridRoot.gameObject.AddComponent<GridLayoutGroup>();
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.spacing = new Vector2(12, 12);
        grid.padding = new RectOffset(0, 0, 0, 0);

        var le =
            gridRoot.GetComponent<LayoutElement>()
            ?? gridRoot.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 0;
        le.preferredHeight = 100;
    }

    public void SetTitle(string text, bool isMain, Sprite icon = null)
    {
        if (titleText)
        {
            titleText.enableAutoSizing = false;
            if (isMain)
            {
                titleText.fontSize = 55;
            }

            titleText.text = text;
        }

        if (!typeIcon)
            return;

        if (icon)
        {
            typeIcon.sprite = icon;
            typeIcon.preserveAspect = true;
            var rt = (RectTransform)typeIcon.transform;
            rt.sizeDelta = mainIconSize;
            typeIcon.enabled = true; // show
        }
        else
        {
            typeIcon.enabled = false; // hide when not a type quiz
        }
    }

    public void SetHeaderGap(bool mainOnlyScreen)
    {
        if (!headerSpacer)
            return;
        var le =
            headerSpacer.GetComponent<LayoutElement>()
            ?? headerSpacer.gameObject.AddComponent<LayoutElement>();
        le.minHeight = mainOnlyScreen ? mainOnlyGap : normalGap;
    }
}
