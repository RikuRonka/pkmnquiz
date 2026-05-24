using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ClickOnDown : MonoBehaviour, IPointerDownHandler
{
    public UnityEvent onDown;
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button && !button.interactable)
            return;

        EventSystem.current?.SetSelectedGameObject(null);
        onDown?.Invoke();
    }
}
