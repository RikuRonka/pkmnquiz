using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
[RequireComponent(typeof(CanvasGroup))]
public class UpdateButtonController : MonoBehaviour
{
    [Header("Wiring")]
    public Button button;
    public Image buttonBg; // optional, can be null
    public TMP_Text currentVersionLabel; // "v1.0.0"
    public TMP_Text label; // main button text
    public UpdaterRunner checker; // your UpdaterRunner component

    [Header("Colors")]
    public Color okColor = new(0.70f, 0.20f, 0.20f);
    public Color readyColor = new(0.15f, 0.65f, 0.35f);
    public Color checkingColor = new(0.25f, 0.45f, 0.75f);

    UpdateInfo _pending;

    void Awake()
    {
#if UNITY_EDITOR
        // Editor-only: disable the button and bail out
        if (currentVersionLabel)
            currentVersionLabel.text = $"v{Application.version}";
        if (label)
            label.text = "Updates disabled in Editor";
        if (button)
            button.interactable = false;

        var cg = GetComponent<CanvasGroup>();
        if (!cg)
            cg = gameObject.AddComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false;

#else
        if (!button)
            button = GetComponent<Button>();
        if (currentVersionLabel)
            currentVersionLabel.text = $"v{Application.version}";
        button.onClick.AddListener(OnClick);

        SetChecking();
        checker.OnNoUpdate += () => SetNoUpdate();
        checker.OnUpdateFound += info =>
        {
            _pending = info;
            SetUpdateAvailable();
        };
        checker.OnDownloadProgress += p => SetStatus($"Downloading {p * 100f:0}%");
        checker.OnStatus += s => SetStatus(s);

        checker.CheckForUpdate();
#endif
    }

    void SetVisual(string text, Color c, bool interactable)
    {
        if (label)
            label.text = text;
        if (buttonBg)
            buttonBg.color = c;
        button.interactable = interactable;
    }

    void OnClick()
    {
#if UNITY_EDITOR
        // Guard in case the button is still clickable in the Editor
        Debug.Log("Update disabled in Editor.");
        return;
#endif
        if (_pending == null)
            return; // nothing to do
        SetVisual("Preparing update…", checkingColor, false);
        checker.StartUpdate(); // <-- launches download + Updater.exe
    }
}
