using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(GridLayoutGroup))]
[RequireComponent(typeof(LayoutElement))]
public class GridPreferredHeight : MonoBehaviour
{
    GridLayoutGroup grid;
    LayoutElement le;
    RectTransform rt;

    void Awake()
    {
        grid = GetComponent<GridLayoutGroup>();
        le = GetComponent<LayoutElement>();
        rt = GetComponent<RectTransform>();
    }

    void OnTransformChildrenChanged()
    {
        Recalc();
    }

    void OnRectTransformDimensionsChange()
    {
        Recalc();
    }

    public void Recalc()
    {
        if (!grid || !le)
            return;

        int cols = Mathf.Max(1, grid.constraintCount);
        int count = 0;
        for (int i = 0; i < transform.childCount; i++)
            if (transform.GetChild(i).gameObject.activeSelf)
                count++;

        int rows = Mathf.CeilToInt((float)count / cols);

        float h =
            grid.padding.top
            + grid.padding.bottom
            + rows * grid.cellSize.y
            + Mathf.Max(0, rows - 1) * grid.spacing.y;

        le.preferredHeight = h;
    }
}
