using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UpdateButtonController : MonoBehaviour
{
    [Header("Wiring")]
    public Button button;
    public Image buttonBg; // the Image on the button for color
    public TMP_Text label; // the text on the button
    public UpdateChecker checker; // reference to your UpdateChecker

    [Header("Colors")]
    public Color okColor = new Color(0.70f, 0.20f, 0.20f); // red
    public Color readyColor = new Color(0.15f, 0.65f, 0.35f); // green
    public Color checkingColor = new Color(0.25f, 0.45f, 0.75f);

    UpdateInfo _pending; // non-null when update is available

    void Awake()
    {
        if (!button)
            button = GetComponent<Button>();
        if (!buttonBg)
            buttonBg = GetComponent<Image>();
        button.onClick.AddListener(OnClick);

        // initial state
        SetChecking();

        // subscribe to checker events
        checker.OnNoUpdate += () => SetNoUpdate();
        checker.OnUpdateFound += info =>
        {
            _pending = info;
            SetUpdateAvailable();
        };

        // kick off a check (or call checker.CheckNow() from elsewhere)
        checker.CheckNow();
    }

    void SetChecking()
    {
        SetVisual("Checking updates…", checkingColor, interactable: false);
    }

    void SetNoUpdate()
    {
        _pending = null;
        SetVisual("No updates available", okColor, interactable: false);
    }

    void SetUpdateAvailable()
    {
        SetVisual("Updates available!", readyColor, interactable: true);
    }

    void SetVisual(string text, Color c, bool interactable)
    {
        if (label)
            label.text = text;
        if (buttonBg)
            buttonBg.color = c;
        if (button)
            button.interactable = interactable;
    }

    void OnClick()
    {
        // Only clickable when update exists
        if (_pending == null)
            return;

        // Option A: open browser to download page (simplest)
        Application.OpenURL(_pending.url);

        // Option B: silent download + run installer (if you implemented it)
        // StartCoroutine(checker.DownloadAndInstall(_pending));
    }
}
