using UnityEngine;
using UnityEngine.EventSystems;

public class PokemonCardHover
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler
{
    PokemonCard card;
    bool _hovering;

    void Awake() => card = GetComponent<PokemonCard>();

    bool CanShow => card != null && card.IsRevealed && card.Pokemon != null;

    public void OnPointerEnter(PointerEventData e)
    {
        if (!CanShow)
            return;

        var p = card.Pokemon;
        string t1 = p.types != null && p.types.Length > 0 ? p.types[0] : null;
        string t2 = p.types != null && p.types.Length > 1 ? p.types[1] : null;
        var cam = e.pressEventCamera ?? e.enterEventCamera ?? Camera.main;
        TooltipManager.Instance?.Show(p.name, t1, t2, e.position, cam);
        _hovering = true;
    }

    public void OnPointerMove(PointerEventData e)
    {
        if (!CanShow)
            return;
        var cam = e.pressEventCamera ?? e.enterEventCamera ?? Camera.main;
        TooltipManager.Instance?.Move(e.position, cam);
    }

    public void OnPointerExit(PointerEventData e)
    {
        _hovering = false;
        TooltipManager.Instance?.Hide();
    }

    void Update()
    {
        if (!_hovering || !CanShow)
            return;

        Vector2 pos;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER

        var mouse = UnityEngine.InputSystem.Mouse.current;
        pos = mouse != null ? mouse.position.ReadValue() : (Vector2)Input.mousePosition;
#else

        pos = Input.mousePosition;
#endif
        TooltipManager.Instance?.Move(pos, null); // Overlay canvas => cam is null
    }
}
