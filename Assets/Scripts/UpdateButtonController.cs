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
    bool _checking;

    void Awake()
    {
        if (!button)
            button = GetComponent<Button>();
        if (currentVersionLabel)
            currentVersionLabel.SetText($"v{Application.version}");
        button.onClick.AddListener(OnClick);

        if (checker)
        {
            checker.OnNoUpdate += OnNoUpdate;
            checker.OnUpdateFound += OnFound;
            checker.OnDownloadProgress += OnProgress;
            checker.OnStatus += OnStatusText;
            checker.OnCheckFailed += OnCheckFailed;
        }

        SetVisual("Check updates", okColor, true);
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

    void OnDestroy()
    {
        if (checker == null)
            return;

        checker.OnNoUpdate -= OnNoUpdate;
        checker.OnUpdateFound -= OnFound;
        checker.OnDownloadProgress -= OnProgress;
        checker.OnStatus -= OnStatusText;
        checker.OnCheckFailed -= OnCheckFailed;
    }

    void OnNoUpdate()
    {
        _checking = false;
        _pending = null;
        SetVisual("No updates available", okColor, true);
    }

    void OnFound(UpdateInfo i)
    {
        _checking = false;
        _pending = i;
        SetVisual("Updates available!", readyColor, true);
    }

    void OnProgress(float p) => SetVisual($"Downloading {p * 100f:0}%", checkingColor, false);

    void OnStatusText(string s)
    {
        if (label)
            label.SetText(s);
    }

    void OnCheckFailed(string message)
    {
        _checking = false;
        _pending = null;
        SetVisual(string.IsNullOrWhiteSpace(message) ? "Check failed" : message, okColor, true);
    }

    private void OnClick()
    {
        if (_checking)
            return;

        if (_pending == null)
        {
            BeginCheck();
            return;
        }

#if UNITY_EDITOR
        Debug.LogWarning("Updater disabled in Editor.");
#else
        SetVisual("Preparing update...", checkingColor, false);
        checker.StartUpdate();
#endif
    }

    private void BeginCheck()
    {
        if (!checker)
        {
            SetVisual("Updater missing", okColor, false);
            return;
        }

        _checking = true;
        _pending = null;
        TooltipManager.Instance?.Hide();
        SetVisual("Checking updates...", checkingColor, false);
        checker.CheckForUpdate();
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (_pending == null)
            return;

        var anchor = (RectTransform)button.transform;
        TooltipManager.Instance?.ShowUpdateUnder(
            anchor,
            version: $"v{_pending.version}",
            notes: _pending.NotesText,
            gapY: 10f,
            centerToAnchor: false
        );
    }

    public void OnPointerMove(PointerEventData e) { }

    public void OnPointerExit(PointerEventData e)
    {
        TooltipManager.Instance?.Hide();
    }
}
