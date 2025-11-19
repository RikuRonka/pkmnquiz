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

    void Awake()
    {
        if (!cg)
            cg = GetComponent<CanvasGroup>();
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

        if (header)
            header.text = gaveUp ? "Finished! (You gave up)" : "Finished!";

        if (body)
        {
            var missed = Mathf.Max(0, total - guessed);
            body.text =
                $"Time: {elapsed:hh\\:mm\\:ss}\n"
                + $"Guessed: {guessed} \nMissed: {missed}\n"
                + $"Type hints used: {hintsUsed} \nShadows used: {shadowsUsed}";
        }

        cg.alpha = 1f;
        cg.blocksRaycasts = true;
        cg.interactable = true;
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
