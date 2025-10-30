using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class ClickOnDown : MonoBehaviour, IPointerDownHandler
{
    public UnityEvent onDown;

    public void OnPointerDown(PointerEventData eventData)
    {
        EventSystem.current?.SetSelectedGameObject(null);
        onDown?.Invoke();
    }
}
