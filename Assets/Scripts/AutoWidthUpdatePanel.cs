using TMPro;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class AutoWidthUpdatePanel : MonoBehaviour
{
    [SerializeField]
    TMP_Text body; // your notes text

    [SerializeField]
    RectTransform panel; // black panel rect

    [SerializeField]
    float horizontalMargin = 40f; // margin from screen edges

    void Awake()
    {
        if (!panel)
            panel = GetComponent<RectTransform>();
        if (!body)
            body = GetComponentInChildren<TMP_Text>();
    }

    public void RefreshSize()
    {
        if (!body || !panel)
            return;

        body.enableWordWrapping = true;
        body.overflowMode = TextOverflowModes.Overflow;

        var parentRect = panel.parent as RectTransform;
        float maxWidth = parentRect.rect.width - horizontalMargin * 2f;

        var noLimit = body.GetPreferredValues(body.text, Mathf.Infinity, Mathf.Infinity);
        float targetWidth = Mathf.Min(noLimit.x, maxWidth);

        var constrained = body.GetPreferredValues(body.text, targetWidth, Mathf.Infinity);

        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, constrained.y);

        body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
        body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, constrained.y);
    }

    void Start() => RefreshSize();
}
