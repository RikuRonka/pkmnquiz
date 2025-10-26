using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class PokemonCard : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image spriteImage;       // "Sprite"
    [SerializeField] private Image placeholderImage;  // "Placeholder"
    [SerializeField] private Image typeIconL;         // "TypeIconL"
    [SerializeField] private Image typeIconR;         // "TypeIconR"
    [SerializeField] private Image highlight;         // "Highlight"  <-- NEW

    private Pokemon data;
    private Sprite loadedSprite;
    private bool revealed, hintVisible;
    private Coroutine highlightCo;

    [Header("Highlight/Shake")]
    [SerializeField] private float highlightDuration = 0.6f;
    [SerializeField] private float shakeDuration = 0.35f;
    [SerializeField] private float shakePosAmplitude = 6f;     // pixels
    [SerializeField] private float shakeRotAmplitude = 6f;     // degrees


    private Coroutine shakeCo;
    private RectTransform rt;
    private Vector2 baseAnchoredPos;
    private Quaternion baseRotation;

    void Awake()
    {
        if (!spriteImage) spriteImage = transform.Find("Sprite")?.GetComponent<Image>();
        if (!placeholderImage) placeholderImage = transform.Find("Placeholder")?.GetComponent<Image>();
        if (!typeIconL) typeIconL = transform.Find("TypeIconL")?.GetComponent<Image>();
        if (!typeIconR) typeIconR = transform.Find("TypeIconR")?.GetComponent<Image>();
        if (!highlight) highlight = transform.Find("Highlight")?.GetComponent<Image>();

        rt = (RectTransform)transform;
        baseAnchoredPos = rt.anchoredPosition;
        baseRotation = rt.localRotation;

        if (highlight) { var c = highlight.color; c.a = 0f; highlight.color = c; }
    }

    public void Bind(Pokemon p)
    {
        data = p; revealed = false; hintVisible = false;

        loadedSprite = SpriteLibrary.Instance.ByPokemon(p);
        if (spriteImage) { spriteImage.preserveAspect = true; spriteImage.type = Image.Type.Simple; spriteImage.sprite = loadedSprite; spriteImage.enabled = false; }
        if (placeholderImage) { placeholderImage.preserveAspect = true; placeholderImage.type = Image.Type.Simple; placeholderImage.enabled = true; }

        StopHighlight();
        StopShake();
        HideTypeIcons();
    }

    public void Reveal()
    {
        if (revealed || data == null) return;
        revealed = true;
        if (placeholderImage) placeholderImage.enabled = false;
        if (spriteImage) spriteImage.enabled = true;

        HideTypeIcons();
    }

    public void ShowTypeHint(string[] types)
    {
        if (revealed) return;
        HideTypeIcons();
        if (types == null || types.Length == 0) return;

        var s0 = TypeIconLibrary.Instance.Get(types[0]);
        var s1 = types.Length > 1 ? TypeIconLibrary.Instance.Get(types[1]) : null;

        if (typeIconL) { typeIconL.sprite = s0; typeIconL.enabled = s0 != null; }
        if (typeIconR) { typeIconR.sprite = s1; typeIconR.enabled = s1 != null; }
        hintVisible = (typeIconL && typeIconL.enabled) || (typeIconR && typeIconR.enabled);
        if (hintVisible) LayoutHintIcons();
    }

    private void StopHighlight()
    {
        if (!highlight) return;
        if (highlightCo != null) StopCoroutine(highlightCo);
        var c = highlight.color; c.a = 0f; highlight.color = c;
        highlight.gameObject.SetActive(false);
        highlightCo = null;
    }

    private void HideTypeIcons()
    {
        if (typeIconL) { typeIconL.sprite = null; typeIconL.enabled = false; }
        if (typeIconR) { typeIconR.sprite = null; typeIconR.enabled = false; }
        hintVisible = false;
    }

    private void LayoutHintIcons()
    {
        var rt = (RectTransform)transform;
        if (rt.rect.width <= 0 || rt.rect.height <= 0) return;
        float side = Mathf.Min(rt.rect.width, rt.rect.height);

        void Place(Image img, float size, Vector2 pos)
        {
            if (!img) return;
            var irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(size, size);
            irt.anchoredPosition = pos;
            img.preserveAspect = true;
        }

        bool dual = typeIconR && typeIconR.enabled;
        if (dual)
        {
            float size = side * 0.48f, gap = size * 0.15f;
            Place(typeIconL, size, new Vector2(-size * 0.5f - gap * 0.5f, 0f));
            Place(typeIconR, size, new Vector2(size * 0.5f + gap * 0.5f, 0f));
        }
        else
        {
            float size = side * 0.9f;
            Place(typeIconL, size, Vector2.zero);
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        if (hintVisible) LayoutHintIcons();
    }

    public void FlashHighlight(float durationOverride = -1f)
    {
        if (!highlight) { ShakeOnly(); return; }
        float d = durationOverride > 0f ? durationOverride : highlightDuration;

        if (highlightCo != null) StopCoroutine(highlightCo);
        highlightCo = StartCoroutine(FlashCo(d));

        // run shake in parallel
        ShakeOnly();
    }

    private void ShakeOnly()
    {
        if (shakeCo != null) StopCoroutine(shakeCo);
        shakeCo = StartCoroutine(ShakeCo(shakeDuration, shakePosAmplitude, shakeRotAmplitude));
    }

    private IEnumerator FlashCo(float d)
    {
        highlight.gameObject.SetActive(true);
        float t = 0f;
        while (t < d)
        {
            t += Time.deltaTime;
            // pulse alpha 0..0.65..0 with a sine
            float a = Mathf.Sin(t * Mathf.PI * 2f) * 0.5f + 0.5f; // 0..1
            a *= 0.65f;
            var c = highlight.color; c.a = a; highlight.color = c;
            yield return null;
        }
        var c0 = highlight.color; c0.a = 0f; highlight.color = c0;
        highlight.gameObject.SetActive(false);
        highlightCo = null;
    }

    private IEnumerator ShakeCo(float dur, float posAmp, float rotAmp)
    {
        // cache base each shake in case grid moved
        baseAnchoredPos = rt.anchoredPosition;
        baseRotation = rt.localRotation;

        float t = 0f;
        // random seeds so multiple shakes look natural
        float seedX = Random.value * 1000f;
        float seedY = Random.value * 2000f;
        float seedR = Random.value * 3000f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = 1f - (t / dur);           // ease out envelope 1→0
            float nx = Mathf.PerlinNoise(seedX, t * 25f) * 2f - 1f; // -1..1
            float ny = Mathf.PerlinNoise(seedY, t * 25f) * 2f - 1f;
            float nr = Mathf.PerlinNoise(seedR, t * 25f) * 2f - 1f;

            Vector2 offset = new(nx * posAmp * k, ny * posAmp * k);
            float rotZ = nr * rotAmp * k;

            rt.anchoredPosition = baseAnchoredPos + offset;
            rt.localRotation = Quaternion.Euler(0, 0, rotZ);

            yield return null;
        }

        // restore
        rt.anchoredPosition = baseAnchoredPos;
        rt.localRotation = baseRotation;
        shakeCo = null;
    }

    private void StopShake(bool restoreTransform = false)
    {
        if (shakeCo != null)
        {
            StopCoroutine(shakeCo);
            shakeCo = null;

            if (restoreTransform && rt)
            {
                rt.anchoredPosition = baseAnchoredPos; // cached at start of ShakeCo
                rt.localRotation = baseRotation;
            }
        }
    }
}
