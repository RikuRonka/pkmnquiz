using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class Toast : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField]
    private CanvasGroup group;

    [SerializeField]
    private TMP_Text label;

    [SerializeField]
    private Graphic background;

    [Header("Timing (seconds)")]
    [SerializeField]
    private float fadeIn = 0.15f;

    [SerializeField]
    private float hold = 2.0f;

    [SerializeField]
    private float fadeOut = 0.25f;

    private Coroutine co;
    private Color defaultBackgroundColor;
    private bool hasDefaultBackgroundColor;

    void Awake()
    {
        if (!group && !TryGetComponent(out group))
            group = gameObject.AddComponent<CanvasGroup>();
        if (!background)
        {
            var rootGraphic = GetComponent<Graphic>();
            if (rootGraphic && rootGraphic != label)
                background = rootGraphic;
        }
        if (!background)
            background = FindBackgroundGraphic();
        if (background)
        {
            defaultBackgroundColor = background.color;
            hasDefaultBackgroundColor = true;
            background.raycastTarget = false;
        }

        HideImmediate();
    }

    void OnEnable()
    {
        HideImmediate();
    }

    public void Show(string message, float? seconds = null)
    {
        Show(message, seconds, null);
    }

    public void Show(string message, float? seconds, Color? backgroundColor)
    {
        if (label)
            label.text = message;
        if (seconds.HasValue)
            hold = Mathf.Max(0.05f, seconds.Value);
        EnsureBackground(backgroundColor);
        if (background)
            background.color = backgroundColor ?? (
                hasDefaultBackgroundColor ? defaultBackgroundColor : background.color
            );

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
        if (background && hasDefaultBackgroundColor)
            background.color = defaultBackgroundColor;
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private void EnsureBackground(Color? backgroundColor)
    {
        if (background || !backgroundColor.HasValue)
            return;

        background = FindBackgroundGraphic();
        if (background)
        {
            defaultBackgroundColor = background.color;
            hasDefaultBackgroundColor = true;
            background.raycastTarget = false;
            return;
        }

        var image = gameObject.AddComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = false;
        background = image;
        defaultBackgroundColor = Color.clear;
        hasDefaultBackgroundColor = true;
    }

    private Graphic FindBackgroundGraphic()
    {
        foreach (var image in GetComponentsInChildren<Image>(true))
        {
            if (!image || image.GetComponent<TMP_Text>())
                continue;

            return image;
        }

        return null;
    }
}
