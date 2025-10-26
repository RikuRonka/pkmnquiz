using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

// Works with the new Input System and legacy.
// Put this on any Button (or any UI object) and wire the event in the Inspector.
public class ClickOnDown : MonoBehaviour, IPointerDownHandler
{
    public UnityEvent onDown;
    public void OnPointerDown(PointerEventData e)
    {
        EventSystem.current?.SetSelectedGameObject(null);
        onDown?.Invoke();
    }
}