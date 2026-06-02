using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(PokemonCard))]
public class PokemonCardHover
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler
{
    private PokemonCard card;
    private bool _hovering;
    private RectTransform _tooltipBounds;

    // Global pinned state (one modal for all cards)
    private static bool s_pinned;
    private static int s_ctrlHandledFrame = -1;

    void Awake()
    {
        card = GetComponent<PokemonCard>();
        _tooltipBounds = FindTooltipBounds();
    }

    bool CanShowTooltip => card != null && card.IsRevealed && card.Pokemon != null;
    bool CanShowModal =>
        card != null && (card.IsRevealed || card.IsShadowed) && card.Pokemon != null;

    public void OnPointerEnter(PointerEventData e)
    {
        _hovering = true;

        // If preview is pinned, update preview to this card immediately.
        if (s_pinned && CanShowModal)
        {
            PokemonPreviewModal.Instance?.Show(card);
            TooltipManager.Instance?.Hide();
            return;
        }

        TryShowTooltip(e);
    }

    public void OnPointerMove(PointerEventData e)
    {
        if (!_hovering)
            return;
        if (s_pinned)
            return; // pinned preview: no tooltip movement
        if (!CanShowTooltip)
            return;

        var cam = e.pressEventCamera ?? e.enterEventCamera ?? Camera.main;
        TooltipManager.Instance?.MoveFollow(e.position, cam, TooltipBounds);
    }

    public void OnPointerExit(PointerEventData e)
    {
        _hovering = false;

        // If pinned, keep modal open; only hide tooltip.
        TooltipManager.Instance?.Hide();
    }

    void Update()
    {
        bool ctrlPressed = CtrlPressedThisFrame();
        if (ctrlPressed && s_ctrlHandledFrame == Time.frameCount)
            ctrlPressed = false;

        if (ctrlPressed && s_pinned)
        {
            s_ctrlHandledFrame = Time.frameCount;
            s_pinned = false;
            PokemonPreviewModal.Instance?.Hide();
            TooltipManager.Instance?.Hide();
            return;
        }

        // Only toggle when we're hovering a card (prevents Ctrl anywhere toggling randomly)
        if (!_hovering)
            return;

        if (ctrlPressed)
        {
            s_ctrlHandledFrame = Time.frameCount;
            // Toggle
            s_pinned = !s_pinned;

            if (s_pinned)
            {
                TooltipManager.Instance?.Hide();
                if (CanShowModal)
                    PokemonPreviewModal.Instance?.Show(card);
            }
            else
            {
                PokemonPreviewModal.Instance?.Hide();
                // Optionally restore tooltip after unpin
                if (CanShowTooltip)
                {
                    var p = card.Pokemon;
                    string t1 = p.types != null && p.types.Length > 0 ? p.types[0] : null;
                    string t2 = p.types != null && p.types.Length > 1 ? p.types[1] : null;
                    Vector2 pos = GetMousePos();
                    TooltipManager.Instance?.ShowFollow(p.name, t1, t2, null, pos, null, TooltipBounds);
                }
            }
        }

        // While not pinned, keep tooltip following mouse (your original behavior)
        if (!s_pinned && _hovering && CanShowTooltip)
        {
            Vector2 pos = GetMousePos();
            TooltipManager.Instance?.MoveFollow(pos, null, TooltipBounds); // overlay canvas
        }

        // While pinned, update preview live as you hover across cards (nice UX)
        if (s_pinned && _hovering && CanShowModal)
        {
            PokemonPreviewModal.Instance?.Show(card);
        }
    }

    private void TryShowTooltip(PointerEventData e)
    {
        if (!CanShowTooltip)
            return;

        var p = card.Pokemon;
        string t1 = p.types != null && p.types.Length > 0 ? p.types[0] : null;
        string t2 = p.types != null && p.types.Length > 1 ? p.types[1] : null;

        var cam = e.pressEventCamera ?? e.enterEventCamera ?? Camera.main;
        LogTooltipBounds(p.name, cam);
        TooltipManager.Instance?.ShowFollow(p.name, t1, t2, null, e.position, cam, TooltipBounds);
    }

    private RectTransform TooltipBounds =>
        _tooltipBounds ? _tooltipBounds : (_tooltipBounds = FindTooltipBounds());

    private RectTransform FindTooltipBounds()
    {
        for (Transform t = transform.parent; t; t = t.parent)
        {
            var scrollRect = t.GetComponent<ScrollRect>();
            if (scrollRect && scrollRect.viewport)
                return scrollRect.viewport;

            if (t.TryGetComponent<RectMask2D>(out _) || t.TryGetComponent<Mask>(out _))
                return t as RectTransform;
        }

        return null;
    }

    private void LogTooltipBounds(string pokemonName, Camera cam)
    {
        var manager = TooltipManager.Instance;
        if (!manager || !manager.debugPlacement)
            return;

        Debug.Log(
            "[PokemonCardHover] "
                + $"pokemon={pokemonName} "
                + $"card={GetPath(transform)} "
                + $"bounds={GetPath(TooltipBounds)} "
                + $"cam={(cam ? cam.name : "null")} "
                + $"boundsCandidates={DescribeBoundsCandidates()}"
        );
    }

    private string DescribeBoundsCandidates()
    {
        string result = "";
        for (Transform t = transform.parent; t; t = t.parent)
        {
            var scrollRect = t.GetComponent<ScrollRect>();
            if (scrollRect && scrollRect.viewport)
                result += $" ScrollRect:{GetPath(t)} viewport:{GetPath(scrollRect.viewport)};";

            if (t.TryGetComponent<RectMask2D>(out _))
                result += $" RectMask2D:{GetPath(t)};";

            if (t.TryGetComponent<Mask>(out _))
                result += $" Mask:{GetPath(t)};";
        }

        return string.IsNullOrWhiteSpace(result) ? "none" : result.Trim();
    }

    private static string GetPath(Component component)
    {
        if (!component)
            return "null";

        return GetPath(component.transform);
    }

    private static string GetPath(Transform transform)
    {
        if (!transform)
            return "null";

        string path = transform.name;
        for (Transform t = transform.parent; t; t = t.parent)
            path = $"{t.name}/{path}";

        return path;
    }

    private static bool CtrlPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null)
            return false;
        return kb.leftCtrlKey.wasPressedThisFrame || kb.rightCtrlKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl);
#endif
    }

    private static Vector2 GetMousePos()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        return mouse != null ? mouse.position.ReadValue() : (Vector2)Input.mousePosition;
#else
        return Input.mousePosition;
#endif
    }
}
