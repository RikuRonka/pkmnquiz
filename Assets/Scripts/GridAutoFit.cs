using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways, RequireComponent(typeof(GridLayoutGroup))]
public class GridAutoFit : MonoBehaviour
{
    public RectTransform viewport;          // assign ScrollView.viewport
    public GridLayoutGroup grid;            // the grid holding cards
    public int itemCount;                   // how many cards are in this grid
    private RectTransform _rt;

    [Header("Limits")]
    public int minColumns = 3;
    public int maxColumns = 30;

    [Header("Extra vertical margin (pixels) to reserve")]
    public float verticalReserve = 0f;      // e.g. header height above the grid if needed


    void Reset()
    {
        grid = GetComponent<GridLayoutGroup>();
        _rt = GetComponent<RectTransform>();
    }

    void OnEnable()
    {
        _rt = GetComponent<RectTransform>();
        Fit();
    }

    void OnRectTransformDimensionsChange()
    {
        // re-fit when the viewport or canvas size changes
        Fit();
    }

    void Awake()
    {
        grid = GetComponent<GridLayoutGroup>();
        _rt = GetComponent<RectTransform>();

        // Auto-wire if missing (works in prefab instances)
        if (!viewport)
        {
            var sr = GetComponentInParent<ScrollRect>(true);
            if (sr) viewport = sr.viewport ? sr.viewport : sr.GetComponent<RectTransform>();
        }
    }

    public void SetItemCount(int count)
    {
        itemCount = Mathf.Max(0, count);
        Fit();
    }

    public void Fit()
    {
        if (!grid || itemCount <= 0) return;

        var view = viewport ? viewport.rect : _rt.rect;

        var pad = grid.padding;
        float availW = view.width - pad.left - pad.right;
        float availH = view.height - pad.top - pad.bottom - verticalReserve;

        if (availW <= 0 || availH <= 0) return;

        int bestCols = minColumns;
        float bestCell = 0f;

        // Search for the columns that give the biggest square cell that fits both axes.
        for (int cols = minColumns; cols <= maxColumns; cols++)
        {
            float cellW = (availW - grid.spacing.x * (cols - 1)) / cols;
            if (cellW <= 0) continue;

            int rows = Mathf.CeilToInt((float)itemCount / cols);
            float cellH = (availH - grid.spacing.y * (rows - 1)) / Mathf.Max(1, rows);
            float cell = Mathf.Min(cellW, cellH);

            if (cell > bestCell)
            {
                bestCell = cell;
                bestCols = cols;
            }
        }

        bestCell = Mathf.Max(1f, bestCell);

        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = bestCols;
        grid.cellSize = new Vector2(bestCell, bestCell);

        LayoutRebuilder.ForceRebuildLayoutImmediate(grid.GetComponent<RectTransform>());
    }
}
