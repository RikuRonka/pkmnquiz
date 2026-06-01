using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(GridLayoutGroup))]
[RequireComponent(typeof(LayoutElement))]
public class GridAutoFit : MonoBehaviour
{
    [Header("Wiring")]
    public RectTransform Viewport;
    public RectTransform WidthSource;
    public RectTransform Header;

    [Header("Layout")]
    public float OuterMarginX = 16f;
    public float OuterMarginY = 16f;
    public float Spacing = 8f;

    [Range(32, 256)]
    public float MaxCell = 140f;

    [Header("Data")]
    public int ItemCount = 0;

    GridLayoutGroup grid;
    LayoutElement layoutElem;

    [Range(0f, 0.5f)]
    public float SpacingRatio = 0.15f;
    public int MinCols = 12;
    public int MaxCols = 60;

    void Awake()
    {
        grid = GetComponent<GridLayoutGroup>();
        layoutElem = GetComponent<LayoutElement>();
    }

    public void Recalculate()
    {
        if (!Viewport || ItemCount <= 0)
            return;

        if (!grid)
            grid = GetComponent<GridLayoutGroup>();
        if (!layoutElem)
            layoutElem = GetComponent<LayoutElement>();

        var vp = Viewport.rect;
        var pad = grid.padding;

        float sourceWidth = ResolveSourceWidth(vp.width);

        float availW = Mathf.Max(1f, sourceWidth - 2f * OuterMarginX - (pad.left + pad.right));

        int colsByMaxCell = Mathf.FloorToInt(availW / MaxCell);
        int bestCols = Mathf.Clamp(colsByMaxCell, MinCols, MaxCols);
        if (ItemCount < bestCols)
            bestCols = Mathf.Max(MinCols, ItemCount);
        if (bestCols <= 0)
            bestCols = Mathf.Clamp(MinCols, 1, MaxCols);

        // --- spacing proportional to cell size ---
        float k = Mathf.Max(0f, SpacingRatio); // gap / cell

        // availW = N*cell + (N-1)*gap  and gap = k*cell
        // => availW = cell * (N + (N-1)*k)
        float denom = bestCols + (bestCols - 1) * k;
        float cell = availW / Mathf.Max(1f, denom);
        cell = Mathf.Min(cell, MaxCell);

        float gap = cell * k;

        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = bestCols;
        grid.cellSize = new Vector2(cell, cell);
        grid.spacing = new Vector2(gap, gap);

        int rows = Mathf.CeilToInt((float)ItemCount / Mathf.Max(1, bestCols));
        float gridHeight = rows * cell + Mathf.Max(0, rows - 1) * gap;

        layoutElem.preferredHeight = gridHeight;

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }

    private float ResolveSourceWidth(float fallbackWidth)
    {
        if (!WidthSource)
            return fallbackWidth;

        if (WidthSource.rect.width > 1f)
            return WidthSource.rect.width;

        if (WidthSource.parent is RectTransform parent)
        {
            float estimated = EstimateLayoutChildWidth(parent);
            if (estimated > 1f)
                return estimated;
        }

        return fallbackWidth;
    }

    private static float EstimateLayoutChildWidth(RectTransform parent)
    {
        if (!parent)
            return 0f;

        float parentWidth = ResolveReadyWidth(parent);
        if (parentWidth <= 1f)
            return 0f;

        var layout = parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (!layout)
            return parentWidth;

        float available = parentWidth - layout.padding.left - layout.padding.right;
        if (layout is HorizontalLayoutGroup)
        {
            int activeChildren = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).gameObject.activeSelf)
                    activeChildren++;
            }

            activeChildren = Mathf.Max(1, activeChildren);
            available -= layout.spacing * Mathf.Max(0, activeChildren - 1);
            available /= activeChildren;
        }

        return Mathf.Max(0f, available);
    }

    private static float ResolveReadyWidth(RectTransform rt)
    {
        while (rt)
        {
            if (rt.rect.width > 1f)
                return rt.rect.width;

            rt = rt.parent as RectTransform;
        }

        return 0f;
    }
}
