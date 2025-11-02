using UnityEngine;
using UnityEngine.EventSystems;

public class PokemonCardHover
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler
{
    PokemonCard card;

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
        TooltipManager.Instance?.Hide();
    }
}
