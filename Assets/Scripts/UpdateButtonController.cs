using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
[RequireComponent(typeof(Image))]
public class UpdateButtonController : MonoBehaviour
{
    [Header("Wiring")]
    public Button button;
    public Image buttonBg;
    public TMP_Text currentVersionLabel;
    public TMP_Text label;
    public UpdateChecker checker;

    [Header("Colors")]
    public Color okColor = new Color(0.70f, 0.20f, 0.20f);
    public Color readyColor = new Color(0.15f, 0.65f, 0.35f);
    public Color checkingColor = new Color(0.25f, 0.45f, 0.75f);

    UpdateInfo _pending;

    void Awake()
    {
        currentVersionLabel.text = $"v{Application.version}";
        if (!button)
            button = GetComponent<Button>();
        if (!buttonBg)
            buttonBg = GetComponent<Image>();
        button.onClick.AddListener(OnClick);

        SetChecking();

        checker.OnNoUpdate += () => SetNoUpdate();
        checker.OnUpdateFound += info =>
        {
            _pending = info;
            SetUpdateAvailable();
        };

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
        SetVisual(
            $"Updates available! v{UpdateInfo.Instance.version}",
            readyColor,
            interactable: true
        );
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
        if (_pending == null)
            return;

        Application.OpenURL(_pending.url);
    }
}
