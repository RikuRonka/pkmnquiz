using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TooltipManager : MonoBehaviour
{
    public static TooltipManager Instance { get; private set; }

    [Header("Wiring")]
    public Canvas uiCanvas;
    public RectTransform tooltipLayer; // full-screen stretch under the UI canvas
    public PokemonTooltip tooltipPrefab; // your tooltip prefab (with Layout Group)

    [Header("Behavior")]
    public Vector2 screenOffset = new(16f, 16f);
    public float fadeTime = 0.12f;

    PokemonTooltip _tip;
    RectTransform _tipRT;
    bool _pinned; // when true, ignore MoveFollow

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
        _tipRT.pivot = new Vector2(0f, 1f); // Top-Left
    }

    RectTransform EnsureLayer()
    {
        var go = new GameObject("TooltipLayer", typeof(RectTransform));
        go.transform.SetParent(uiCanvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0f, 1f);
        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0);
        img.raycastTarget = false;
        go.transform.SetAsLastSibling();
        return rt;
    }

    // ---------- Public API ----------

    // For Pokémon hover: follow the cursor
    public void ShowFollow(
        string title,
        string t1,
        string t2,
        string description,
        Vector2 screenPos,
        Camera eventCam
    )
    {
        _pinned = false;
        _tip.SetContent(title, t1, t2);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRT);
        PositionFollow(screenPos, eventCam);
        _tip.SetVisible(true, fadeTime <= 0f, fadeTime);
    }

    public void MoveFollow(Vector2 screenPos, Camera eventCam)
    {
        if (_pinned || !_tip || !_tip.IsVisible)
            return;
        PositionFollow(screenPos, eventCam);
    }

    // For update notes: pin bottom-right (never off-screen)
    public void ShowUpdate(string version, string notes)
    {
        _pinned = true;
        _tip.SetNotes($"Update v{version}", notes);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRT);
        PlaceAtBottomRight();
        _tip.SetVisible(true, fadeTime <= 0f, fadeTime);
    }

    public void Hide()
    {
        if (!_tip)
            return;
        _tip.SetVisible(false, fadeTime <= 0f, fadeTime);
    }

    // ---------- Position helpers ----------

    void PlaceAtBottomRight()
    {
        var r = tooltipLayer.rect;
        var size = _tip.PreferredSize;
        const float EDGE = 12f;

        float x = r.xMax - size.x - EDGE;
        float y = r.yMin + size.y + EDGE;

        float sf = uiCanvas ? uiCanvas.scaleFactor : 1f;
        _tipRT.anchoredPosition = new Vector2(Mathf.Round(x * sf) / sf, Mathf.Round(y * sf) / sf);
    }

    public void ShowUpdateUnder(
        RectTransform anchor,
        string version,
        string notes,
        float gapY = 8f,
        bool centerToAnchor = false
    )
    {
        _pinned = true;

        // Fill content (left-aligned notes inside your tooltip component)
        _tip.SetNotes($"Update {version}", notes);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRT);
        var size = _tip.PreferredSize;

        // Get the anchor’s corners in the tooltipLayer’s local space
        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        Vector2 bl = tooltipLayer.InverseTransformPoint(corners[0]); // bottom-left
        Vector2 br = tooltipLayer.InverseTransformPoint(corners[3]); // bottom-right

        // Start position: just under the anchor (our tooltip has Top-Left pivot)
        float x = centerToAnchor
            ? (bl.x + br.x - size.x) * 0.5f // centered under the button
            : bl.x; // left-aligned to the button
        float y = bl.y - gapY;

        // Clamp inside the parent rect (with a small edge padding)
        const float EDGE = 150f;
        var r = tooltipLayer.rect;

        // If it would go off the right edge, shift left.
        if (x + size.x > r.xMax - EDGE)
            x = r.xMax - size.x - EDGE;
        // If it would go below, flip above the anchor.
        if (y - size.y < r.yMin + EDGE)
            y = bl.y + size.y + gapY;

        // Final clamping
        x = Mathf.Clamp(x, r.xMin + EDGE, r.xMax - size.x - EDGE);
        y = Mathf.Clamp(y, r.yMin + size.y + EDGE, r.yMax - EDGE);

        // Pixel snap
        float sf = uiCanvas ? uiCanvas.scaleFactor : 1f;
        _tipRT.anchoredPosition = new Vector2(Mathf.Round(x * sf) / sf, Mathf.Round(y * sf) / sf);

        _tip.SetVisible(true, fadeTime <= 0f, fadeTime);
        _tipRT.SetAsLastSibling();
    }

    void PositionFollow(Vector2 screenPos, Camera eventCam)
    {
        if (!uiCanvas || !tooltipLayer || !_tipRT)
            return;

        // choose camera
        Camera cam =
            (uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                ? null
                : (eventCam ? eventCam : uiCanvas.worldCamera);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            tooltipLayer,
            screenPos,
            cam,
            out var local
        );

        LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRT);
        Vector2 size = _tip.PreferredSize;
        Rect bounds = tooltipLayer.rect;
        Vector2 pad = new(Mathf.Abs(screenOffset.x), Mathf.Abs(screenOffset.y));
        const float EDGE = 12f;

        // start near cursor (Top-Left pivot)
        Vector2 pos = local + new Vector2(pad.x + 10, -pad.y);

        // flip if overflowing
        if (pos.x + size.x > bounds.xMax - EDGE)
            pos.x = local.x - size.x - pad.x;
        if (pos.y - size.y < bounds.yMin + EDGE)
            pos.y = local.y + size.y + pad.y;

        // clamp inside
        pos.x = Mathf.Clamp(pos.x, bounds.xMin + EDGE, bounds.xMax - size.x - EDGE);
        pos.y = Mathf.Clamp(pos.y, bounds.yMin + size.y + EDGE, bounds.yMax - EDGE);

        // pixel snap
        float sf = uiCanvas ? uiCanvas.scaleFactor : 1f;
        pos = new Vector2(Mathf.Round(pos.x * sf) / sf, Mathf.Round(pos.y * sf) / sf);

        _tipRT.anchoredPosition = pos;
        _tipRT.SetAsLastSibling();
    }
}
