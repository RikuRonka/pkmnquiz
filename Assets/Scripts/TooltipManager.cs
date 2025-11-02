using UnityEngine;
using UnityEngine.UI;

public class TooltipManager : MonoBehaviour
{
    public static TooltipManager Instance { get; private set; }

    [Header("Wiring")]
    public Canvas uiCanvas;
    public RectTransform tooltipLayer;
    public PokemonTooltip tooltipPrefab;

    [Header("Behavior")]
    public Vector2 screenOffset = new(16f, 16f);
    public float fadeTime = 0.12f;

    PokemonTooltip _tip;
    RectTransform _tipRT;

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (!uiCanvas)
            uiCanvas = FindFirstObjectByType<Canvas>();
        if (!tooltipLayer)
            tooltipLayer = EnsureLayer();

        _tip = Instantiate(tooltipPrefab, tooltipLayer);
        _tip.gameObject.SetActive(true);
        _tip.SetVisible(false, immediate: true);

        _tipRT = (RectTransform)_tip.transform;
        _tipRT.anchorMin = _tipRT.anchorMax = new Vector2(0.4f, 0.75f);
        _tipRT.pivot = new Vector2(0f, 1f);
    }

    RectTransform EnsureLayer()
    {
        var go = new GameObject("TooltipLayer", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        go.transform.SetParent(uiCanvas.transform, false);

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        rt.pivot = new Vector2(0f, 1f);

        go.transform.SetAsLastSibling();

        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0);
        img.raycastTarget = false;
        return rt;
    }

    public void Show(string name, string t1, string t2, Vector2 screenPos, Camera eventCam)
    {
        if (!_tip)
            return;
        _tip.SetContent(name, t1, t2);
        Position(screenPos, eventCam);
        _tip.SetVisible(true, fadeTime <= 0, fadeTime);
    }

    public void Move(Vector2 screenPos, Camera eventCam)
    {
        if (!_tip || !_tip.IsVisible)
            return;
        Position(screenPos, eventCam);
    }

    public void Hide()
    {
        if (!_tip)
            return;
        _tip.SetVisible(false, fadeTime <= 0, fadeTime);
    }

    void Position(Vector2 screenPos, Camera eventCam)
    {
        if (!uiCanvas || !tooltipLayer || !_tipRT)
            return;

        // choose camera
        Camera cam =
            (uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                ? null
                : (eventCam ? eventCam : uiCanvas.worldCamera);

        // screen -> parent-local
        RectTransform parentRT = tooltipLayer;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRT,
            screenPos,
            cam,
            out var local
        );

        Vector2 size = _tip.PreferredSize; // tooltip size
        Rect bounds = parentRT.rect; // parent rect
        Vector2 margin = new(Mathf.Abs(screenOffset.x), Mathf.Abs(screenOffset.y));

        // Start above-right of the cursor (pivot is top-left)
        Vector2 pos = local + new Vector2(margin.x, -margin.y);

        // ---- horizontal flip (if would overflow right, place to the left of cursor)
        if (pos.x + size.x > bounds.xMax)
            pos.x = local.x - size.x - margin.x;

        // keep inside left/right after deciding side
        pos.x = Mathf.Clamp(pos.x, bounds.xMin, bounds.xMax - size.x);

        // ---- vertical flip
        // prefer above; if bottom would go below, place below the cursor
        if (pos.y - size.y < bounds.yMin)
            pos.y = local.y + size.y + margin.y;

        // keep inside top/bottom after deciding side
        pos.y = Mathf.Clamp(pos.y, bounds.yMin + size.y, bounds.yMax);

        _tipRT.anchoredPosition = pos;
        const float EDGE_PAD = 8f;
        pos.x = Mathf.Clamp(pos.x, bounds.xMin + EDGE_PAD, bounds.xMax - size.x - EDGE_PAD);
        pos.y = Mathf.Clamp(pos.y, bounds.yMin + size.y + EDGE_PAD, bounds.yMax - EDGE_PAD);
    }
}
