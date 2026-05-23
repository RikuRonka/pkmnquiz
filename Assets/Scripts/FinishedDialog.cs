using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
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

    private bool layoutCaptured;
    private Vector3 originalDialogScale;
    private float originalHeaderFontSize;
    private bool originalHeaderAutoSizing;
    private float originalBodyFontSize;
    private bool originalBodyAutoSizing;
    private float originalBodyFontSizeMin;
    private float originalBodyFontSizeMax;
    private Vector2 originalBodySize;
    private Vector2 originalBodyPosition;

    public bool IsShowing
    {
        get
        {
            if (!cg)
                cg = GetComponent<CanvasGroup>();

            return gameObject.activeInHierarchy && cg && cg.alpha > 0.01f;
        }
    }

    void Awake()
    {
        if (!cg)
            cg = GetComponent<CanvasGroup>();
        CaptureLayout();
        if (closeBtn)
            closeBtn.onClick.AddListener(Hide);
        Hide();
    }

    public void Show(
        int guessed,
        int total,
        TimeSpan elapsed,
        bool gaveUp,
        int hintsUsed,
        int shadowsUsed
    )
    {
        if (!cg)
            cg = GetComponent<CanvasGroup>();
        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        var multiplayerStats = QuizMultiplayerCoordinator.GetFinishedStatsText();
        bool hasMultiplayerStats = !string.IsNullOrEmpty(multiplayerStats);
        ApplyAdaptiveLayout(hasMultiplayerStats);

        if (header)
            header.text = gaveUp ? "Finished! (You gave up)" : "Finished!";

        if (body)
        {
            var missed = Mathf.Max(0, total - guessed);
            body.richText = true;
            body.text =
                $"Time: {elapsed:hh\\:mm\\:ss}\n"
                + $"Guessed: {guessed} \nMissed: {missed}\n"
                + $"Type hints used: {hintsUsed} \nShadows used: {shadowsUsed}";

            if (hasMultiplayerStats)
                body.text += $"\n\n{multiplayerStats}";
        }

        cg.alpha = 1f;
        cg.blocksRaycasts = true;
        cg.interactable = true;
    }

    private void CaptureLayout()
    {
        if (layoutCaptured)
            return;

        originalDialogScale = transform.localScale;

        if (header)
        {
            originalHeaderFontSize = header.fontSize;
            originalHeaderAutoSizing = header.enableAutoSizing;
        }

        if (body)
        {
            originalBodyFontSize = body.fontSize;
            originalBodyAutoSizing = body.enableAutoSizing;
            originalBodyFontSizeMin = body.fontSizeMin;
            originalBodyFontSizeMax = body.fontSizeMax;
            originalBodySize = body.rectTransform.sizeDelta;
            originalBodyPosition = body.rectTransform.anchoredPosition;
        }

        layoutCaptured = true;
    }

    private void ApplyAdaptiveLayout(bool hasMultiplayerStats)
    {
        CaptureLayout();

        transform.localScale = originalDialogScale;

        if (header)
        {
            header.enableAutoSizing = originalHeaderAutoSizing;
            header.fontSize = originalHeaderFontSize;
        }

        if (body)
        {
            body.enableAutoSizing = originalBodyAutoSizing;
            body.fontSize = originalBodyFontSize;
            body.fontSizeMin = originalBodyFontSizeMin;
            body.fontSizeMax = originalBodyFontSizeMax;
            body.rectTransform.sizeDelta = originalBodySize;
            body.rectTransform.anchoredPosition = originalBodyPosition;
        }

        if (!hasMultiplayerStats)
            return;

        transform.localScale = new Vector3(
            originalDialogScale.x * 1.08f,
            originalDialogScale.y * 1.08f,
            originalDialogScale.z
        );

        if (header)
        {
            header.enableAutoSizing = false;
            header.fontSize = originalHeaderFontSize * 0.88f;
        }

        if (!body)
            return;

        float coOpBodySize = originalBodyFontSize * 0.62f;
        body.enableAutoSizing = true;
        body.fontSize = coOpBodySize;
        body.fontSizeMax = coOpBodySize;
        body.fontSizeMin = Mathf.Max(14f, coOpBodySize * 0.78f);
        body.rectTransform.sizeDelta = new Vector2(
            originalBodySize.x * 1.18f,
            originalBodySize.y * 1.42f
        );
        float bodyHeightDelta = body.rectTransform.sizeDelta.y - originalBodySize.y;
        body.rectTransform.anchoredPosition =
            originalBodyPosition + new Vector2(0f, -0.5f * bodyHeightDelta - 18f);
    }

    // optional: keep old signature so any other code still compiles
    public void Show(int guessed, int total, TimeSpan elapsed, bool gaveUp)
    {
        Show(guessed, total, elapsed, gaveUp, 0, 0);
    }

    public void Hide()
    {
        if (!cg)
            cg = GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
    }
}
