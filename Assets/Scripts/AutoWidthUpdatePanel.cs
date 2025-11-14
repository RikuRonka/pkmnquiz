using TMPro;
using UnityEngine;

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

        // how wide is the screen area we’re allowed to use?
        var parentRect = panel.parent as RectTransform;
        float maxWidth = parentRect.rect.width - horizontalMargin * 2f;

        // 1) preferred size with no width limit
        var noLimit = body.GetPreferredValues(body.text, Mathf.Infinity, Mathf.Infinity);
        float targetWidth = Mathf.Min(noLimit.x, maxWidth);

        // 2) now ask TMP what height it needs when clamped to that width
        var constrained = body.GetPreferredValues(body.text, targetWidth, Mathf.Infinity);

        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, constrained.y);

        // if the text rect is different from the panel, also size that:
        body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
        body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, constrained.y);
    }

    // call this after you set the text (or in Start if text is static)
    void Start() => RefreshSize();
}
