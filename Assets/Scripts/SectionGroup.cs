// inside your SectionGroup class
using UnityEngine;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class SectionGroup : MonoBehaviour
{
    [Header("Auto-wired")]
    public RectTransform headerRect;      // Header root
    public TMP_Text headerLabel;          // The TMP text
    public RectTransform gridRoot;        // Where cards go

    [SerializeField] float headerToGridGap = 8f;
    /// <summary>Total cards spawned into this section’s grid.</summary>
    public int CardCount => gridRoot ? gridRoot.childCount : 0;

    /// <summary>Header height the fitter should reserve (0 if header is hidden/missing).</summary>
    public float HeaderHeight
    {
        get
        {
            if (!headerRect || !headerRect.gameObject.activeInHierarchy) return 0f;
            // Prefer layout’s idea of height; fallback to current rect height.
            float h = LayoutUtility.GetPreferredHeight(headerRect);
            if (h <= 0f) h = headerRect.rect.height;
            return Mathf.Max(0f, h);
        }
    }


    void Awake()
    {
        // Make sure header has some minimum height
        if (headerRect)
        {
            var le = headerRect.GetComponent<LayoutElement>();
            if (!le) le = headerRect.gameObject.AddComponent<LayoutElement>();
            le.minHeight = Mathf.Max(le.minHeight, 36f);
            le.preferredHeight = Mathf.Max(le.preferredHeight, 36f);
        }

        // Ensure a GridLayoutGroup exists and has a little top padding
        if (gridRoot)
        {
            var grid = gridRoot.GetComponent<GridLayoutGroup>();
            if (!grid) grid = gridRoot.gameObject.AddComponent<GridLayoutGroup>();
            var pad = grid.padding;
            pad.top = Mathf.Max(pad.top, (int)headerToGridGap);
            grid.padding = pad;
        }
        EnsureLayout(null);
    }

    // Call in Awake and OnValidate so it's correct in Play Mode and in Editor
    void OnValidate() => EnsureLayout(null);

    /// <summary>Ensure required children/components exist and are configured.</summary>
    public void EnsureLayout(RectTransform viewport)
    {
        // Ensure children exist
        if (!headerRect) headerRect = transform.EnsureChildRect("SectionHeader");
        if (!gridRoot) gridRoot = transform.EnsureChildRect("GridRoot");

        // Ensure a TMP text exists under the header
        if (!headerLabel)
        {
            var txtT = headerRect.Find("Text (TMP)") as RectTransform;
            if (!txtT)
            {
                txtT = headerRect.EnsureChildRect("Text (TMP)");
                txtT.anchorMin = new Vector2(0, 0.5f);
                txtT.anchorMax = new Vector2(0, 0.5f);
                txtT.pivot = new Vector2(0, 0.5f);
                txtT.anchoredPosition = new Vector2(0, 0);
            }
            headerLabel = txtT.GetOrAdd<TMP_Text>();
            headerLabel.text = string.IsNullOrEmpty(headerLabel.text) ? "" : headerLabel.text;
        }

        // Ensure this group stacks vertically and sizes to content
        var vlg = gameObject.GetOrAdd<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = 8f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false; // gridRoot will have preferredHeight
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = gameObject.GetOrAdd<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header layout (optional but tidy)
        var headerLE = headerRect.GetOrAdd<LayoutElement>();
        headerLE.minHeight = 32f; // whatever you want; your fitter uses HeaderHeight

        // Ensure the grid has required components
        var grid = gridRoot.GetOrAdd<GridLayoutGroup>();
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;               // temporary; fitter will overwrite
        grid.cellSize = new Vector2(128, 128);
        grid.spacing = new Vector2(12, 12);
        grid.padding = new RectOffset(0, 0, 0, 0);

        var gridLE = gridRoot.GetOrAdd<LayoutElement>();
        gridLE.preferredHeight = 200;           // temporary; fitter will overwrite
    }

    public void SetTitle(string title)
    {
        bool show = !string.IsNullOrWhiteSpace(title);
        if (headerRect) headerRect.gameObject.SetActive(show);
        if (show && headerLabel) headerLabel.text = title;
    }
}
