using TMPro;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class PauseMenu : MonoBehaviour
{
    [SerializeField]
    CanvasGroup root;
    public bool IsShowing { get; private set; }

    [SerializeField]
    TMP_Text timeLabel;

    void Awake()
    {
        if (!root)
            root = GetComponent<CanvasGroup>();
        Hide();
    }

    public void Show()
    {
        IsShowing = true;
        gameObject.SetActive(true);
        if (root)
        {
            root.alpha = 1f;
            root.interactable = true;
            root.blocksRaycasts = true;
        }
    }

    public void Hide()
    {
        IsShowing = false;
        if (root)
        {
            root.alpha = 0f;
            root.interactable = false;
            root.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }

    public void SetElapsed(System.TimeSpan t)
    {
        if (timeLabel)
            timeLabel.text = $"Time: {t:hh\\:mm\\:ss}";
    }

    public System.Action OnResume;
    public System.Action OnRestart;
    public System.Action OnBackToMenu;

    public void ClickResume() => OnResume?.Invoke();

    public void ClickRestart() => OnRestart?.Invoke();

    public void ClickBack() => OnBackToMenu?.Invoke();
}
