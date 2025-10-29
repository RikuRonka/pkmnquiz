using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SectionGroup : MonoBehaviour
{
    [Header("Refs (assign in prefab OR created at runtime)")]
    public RectTransform headerRect;     // the header bar rect
    public TMP_Text headerLabel;         // the label
    public RectTransform gridRoot;       // the grid container (children = cards)

    public int CardCount => gridRoot ? gridRoot.childCount : 0;

    /// Call once right after Instantiate()
    public void EnsureLayout()
    {
        // Root: VerticalLayoutGroup + ContentSizeFitter
        var vlg = GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 16;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = GetComponent<ContentSizeFitter>() ?? gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header
        if (!headerRect)
        {
            var hdrGO = new GameObject("Header", typeof(RectTransform), typeof(Image));
            hdrGO.transform.SetParent(transform, false);
            headerRect = hdrGO.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1);
            headerRect.sizeDelta = new Vector2(0, 44);   // height ~44
            var img = hdrGO.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0.3f);
        }

        if (!headerLabel)
        {
            var lblGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGO.transform.SetParent(headerRect, false);
            var lrt = (RectTransform)lblGO.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0, 0.5f);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = new Vector2(16, 0);
            headerLabel = lblGO.GetComponent<TextMeshProUGUI>();
            headerLabel.alignment = TextAlignmentOptions.MidlineLeft;
            headerLabel.fontSize = 36;
            headerLabel.enableAutoSizing = true;
            headerLabel.fontSizeMin = 18;
            headerLabel.fontSizeMax = 42;
            headerLabel.color = Color.white;
            headerLabel.text = "Header";
        }

        // GridRoot
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

        // GridRoot needs GridLayoutGroup + LayoutElement; do NOT put ContentSizeFitter here
        var grid = gridRoot.GetComponent<GridLayoutGroup>() ?? gridRoot.gameObject.AddComponent<GridLayoutGroup>();
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; // will be set later
        grid.spacing = new Vector2(12, 12);
        grid.padding = new RectOffset(0, 0, 0, 0);

        var le = gridRoot.GetComponent<LayoutElement>() ?? gridRoot.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 0;
        le.preferredHeight = 100;
    }

    public void SetTitle(string t)
    {
        if (headerLabel) headerLabel.text = t;
    }
}
