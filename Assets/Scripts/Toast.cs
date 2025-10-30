using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class Toast : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField]
    private CanvasGroup group;

    [SerializeField]
    private TMP_Text label;

    [Header("Timing (seconds)")]
    [SerializeField]
    private float fadeIn = 0.15f;

    [SerializeField]
    private float hold = 2.0f;

    [SerializeField]
    private float fadeOut = 0.25f;

    private Coroutine co;

    void Awake()
    {
        if (!group && !TryGetComponent(out group))
            group = gameObject.AddComponent<CanvasGroup>();

        HideImmediate();
    }

    void OnEnable()
    {
        HideImmediate();
    }

    public void Show(string message, float? seconds = null)
    {
        if (label)
            label.text = message;
        if (seconds.HasValue)
            hold = Mathf.Max(0.05f, seconds.Value);

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        if (co != null)
            StopCoroutine(co);
        co = StartCoroutine(ShowCo());
    }

    IEnumerator ShowCo()
    {
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(t / fadeIn);
            yield return null;
        }
        group.alpha = 1f;

        float h = hold;
        while (h > 0f)
        {
            h -= Time.unscaledDeltaTime;
            yield return null;
        }

        t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Clamp01(t / fadeOut);
            yield return null;
        }

        HideImmediate();
        co = null;
    }

    private void HideImmediate()
    {
        if (!group)
            return;
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }
}
