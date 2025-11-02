// TooltipManager.cs
using UnityEngine;
using UnityEngine.UI;

public class TooltipManager : MonoBehaviour
{
    public static TooltipManager Instance { get; private set; }

    [Header("Wiring")]
    public Canvas uiCanvas;
    public RectTransform tooltipLayer; // full-screen RT under the canvas
    public PokemonTooltip tooltipPrefab;

    [Header("Behavior")]
    public Vector2 screenOffset = new(16f, 16f); // <- Y is POSITIVE to go UP with top-left pivot
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
        _tipRT.pivot = new Vector2(0f, 1f); // top-left
    }

    RectTransform EnsureLayer()
    {
        var go = new GameObject("TooltipLayer", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        go.transform.SetParent(uiCanvas.transform, false);

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        // IMPORTANT: top-left origin for the layer
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
        if (!_tipRT || !uiCanvas)
            return;

        // The rect we’re positioning inside
        var parentRT = tooltipLayer ? tooltipLayer : (RectTransform)uiCanvas.transform;

        // Pick the correct camera for the conversion
        var cam =
            uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (eventCam ? eventCam : uiCanvas.worldCamera);

        // Screen -> local in the SAME rect we will clamp to
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRT,
            screenPos,
            cam,
            out var local
        );

        // Prefer above/right of cursor (top-left pivot -> positive Y goes up)
        local += screenOffset; // e.g. new Vector2(16f, 16f)

        // Clamp within parent rect for top-left pivot (0,1)
        var r = parentRT.rect;
        var size = _tip.PreferredSize;

        float minX = r.xMin;
        float maxX = r.xMax - size.x;
        float minY = r.yMin + size.y; // top-left pivot: keep bottom inside
        float maxY = r.yMax;

        local.x = Mathf.Clamp(local.x, minX, maxX);
        local.y = Mathf.Clamp(local.y, minY, maxY);

        _tipRT.anchoredPosition = local;
    }
}
