using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FinishedDialog : MonoBehaviour
{
    [SerializeField]
    CanvasGroup cg;

    [SerializeField]
    TMP_Text header;

    [SerializeField]
    TMP_Text body;

    [SerializeField]
    Button closeBtn;

    void Awake()
    {
        if (!cg)
            cg = GetComponent<CanvasGroup>();
        if (closeBtn)
            closeBtn.onClick.AddListener(Hide);
        Hide(); // start hidden
    }

    public void Show(int guessed, int total, System.TimeSpan time)
    {
        if (!cg)
            cg = GetComponent<CanvasGroup>();

        if (header)
            header.text = "Finished!";
        if (body)
            body.text = $"Guessed {guessed} • Missed {total - guessed}\nTime: {time:hh\\:mm\\:ss}";

        // ensure on top of its siblings (above ScrollRect/content)
        transform.SetAsLastSibling();

        // if it lives under some layout, give it its own overlay canvas
        // EnsureOverlayCanvas();

        cg.alpha = 1f;
        cg.blocksRaycasts = true;
        cg.interactable = true;

        Debug.Log("[FinishedDialog] Show called.");
    }

    void Update()
    {
        Debug.Log(cg.alpha);
    }

    public void Hide()
    {
        if (!cg)
            cg = GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
    }

    void EnsureOverlayCanvas()
    {
        // Optional but safe: create a local Canvas with high sorting order so nothing covers it.
        var ownCanvas = GetComponent<Canvas>();
        if (!ownCanvas)
            ownCanvas = gameObject.AddComponent<Canvas>();
        var scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
        ownCanvas.overrideSorting = true;
        ownCanvas.sortingOrder = 5000; // above everything else in this scene
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    }
}
