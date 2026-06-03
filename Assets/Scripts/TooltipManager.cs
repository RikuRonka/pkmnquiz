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

    [Header("Debug")]
    public bool debugPlacement = false;
    public float debugLogInterval = 0.25f;

    PokemonTooltip _tip;
    RectTransform _tipRT;
    RectTransform _tipVisualRT;
    bool _pinned;
    float _nextDebugLogTime;

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

        NormalizeTooltipLayer();

        _tip = Instantiate(tooltipPrefab, tooltipLayer);
        _tip.gameObject.SetActive(true);
        _tip.SetVisible(false, immediate: true);

        _tipRT = (RectTransform)_tip.transform;
        Vector2 anchor = tooltipLayer ? tooltipLayer.pivot : new Vector2(0.5f, 0.5f);
        _tipRT.anchorMin = anchor;
        _tipRT.anchorMax = anchor;
        _tipRT.pivot = new Vector2(0.5f, 0.5f);
        _tipVisualRT = _tip.VisualRoot ? _tip.VisualRoot : FindTooltipVisualRoot(_tipRT);
    }

    public void ShowFollow(
        string title,
        string t1,
        string t2,
        string description,
        Vector2 screenPos,
        Camera eventCam,
        RectTransform bounds = null,
        Pokemon pokemon = null,
        IReadOnlyCollection<int> guessedIds = null,
        IReadOnlyCollection<int> activeQuizIds = null
    )
    {
        _pinned = false;
        if (!string.IsNullOrWhiteSpace(description))
            _tip.SetNotes(title, description);
        else
            _tip.SetContent(title, t1, t2, pokemon, guessedIds, activeQuizIds);

        ForceRebuildTooltipLayout();

        PositionFollow(screenPos, eventCam, bounds);
        _tip.SetVisible(true, fadeTime <= 0f, fadeTime);
    }

    public void ShowFollowPlayerList(
        string title,
        IReadOnlyList<string> playerNames,
        Vector2 screenPos,
        Camera eventCam,
        RectTransform bounds = null
    )
    {
        _pinned = false;
        _tip.SetPlayerList(title, playerNames);
        ForceRebuildTooltipLayout();

        PositionFollow(screenPos, eventCam, bounds);
        _tip.SetVisible(true, fadeTime <= 0f, fadeTime);
    }

    public void MoveFollow(Vector2 screenPos, Camera eventCam, RectTransform bounds = null)
    {
        if (_pinned || !_tip || !_tip.IsVisible)
            return;

        PositionFollow(screenPos, eventCam, bounds);
    }

    public void ShowUpdate(string version, string notes)
    {
        _pinned = true;
        _tip.SetNotes($"Update v{version}", notes);
        ForceRebuildTooltipLayout();
        ConstrainTooltipToLayer(12f);
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

        Vector2 topLeft = new(r.xMax - size.x - EDGE, r.yMin + size.y + EDGE);
        SetTooltipTopLeft(ClampTopLeftToLayer(topLeft, size, EDGE), size);
        ClampTooltipToScreen(EDGE, GetCanvasCamera(null), null, "bottom-right");
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
        ForceRebuildTooltipLayout();
        ConstrainTooltipToLayer(8f);
        var size = _tip.PreferredSize;

        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        Vector2 bl = tooltipLayer.InverseTransformPoint(corners[0]);
        Vector2 tl = tooltipLayer.InverseTransformPoint(corners[1]);
        Vector2 br = tooltipLayer.InverseTransformPoint(corners[3]);

        float x = centerToAnchor ? (bl.x + br.x - size.x) * 0.5f : bl.x;
        float y = bl.y - gapY;

        const float EDGE = 8f;
        var r = tooltipLayer.rect;

        if (y - size.y < r.yMin + EDGE)
            y = tl.y + size.y + gapY;

        Vector2 topLeft = ClampTopLeftToLayer(new Vector2(x, y), size, EDGE);
        SetTooltipTopLeft(topLeft, size);
        ClampTooltipToScreen(EDGE, GetCanvasCamera(null), null, "update-under");

        _tip.SetVisible(true, fadeTime <= 0f, fadeTime);
        _tipRT.SetAsLastSibling();
    }

    void PositionFollow(Vector2 screenPos, Camera eventCam, RectTransform bounds)
    {
        if (!uiCanvas || !_tipRT || !_tip)
            return;

        const float EDGE = 8f; // margin to screen edge
        Camera cam = GetCanvasCamera(eventCam);

        if (
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                tooltipLayer,
                screenPos,
                cam,
                out Vector2 localPos
            )
        )
            return;

        ConstrainTooltipToLayer(EDGE);
        _tipRT.anchoredPosition = RoundForCanvas(localPos);
        AlignFollowTooltipToCursor(screenPos, cam);
        ClampTooltipToScreen(EDGE, cam, bounds, "follow", screenPos, localPos);
        _tipRT.SetAsLastSibling();
    }

    void AlignFollowTooltipToCursor(Vector2 screenPos, Camera cam)
    {
        Canvas.ForceUpdateCanvases();

        Rect tooltipRect = GetTooltipScreenRect(cam);
        if (tooltipRect.width <= 0f || tooltipRect.height <= 0f)
            return;

        float gap = Mathf.Abs(screenOffset.y);
        Vector2 desiredBottomCenter = new Vector2(screenPos.x, screenPos.y + gap);
        Vector2 currentBottomCenter = new Vector2(tooltipRect.center.x, tooltipRect.yMin);

        if (TryGetLocalDelta(desiredBottomCenter - currentBottomCenter, cam, out Vector2 localDelta))
            _tipRT.anchoredPosition = RoundForCanvas(_tipRT.anchoredPosition + localDelta);
    }

    void NormalizeTooltipLayer()
    {
        if (!tooltipLayer)
            return;

        tooltipLayer.localRotation = Quaternion.identity;
        tooltipLayer.localScale = Vector3.one;
        tooltipLayer.anchorMin = Vector2.zero;
        tooltipLayer.anchorMax = Vector2.one;
        tooltipLayer.pivot = new Vector2(0.5f, 0.5f);
        tooltipLayer.offsetMin = Vector2.zero;
        tooltipLayer.offsetMax = Vector2.zero;
        tooltipLayer.anchoredPosition = Vector2.zero;
        tooltipLayer.sizeDelta = Vector2.zero;
    }

    void ConstrainTooltipToLayer(float edge)
    {
        if (!_tip || !tooltipLayer)
            return;

        float maxWidth = Mathf.Max(1f, tooltipLayer.rect.width - edge * 2f);
        if (_tip.ConstrainWidth(maxWidth))
            ForceRebuildTooltipLayout();
    }

    void ForceRebuildTooltipLayout()
    {
        if (_tipRT)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRT);
        if (_tipVisualRT && _tipVisualRT != _tipRT)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tipVisualRT);
    }

    Vector2 GetTooltipSize()
    {
        Vector2 size = _tipVisualRT ? _tipVisualRT.rect.size : _tipRT.rect.size;
        if (size.x <= 0f || size.y <= 0f)
            size =
                GetTooltipScreenRect(GetCanvasCamera(null)).size
                / Mathf.Max(0.001f, uiCanvas ? uiCanvas.scaleFactor : 1f);
        if (size.x <= 0f || size.y <= 0f)
            size = _tip.PreferredSize;
        return size;
    }

    Vector2 ClampTopLeftToLayer(Vector2 topLeft, Vector2 size, float edge)
    {
        var r = tooltipLayer.rect;

        float minX = r.xMin + edge;
        float maxX = r.xMax - size.x - edge;
        float minY = r.yMin + size.y + edge;
        float maxY = r.yMax - edge;

        return new Vector2(
            ClampAxis(topLeft.x, minX, maxX),
            ClampAxis(topLeft.y, minY, maxY)
        );
    }

    void SetTooltipTopLeft(Vector2 topLeft, Vector2 size)
    {
        var pivot = _tipRT.pivot;
        Vector2 pivotPos = new(
            topLeft.x + size.x * pivot.x,
            topLeft.y - size.y * (1f - pivot.y)
        );
        _tipRT.anchoredPosition = RoundForCanvas(pivotPos);
    }

    float ClampAxis(float value, float min, float max)
    {
        if (max < min)
            return (min + max) * 0.5f;
        return Mathf.Clamp(value, min, max);
    }

    void ClampTooltipToScreen(
        float edge,
        Camera cam,
        RectTransform bounds,
        string context,
        Vector2? inputScreenPos = null,
        Vector2? inputLocalPos = null
    )
    {
        Canvas.ForceUpdateCanvases();

        Rect screenRect = GetVisibleScreenRect(bounds, cam);
        screenRect.xMin += edge;
        screenRect.xMax -= edge;
        screenRect.yMin += edge;
        screenRect.yMax -= edge;

        Rect beforeRect = GetTooltipScreenRect(cam);

        Vector2 screenDelta = Vector2.zero;
        if (beforeRect.xMin < screenRect.xMin)
            screenDelta.x += screenRect.xMin - beforeRect.xMin;
        if (beforeRect.xMax > screenRect.xMax)
            screenDelta.x += screenRect.xMax - beforeRect.xMax;
        if (beforeRect.yMin < screenRect.yMin)
            screenDelta.y += screenRect.yMin - beforeRect.yMin;
        if (beforeRect.yMax > screenRect.yMax)
            screenDelta.y += screenRect.yMax - beforeRect.yMax;

        if (screenDelta == Vector2.zero)
        {
            LogPlacement(
                context,
                bounds,
                screenRect,
                beforeRect,
                beforeRect,
                screenDelta,
                Vector2.zero,
                inputScreenPos,
                inputLocalPos,
                forced: false
            );
            return;
        }

        Vector2 localDelta = Vector2.zero;
        bool applied = false;

        if (TryGetLocalDelta(screenDelta, cam, out localDelta))
        {
            _tipRT.anchoredPosition = RoundForCanvas(
                _tipRT.anchoredPosition + localDelta
            );
            applied = true;
        }

        Rect afterRect = GetTooltipScreenRect(cam);
        LogPlacement(
            context,
            bounds,
            screenRect,
            beforeRect,
            afterRect,
            screenDelta,
            localDelta,
            inputScreenPos,
            inputLocalPos,
            forced: true
        );

        if (!applied)
            Debug.LogWarning(
                $"[Tooltip] Clamp wanted delta={FormatVec(screenDelta)} but local conversion failed. "
                    + $"context={context} cam={CameraName(cam)} bounds={RectName(bounds)}"
            );
    }

    bool TryGetLocalDelta(Vector2 screenDelta, Camera cam, out Vector2 localDelta)
    {
        localDelta = Vector2.zero;

        if (
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                tooltipLayer,
                Vector2.zero,
                cam,
                out Vector2 localZero
            )
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                tooltipLayer,
                screenDelta,
                cam,
                out Vector2 localDeltaPoint
            )
        )
            return false;

        localDelta = localDeltaPoint - localZero;
        return true;
    }

    Rect GetVisibleScreenRect(RectTransform bounds, Camera cam)
    {
        Rect rect = uiCanvas ? uiCanvas.pixelRect : new Rect(0f, 0f, Screen.width, Screen.height);
        if (rect.width <= 0f || rect.height <= 0f)
            rect = new Rect(0f, 0f, Screen.width, Screen.height);

        Rect safe = Screen.safeArea;
        if (safe.width <= 0f || safe.height <= 0f)
            return rect;

        float xMin = Mathf.Max(rect.xMin, safe.xMin);
        float yMin = Mathf.Max(rect.yMin, safe.yMin);
        float xMax = Mathf.Min(rect.xMax, safe.xMax);
        float yMax = Mathf.Min(rect.yMax, safe.yMax);

        if (xMax <= xMin || yMax <= yMin)
            return rect;

        Rect visible = Rect.MinMaxRect(xMin, yMin, xMax, yMax);

        if (!bounds)
            return visible;

        Rect boundsRect = GetScreenRect(bounds, cam);
        xMin = Mathf.Max(visible.xMin, boundsRect.xMin);
        yMin = Mathf.Max(visible.yMin, boundsRect.yMin);
        xMax = Mathf.Min(visible.xMax, boundsRect.xMax);
        yMax = Mathf.Min(visible.yMax, boundsRect.yMax);

        if (xMax <= xMin || yMax <= yMin)
            return visible;

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    Rect GetScreenRect(RectTransform rectTransform, Camera cam)
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            minX = Mathf.Min(minX, screenPoint.x);
            maxX = Mathf.Max(maxX, screenPoint.x);
            minY = Mathf.Min(minY, screenPoint.y);
            maxY = Mathf.Max(maxY, screenPoint.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    Rect GetTooltipScreenRect(Camera cam)
    {
        Rect rect = _tipVisualRT ? GetScreenRect(_tipVisualRT, cam) : Rect.zero;

        if (rect.width > 0f && rect.height > 0f)
            return rect;

        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
            tooltipLayer,
            _tipRT
        );

        if (bounds.size.x <= 0f || bounds.size.y <= 0f)
            return GetScreenRect(_tipRT, cam);

        Vector3 min = tooltipLayer.TransformPoint(bounds.min);
        Vector3 max = tooltipLayer.TransformPoint(bounds.max);
        Vector2 minScreen = RectTransformUtility.WorldToScreenPoint(cam, min);
        Vector2 maxScreen = RectTransformUtility.WorldToScreenPoint(cam, max);

        return Rect.MinMaxRect(
            Mathf.Min(minScreen.x, maxScreen.x),
            Mathf.Min(minScreen.y, maxScreen.y),
            Mathf.Max(minScreen.x, maxScreen.x),
            Mathf.Max(minScreen.y, maxScreen.y)
        );
    }

    RectTransform FindTooltipVisualRoot(RectTransform root)
    {
        if (!root)
            return null;

        RectTransform best = null;
        float bestArea = 0f;
        var rectTransforms = root.GetComponentsInChildren<RectTransform>(true);
        foreach (var rt in rectTransforms)
        {
            if (rt == root)
                continue;

            if (!rt.GetComponent<Graphic>() && !rt.GetComponent<LayoutGroup>())
                continue;

            float area = Mathf.Max(0f, rt.rect.width) * Mathf.Max(0f, rt.rect.height);
            if (area <= bestArea && best)
                continue;

            best = rt;
            bestArea = area;
        }

        return best ? best : root;
    }

    Camera GetCanvasCamera(Camera eventCam)
    {
        if (!uiCanvas || uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return eventCam ? eventCam : uiCanvas.worldCamera;
    }

    void LogPlacement(
        string context,
        RectTransform bounds,
        Rect clampRect,
        Rect beforeRect,
        Rect afterRect,
        Vector2 screenDelta,
        Vector2 localDelta,
        Vector2? inputScreenPos,
        Vector2? inputLocalPos,
        bool forced
    )
    {
        if (!debugPlacement)
            return;

        if (!forced && Time.unscaledTime < _nextDebugLogTime)
            return;

        _nextDebugLogTime = Time.unscaledTime + Mathf.Max(0.01f, debugLogInterval);

        Debug.Log(
            "[Tooltip] "
                + $"context={context} "
                + $"inputScreen={FormatVec(inputScreenPos)} "
                + $"inputLocal={FormatVec(inputLocalPos)} "
                + $"anchored={FormatVec(_tipRT ? _tipRT.anchoredPosition : Vector2.zero)} "
                + $"size={FormatVec(GetTooltipSize())} "
                + $"pivot={FormatVec(_tipRT ? _tipRT.pivot : Vector2.zero)} "
                + $"before={FormatRect(beforeRect)} "
                + $"clamp={FormatRect(clampRect)} "
                + $"screenDelta={FormatVec(screenDelta)} "
                + $"localDelta={FormatVec(localDelta)} "
                + $"after={FormatRect(afterRect)} "
                + $"bounds={RectName(bounds)} "
                + $"canvas={CanvasName()} "
                + $"canvasPixel={FormatRect(uiCanvas ? uiCanvas.pixelRect : Rect.zero)} "
                + $"screen={Screen.width}x{Screen.height} "
                + $"safe={FormatRect(Screen.safeArea)} "
                + $"cam={CameraName(GetCanvasCamera(null))}"
        );
    }

    static string FormatVec(Vector2? value) => value.HasValue ? FormatVec(value.Value) : "null";

    static string FormatVec(Vector2 value) => $"({value.x:0.##},{value.y:0.##})";

    static string FormatRect(Rect rect) =>
        $"[{rect.xMin:0.##},{rect.yMin:0.##}..{rect.xMax:0.##},{rect.yMax:0.##} "
        + $"{rect.width:0.##}x{rect.height:0.##}]";

    static string RectName(RectTransform rectTransform)
    {
        if (!rectTransform)
            return "null";

        return $"{rectTransform.name} path={GetPath(rectTransform)}";
    }

    string CanvasName()
    {
        if (!uiCanvas)
            return "null";

        return $"{uiCanvas.name} mode={uiCanvas.renderMode} scale={uiCanvas.scaleFactor:0.###}";
    }

    static string CameraName(Camera cam) => cam ? cam.name : "null";

    static string GetPath(Component component)
    {
        if (!component)
            return "null";

        return GetPath(component.transform);
    }

    static string GetPath(Transform transform)
    {
        if (!transform)
            return "null";

        string path = transform.name;
        for (Transform t = transform.parent; t; t = t.parent)
            path = $"{t.name}/{path}";

        return path;
    }

    Vector2 RoundForCanvas(Vector2 value)
    {
        float sf = uiCanvas ? uiCanvas.scaleFactor : 1f;
        return new Vector2(Mathf.Round(value.x * sf) / sf, Mathf.Round(value.y * sf) / sf);
    }
}
