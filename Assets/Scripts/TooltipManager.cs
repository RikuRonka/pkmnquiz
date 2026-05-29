using System.Collections.Generic;
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
    bool _pinned;

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (!uiCanvas)
            uiCanvas = GetComponentInParent<Canvas>();

        if (!tooltipLayer)
            tooltipLayer = uiCanvas.transform.Find("TooltipLayer") as RectTransform;

        _tip = Instantiate(tooltipPrefab, tooltipLayer);
        _tip.gameObject.SetActive(true);
        _tip.SetVisible(false, immediate: true);

        _tipRT = (RectTransform)_tip.transform;
        _tipRT.pivot = new Vector2(0.5f, 0.5f); // top-left
    }

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
        if (!string.IsNullOrWhiteSpace(description))
            _tip.SetNotes(title, description);
        else
            _tip.SetContent(title, t1, t2);

        LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRT);

        PositionFollow(screenPos);
        _tip.SetVisible(true, fadeTime <= 0f, fadeTime);
    }

    public void ShowFollowPlayerList(
        string title,
        IReadOnlyList<string> playerNames,
        Vector2 screenPos,
        Camera eventCam
    )
    {
        _pinned = false;
        _tip.SetPlayerList(title, playerNames);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRT);

        PositionFollow(screenPos);
        _tip.SetVisible(true, fadeTime <= 0f, fadeTime);
    }

    public void MoveFollow(Vector2 screenPos, Camera eventCam)
    {
        if (_pinned || !_tip || !_tip.IsVisible)
            return;

        PositionFollow(screenPos);
    }

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

        _tip.SetNotes($"Update {version}", notes);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRT);
        var size = _tip.PreferredSize;

        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        Vector2 bl = tooltipLayer.InverseTransformPoint(corners[0]);
        Vector2 br = tooltipLayer.InverseTransformPoint(corners[3]);

        float x = centerToAnchor ? (bl.x + br.x - size.x) * 0.5f : bl.x;
        float y = bl.y - gapY;

        const float EDGE = 150f;
        var r = tooltipLayer.rect;

        if (x + size.x > r.xMax - EDGE)
            x = r.xMax - size.x - EDGE;
        if (y - size.y < r.yMin + EDGE)
            y = bl.y + size.y + gapY;

        x = Mathf.Clamp(x, r.xMin + EDGE, r.xMax - size.x - EDGE);
        y = Mathf.Clamp(y, r.yMin + size.y + EDGE, r.yMax - EDGE);

        float sf = uiCanvas ? uiCanvas.scaleFactor : 1f;
        _tipRT.anchoredPosition = new Vector2(
            Mathf.Round(x * sf) / sf - 70f,
            Mathf.Round(y * sf) / sf
        );

        _tip.SetVisible(true, fadeTime <= 0f, fadeTime);
        _tipRT.SetAsLastSibling();
    }

    void PositionFollow(Vector2 screenPos)
    {
        if (!uiCanvas || !_tipRT || !_tip)
            return;

        const float EDGE = 8f; // margin to screen edge
        Vector2 offset = new Vector2(screenOffset.x, -screenOffset.y);

        Vector2 pos = screenPos + offset;
        _tipRT.position = pos;

        Canvas.ForceUpdateCanvases();

        Rect screenRect = uiCanvas.pixelRect;
        Vector3[] corners = new Vector3[4];
        _tipRT.GetWorldCorners(corners);

        Camera cam = null;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < 4; i++)
        {
            Vector3 sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            if (sp.x < minX)
                minX = sp.x;
            if (sp.x > maxX)
                maxX = sp.x;
            if (sp.y < minY)
                minY = sp.y;
            if (sp.y > maxY)
                maxY = sp.y;
        }

        float dx = 0f;
        float dy = 0f;

        if (minX < screenRect.xMin + EDGE)
            dx += screenRect.xMin + EDGE - minX;

        if (maxX > screenRect.xMax - EDGE)
            dx += screenRect.xMax - EDGE - maxX;

        if (minY < screenRect.yMin + EDGE)
            dy += screenRect.yMin + EDGE - minY;

        if (maxY > screenRect.yMax - EDGE)
            dy += screenRect.yMax - EDGE - maxY;

        pos += new Vector2(dx, dy);
        _tipRT.position = pos;
        _tipRT.SetAsLastSibling();
    }
}
