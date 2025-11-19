using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))]
[RequireComponent(typeof(Shadow))]
[RequireComponent(typeof(Button))]
public class UiButtonHover
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        ISelectHandler,
        IDeselectHandler,
        ISubmitHandler
{
    [Header("Targets")]
    public RectTransform target;
    public Image background;
    public TMP_Text label;
    public Shadow shadow;

    [Header("State")]
    [SerializeField]
    Button button;

    [Header("Scale")]
    public float hoverScale = 1.06f;
    public float pressScale = 0.98f;
    public float tweenTime = 0.12f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Colors")]
    public Color bgNormal = new(1, 1, 1, 1);
    public Color bgHover = new(0.95f, 0.95f, 1f, 1);
    public Color textNormal = Color.black;
    public Color textHover = Color.black;

    [Header("Shadow Lift")]
    public Vector2 shadowNormal = new(0f, -1.5f);
    public Vector2 shadowHover = new(0f, -3.0f);

    [Header("Audio (optional)")]
    public AudioSource audioSource;
    public AudioClip hoverClip;
    public AudioClip clickClip;

    Vector3 _baseScale;
    bool _hovered;
    Coroutine _anim;

    void Reset()
    {
        target = GetComponent<RectTransform>();
        background = GetComponent<Image>();
        label = GetComponentInChildren<TMP_Text>();
        shadow = GetComponent<Shadow>();
        button = GetComponent<Button>();
    }

    void Awake()
    {
        if (!target)
            target = (RectTransform)transform;
        if (!button)
            button = GetComponent<Button>();
        _baseScale = target.localScale;
        ApplyColors(bgNormal, textNormal);
        ApplyShadow(shadowNormal);
    }

    public void OnPointerEnter(PointerEventData _)
    {
        if (!IsInteractable())
            return;
        SetHover(true, playSound: true);
    }

    public void OnPointerExit(PointerEventData _)
    {
        SetHover(false);
    }

    public void OnPointerDown(PointerEventData _)
    {
        if (!IsInteractable())
            return;
        TweenScale(pressScale);
    }

    public void OnPointerUp(PointerEventData _)
    {
        if (!IsInteractable())
            return;
        TweenScale(_hovered ? hoverScale : 1f);
    }

    public void OnSelect(BaseEventData _)
    {
        if (!IsInteractable())
            return;
        SetHover(true);
    }

    public void OnDeselect(BaseEventData _)
    {
        SetHover(false);
    }

    public void OnSubmit(BaseEventData _)
    {
        if (!IsInteractable())
            return;
        Play(clickClip);
        TweenBump();
    }

    bool IsInteractable()
    {
        return !button || button.interactable;
    }

    void SetHover(bool on, bool playSound = false)
    {
        if (on && !IsInteractable())
            return;
        _hovered = on;
        TweenScale(on ? hoverScale : 1f);
        ApplyColors(on ? bgHover : bgNormal, on ? textHover : textNormal);
        ApplyShadow(on ? shadowHover : shadowNormal);
        if (playSound)
            Play(hoverClip);
    }

    void TweenScale(float targetScale)
    {
        if (_anim != null)
            StopCoroutine(_anim);
        _anim = StartCoroutine(ScaleCo(targetScale));
    }

    IEnumerator ScaleCo(float targetScale)
    {
        var start = target.localScale;
        var end = _baseScale * targetScale;
        float t = 0f;
        while (t < tweenTime)
        {
            t += Time.unscaledDeltaTime;
            float k = ease.Evaluate(Mathf.Clamp01(t / tweenTime));
            target.localScale = Vector3.LerpUnclamped(start, end, k);
            yield return null;
        }
        target.localScale = end;
    }

    public void RefreshDisabledVisual()
    {
        if (!background)
            return;

        if (IsInteractable())
            ApplyColors(bgNormal, textNormal);
        else
        {
            var c = bgNormal * 0.7f;
            c.a = bgNormal.a;
            ApplyColors(c, textNormal * 0.7f);
            target.localScale = _baseScale;
            _hovered = false;
        }
    }

    void TweenBump() => StartCoroutine(BumpCo());

    IEnumerator BumpCo()
    {
        float outTime = tweenTime * 0.6f;
        float inTime = tweenTime * 0.6f;
        var start = target.localScale;
        var outScale = _baseScale * pressScale;
        var inScale = _baseScale * (_hovered ? hoverScale : 1f);

        float t = 0f;
        while (t < outTime)
        {
            t += Time.unscaledDeltaTime;
            target.localScale = Vector3.Lerp(start, outScale, ease.Evaluate(t / outTime));
            yield return null;
        }
        t = 0f;
        while (t < inTime)
        {
            t += Time.unscaledDeltaTime;
            target.localScale = Vector3.Lerp(outScale, inScale, ease.Evaluate(t / inTime));
            yield return null;
        }
        target.localScale = inScale;
    }

    void ApplyColors(Color bg, Color txt)
    {
        if (background)
            background.color = bg;
        if (label)
            label.color = txt;
    }

    void ApplyShadow(Vector2 effect)
    {
        if (!shadow)
            return;
        shadow.effectDistance = effect;
    }

    void Play(AudioClip clip)
    {
        if (clip && audioSource)
            audioSource.PlayOneShot(clip, 0.9f);
    }
}
