using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(GridLayoutGroup))]
[RequireComponent(typeof(LayoutElement))]
public class GridAutoFit : MonoBehaviour
{
    [Header("Wiring")]
    public RectTransform Viewport;     // assign from QuizManager
    public RectTransform Header;       // section header rect

    [Header("Layout")]
    public float OuterMarginX = 16f;
    public float OuterMarginY = 16f;
    public float Spacing = 8f;
    public int MinCols = 12;
    public int MaxCols = 60;
    [Range(32, 256)] public float MaxCell = 140f;

    [Header("Data")]
    public int ItemCount = 0;

    GridLayoutGroup grid;
    LayoutElement layoutElem;

    void Awake()
    {
        grid = GetComponent<GridLayoutGroup>();
        layoutElem = GetComponent<LayoutElement>();
    }

    public void Recalculate()
    {
        if (!Viewport || ItemCount <= 0) return;

        var vp = Viewport.rect;
        float headH = Header ? Header.rect.height : 0f;
        var pad = grid.padding;

        float availW = Mathf.Max(1f, vp.width - OuterMarginX * 2f - (pad.left + pad.right));
        float availH = Mathf.Max(1f, vp.height - OuterMarginY * 2f - headH - (pad.top + pad.bottom));

        int bestCols = MinCols;
        float bestCell = 0f;

        for (int cols = MinCols; cols <= MaxCols; cols++)
        {
            float cell = (availW - Spacing * (cols - 1)) / cols;
            if (cell <= 1f) continue;

            int rows = Mathf.CeilToInt((float)ItemCount / cols);
            float gridH = rows * cell + Mathf.Max(0, rows - 1) * Spacing;

            if (gridH <= availH && cell > bestCell)
            {
                bestCell = cell;
                bestCols = cols;
            }
        }

        if (bestCell <= 0f)
        {
            bestCols = MaxCols;
            bestCell = (availW - Spacing * (bestCols - 1)) / bestCols;
        }

        bestCell = Mathf.Min(bestCell, MaxCell);

        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = bestCols;
        grid.cellSize = new Vector2(bestCell, bestCell);
        grid.spacing = new Vector2(Spacing, Spacing);

        int rowsFinal = Mathf.CeilToInt((float)ItemCount / bestCols);
        float finalGrid = rowsFinal * bestCell + Mathf.Max(0, rowsFinal - 1) * Spacing;

        layoutElem.preferredHeight = finalGrid;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }
}
