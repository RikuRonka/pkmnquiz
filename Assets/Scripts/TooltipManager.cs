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
        _tipRT.anchorMin = _tipRT.anchorMax = new Vector2(0.42f, 0.67f);
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

        Camera cam =
            uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (eventCam ? eventCam : uiCanvas.worldCamera);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            tooltipLayer,
            screenPos,
            cam,
            out var local
        );
        Vector2 margin = new(Mathf.Abs(screenOffset.x), Mathf.Abs(screenOffset.y));
        Vector2 pos = local + new Vector2(margin.x, -margin.y);

        _tipRT.anchoredPosition = pos; // set first
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRT); // ensure size/layout is up-to-date

        const float EDGE = 8f;

        var parentRT = tooltipLayer;
        Vector3[] tip = new Vector3[4];
        Vector3[] par = new Vector3[4];
        _tipRT.GetWorldCorners(tip);
        parentRT.GetWorldCorners(par);

        float dxLeft = Mathf.Max(0f, par[0].x + EDGE - tip[0].x); // need to move right
        float dxRight = Mathf.Min(0f, par[2].x - EDGE - tip[2].x); // need to move left (negative)
        float dyBottom = Mathf.Max(0f, par[0].y + EDGE - tip[0].y); // need to move up
        float dyTop = Mathf.Min(0f, par[2].y - EDGE - tip[2].y); // need to move down (negative)

        const float EPS = 1e-4f;

        float shiftX = (dxLeft > EPS) ? dxLeft : (dxRight < -EPS ? dxRight : 0f);

        float shiftY = (dyBottom > EPS) ? dyBottom : (dyTop < -EPS ? dyTop : 0f);

        Vector3 worldDelta = new Vector3(shiftX, shiftY, 0f);

        if (worldDelta.sqrMagnitude > 0f)
        {
            Vector2 localDelta = (Vector2)tooltipLayer.InverseTransformVector(worldDelta);
            _tipRT.anchoredPosition += localDelta;
        }

        _tipRT.SetAsLastSibling(); // make sure it renders on top
    }
}
