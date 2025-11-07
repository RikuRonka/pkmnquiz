using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
[RequireComponent(typeof(CanvasGroup))]
public class UpdateButtonController
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler
{
    [Header("Wiring")]
    public Button button;
    public Image buttonBg;
    public TMP_Text currentVersionLabel;
    public TMP_Text label;
    public UpdaterRunner checker;

    [Header("Colors")]
    public Color okColor = new(0.70f, 0.20f, 0.20f);
    public Color readyColor = new(0.15f, 0.65f, 0.35f);
    public Color checkingColor = new(0.25f, 0.45f, 0.75f);

    UpdateInfo _pending;

    void Awake()
    {
        if (!button)
            button = GetComponent<Button>();
        currentVersionLabel.SetText($"v{Application.version}");
        button.onClick.AddListener(OnClick);

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
        checker.OnStatus += s => label.SetText(s);

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

    void OnDestroy()
    {
        if (checker == null)
            return;
        checker.OnNoUpdate -= OnNoUpdate;
        checker.OnUpdateFound -= OnFound;
        checker.OnDownloadProgress -= OnProgress;
        checker.OnStatus -= OnStatusText;
    }

    void OnNoUpdate() => SetVisual("No updates available", okColor, false);

    void OnFound(UpdateInfo i)
    {
        _pending = i;
        SetVisual("Updates available!", readyColor, true);
    }

    void OnProgress(float p) => SetVisual($"Downloading {p * 100f:0}%", checkingColor, false);

    void OnStatusText(string s) => label.SetText(s);

    private void OnClick()
    {
#if UNITY_EDITOR
        Debug.LogWarning("Updater disabled in Editor.");
#else
        if (_pending == null)
            return;
        SetVisual("Preparing update…", checkingColor, false);
        checker.StartUpdate();
#endif
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (_pending == null)
            return;

        var anchor = (RectTransform)button.transform; // the "Full quiz" button RectTransform
        TooltipManager.Instance?.ShowUpdateUnder(
            anchor,
            version: $"v{_pending.version}",
            notes: _pending.notes,
            gapY: 10f,
            centerToAnchor: false // set true if you prefer centered
        );
    }

    public void OnPointerMove(PointerEventData e) { }

    public void OnPointerExit(PointerEventData e)
    {
        TooltipManager.Instance.Hide();
    }
}
