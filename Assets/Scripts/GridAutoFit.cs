using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(GridLayoutGroup))]
[RequireComponent(typeof(LayoutElement))]
public class GridAutoFit : MonoBehaviour
{
    [Header("Wiring")]
    public RectTransform Viewport;
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

        float availW = Mathf.Max(1f, vp.width - 2f * OuterMarginX - (pad.left + pad.right));

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
}
