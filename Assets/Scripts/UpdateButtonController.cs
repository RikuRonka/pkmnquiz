using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
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
        if (!button)
            button = GetComponent<Button>();
        currentVersionLabel?.SetText($"v{Application.version}");
        button.onClick.AddListener(OnClick);

        // Hook updater events for UI state
        checker.OnNoUpdate += () =>
        {
            _pending = null;
            SetVisual("No updates available", okColor, false);
        };
        checker.OnUpdateFound += info =>
        {
            _pending = info;
            SetVisual("Updates available!", readyColor, true);
        };
        checker.OnDownloadProgress += p =>
            SetVisual($"Downloading {p * 100f:0}%", checkingColor, false);
        checker.OnStatus += s => label?.SetText(s);

        // Start check
        SetVisual("Checking updates…", checkingColor, false);
        checker.CheckForUpdate();
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
        if (_pending == null)
            return; // nothing to do
        SetVisual("Preparing update…", checkingColor, false);
        checker.StartUpdate(); // <-- launches download + Updater.exe
    }
}
