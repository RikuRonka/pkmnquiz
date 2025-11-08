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

    public void Show(int guessed, int total, TimeSpan elapsed, bool gaveUp)
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
            body.text = $"Guessed {guessed} • Missed {missed}\nTime: {elapsed:hh\\:mm\\:ss}";
        }

        cg.alpha = 1f;
        cg.blocksRaycasts = true;
        cg.interactable = true;
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
