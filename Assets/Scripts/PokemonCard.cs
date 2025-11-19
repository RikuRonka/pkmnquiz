using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Graphic))]
[RequireComponent(typeof(Image))]
public class PokemonCard : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField]
    private Image spriteImage;

    [SerializeField]
    private Image placeholderImage;

    [SerializeField]
    private Image typeIconL;

    [SerializeField]
    private Image typeIconR;

    [SerializeField]
    private Image highlight;

    [Header("Layout")]
    [SerializeField]
    private float spritePadding = 6f;

    public Pokemon Pokemon => data;
    private Pokemon data;
    private Sprite loadedSprite;
    private bool revealed,
        hintVisible;
    private Coroutine highlightCo;

    [Header("Highlight/Shake")]
    [SerializeField]
    private float highlightDuration = 0.6f;

    [SerializeField]
    private float shakeDuration = 0.35f;

    [SerializeField]
    private float shakePosAmplitude = 6f;

    [SerializeField]
    private float shakeRotAmplitude = 6f;

    private Coroutine shakeCo;
    private RectTransform rt;
    private Vector2 baseAnchoredPos;
    private Quaternion baseRotation;
    public Pokemon Bound;
    public bool IsRevealed { get; private set; }

    [SerializeField]
    Image background;

    [SerializeField]
    Outline endStateOutline; // assign in prefab OR let Awake() add it

    [SerializeField, Range(0f, 0.15f)]
    float borderPctOfSide = 0.06f; // ~6% of the shortest side

    [SerializeField, Range(1f, 12f)]
    float borderMinPx = 3f; // clamps for tiny cells

    [SerializeField, Range(1f, 24f)]
    float borderMaxPx = 10f;

    static readonly Color BorderGreen = new(0f, 1f, 0f, 1f);
    static readonly Color BorderRed = new(1f, 0f, 0f, 1f);
    Color _normalColor = Color.white;
    Color _shadowColor = new Color(0f, 0f, 0f, 1f);
    public bool HintVisible => hintVisible;

    void Awake()
    {
        if (!TryGetComponent<PokemonCardHover>(out _))
            gameObject.AddComponent<PokemonCardHover>();
        if (!spriteImage)
            spriteImage = transform.Find("Sprite").GetComponent<Image>();
        if (!placeholderImage)
            placeholderImage = transform.Find("Placeholder").GetComponent<Image>();
        if (!typeIconL)
            typeIconL = transform.Find("TypeIconL").GetComponent<Image>();
        if (!typeIconR)
            typeIconR = transform.Find("TypeIconR").GetComponent<Image>();
        if (!highlight)
            highlight = transform.Find("Highlight").GetComponent<Image>();

        rt = (RectTransform)transform;
        if (!background)
            background = GetComponent<Image>();
        if (!endStateOutline)
            endStateOutline = background ? background.GetComponent<Outline>() : null;
        if (!endStateOutline && background)
            endStateOutline = background.gameObject.AddComponent<Outline>();

        if (endStateOutline)
        {
            endStateOutline.useGraphicAlpha = false;
            endStateOutline.enabled = false;
            endStateOutline.effectColor = Color.clear;
        }
        if (spriteImage)
        {
            spriteImage.preserveAspect = true;
            spriteImage.raycastTarget = false;
        }
        if (placeholderImage)
        {
            placeholderImage.preserveAspect = true;
            placeholderImage.raycastTarget = false;
        }
        if (highlight)
        {
            var c = highlight.color;
            c.a = 0f;
            highlight.color = c;
            highlight.raycastTarget = false;
        }

        var f1 = spriteImage ? spriteImage.GetComponent<AspectRatioFitter>() : null;
        var f2 = placeholderImage ? placeholderImage.GetComponent<AspectRatioFitter>() : null;
        if (f1)
            Destroy(f1);
        if (f2)
            Destroy(f2);

        var anyGraphic = GetComponent<Graphic>();
        if (!anyGraphic)
        {
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0);
            bg.raycastTarget = true;
        }
        else
        {
            anyGraphic.raycastTarget = true;
        }

        var rootGraphic = GetComponent<Graphic>();
        if (rootGraphic)
            rootGraphic.raycastTarget = true;

        foreach (var g in GetComponentsInChildren<Graphic>(true))
        {
            if (g.gameObject == gameObject)
                continue; // keep root ON
            g.raycastTarget = false;
        }
        ResizeArtToCell();
    }

    public void ClearEndState()
    {
        if (endStateOutline)
            endStateOutline.enabled = false;

        if (background)
        {
            var c = background.color;
            c.a = 0f; // keep it transparent
            background.color = c;
        }
    }

    public void ShowEndState(bool guessed)
    {
        if (background)
        {
            // keep the card itself transparent – we only want the border
            var c = background.color;
            c.a = 0f;
            background.color = c;
            background.sprite = null;
        }

        if (endStateOutline)
        {
            UpdateBorderThickness();
            endStateOutline.effectColor = guessed ? BorderGreen : BorderRed;
            endStateOutline.enabled = true;
            endStateOutline.gameObject.GetComponent<Image>().enabled = true;
        }
    }

    private static void FitImageAsCenteredSquare(Image img, float pad)
    {
        if (!img)
            return;

        var fitter = img.GetComponent<AspectRatioFitter>();
        if (fitter)
            Destroy(fitter);

        img.type = Image.Type.Simple;
        img.preserveAspect = true;

        var parent = img.rectTransform.parent as RectTransform;
        if (!parent)
            return;

        float side = Mathf.Max(0f, Mathf.Min(parent.rect.width, parent.rect.height) - pad * 2f);

        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(side, side);
    }

    public void Bind(Pokemon p)
    {
        Bound = p;
        IsRevealed = false;
        spriteImage.sprite = null;
        data = p;
        revealed = false;
        hintVisible = false;

        loadedSprite = SpriteLibrary.Instance.ByPokemon(p);
        if (spriteImage)
        {
            spriteImage.sprite = loadedSprite;
            spriteImage.enabled = false;
        }
        if (placeholderImage)
        {
            placeholderImage.enabled = true;
        }

        StopHighlight();
        StopShake();
        HideTypeIcons();
        ResizeArtToCell();
    }

    private void ResizeArtToCell()
    {
        if (!rt)
            return;

        float side = Mathf.Max(0f, Mathf.Min(rt.rect.width, rt.rect.height) - spritePadding * 2f);

        void Size(Image img)
        {
            if (!img)
                return;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(side, side);
            img.preserveAspect = true;
            img.type = Image.Type.Simple;
        }

        Size(spriteImage);
        Size(placeholderImage);
    }

    public void SetShadowMode(bool enabled)
    {
        if (!spriteImage)
            return;

        if (IsRevealed)
            return;

        if (enabled)
        {
            // overwrite any type hint
            HideTypeIcons();

            if (!spriteImage.sprite && loadedSprite != null)
                spriteImage.sprite = loadedSprite;

            spriteImage.enabled = true;
            spriteImage.color = _shadowColor;

            if (placeholderImage)
                placeholderImage.enabled = false;
        }
        else
        {
            spriteImage.color = _normalColor;

            if (spriteImage)
                spriteImage.enabled = false;
            if (placeholderImage)
                placeholderImage.enabled = true;
        }
    }

    public void Reveal(Sprite sprite)
    {
        if (IsRevealed || Bound == null)
            return;

        IsRevealed = true;
        spriteImage.sprite = sprite;
        spriteImage.color = _normalColor;

        if (placeholderImage)
        {
            placeholderImage.enabled = false; // square disappears
            placeholderImage.color = Color.white;
            placeholderImage.transform.SetAsFirstSibling();
        }
        if (spriteImage)
        {
            spriteImage.enabled = true; // Pokémon appears
            spriteImage.transform.SetAsLastSibling();
        }
        HideTypeIcons();
    }

    public void Reveal()
    {
        if (loadedSprite == null && spriteImage != null)
            loadedSprite = spriteImage.sprite;

        Reveal(loadedSprite);
    }

    public void ShowTypeHint(string[] types)
    {
        if (revealed)
            return;
        HideTypeIcons();
        if (types == null || types.Length == 0)
            return;

        var s0 = TypeIconLibrary.Instance.Get(types[0]);
        var s1 = types.Length > 1 ? TypeIconLibrary.Instance.Get(types[1]) : null;

        if (typeIconL)
        {
            typeIconL.sprite = s0;
            typeIconL.enabled = s0 != null;
        }
        if (typeIconR)
        {
            typeIconR.sprite = s1;
            typeIconR.enabled = s1 != null;
        }
        hintVisible = (typeIconL && typeIconL.enabled) || (typeIconR && typeIconR.enabled);
        if (hintVisible)
        {
            if (placeholderImage)
                placeholderImage.enabled = false;
            LayoutHintIcons();
        }
    }

    private void StopHighlight()
    {
        if (!highlight)
            return;
        if (highlightCo != null)
            StopCoroutine(highlightCo);
        var c = highlight.color;
        c.a = 0f;
        highlight.color = c;
        highlight.gameObject.SetActive(false);
        highlightCo = null;
    }

    private void HideTypeIcons()
    {
        if (typeIconL)
        {
            typeIconL.sprite = null;
            typeIconL.enabled = false;
        }
        if (typeIconR)
        {
            typeIconR.sprite = null;
            typeIconR.enabled = false;
        }
        hintVisible = false;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (hintVisible)
            LayoutHintIcons();

        FitImageAsCenteredSquare(spriteImage, spritePadding);
        FitImageAsCenteredSquare(placeholderImage, spritePadding);
        FitArtToCell();
        UpdateBorderThickness();
    }

    void UpdateBorderThickness()
    {
        if (!endStateOutline || !rt)
            return;

        float side = Mathf.Min(rt.rect.width, rt.rect.height);
        if (side <= 0f)
            return;

        float px = Mathf.Clamp(Mathf.Round(side * borderPctOfSide), borderMinPx, borderMaxPx);
        endStateOutline.effectDistance = new Vector2(px, -px);
    }

    public void FlashHighlight(float durationOverride = -1f)
    {
        if (!highlight)
        {
            ShakeOnly();
            return;
        }

        highlight.transform.SetAsLastSibling();

        float d = durationOverride > 0f ? durationOverride : highlightDuration;

        if (highlightCo != null)
            StopCoroutine(highlightCo);
        highlightCo = StartCoroutine(FlashCo(d));

        ShakeOnly();
    }

    private void ShakeOnly()
    {
        if (shakeCo != null)
            StopCoroutine(shakeCo);
        shakeCo = StartCoroutine(ShakeCo(shakeDuration, shakePosAmplitude, shakeRotAmplitude));
    }

    private IEnumerator FlashCo(float d)
    {
        highlight.gameObject.SetActive(true);
        float t = 0f;
        while (t < d)
        {
            t += Time.deltaTime;

            float a = Mathf.Sin(t * Mathf.PI * 2f) * 0.5f + 0.5f;
            a *= 0.65f;
            var c = highlight.color;
            c.a = a;
            highlight.color = c;
            yield return null;
        }
        var c0 = highlight.color;
        c0.a = 0f;
        highlight.color = c0;
        highlight.gameObject.SetActive(false);
        highlightCo = null;
    }

    private IEnumerator ShakeCo(float dur, float posAmp, float rotAmp)
    {
        baseAnchoredPos = rt.anchoredPosition;
        baseRotation = rt.localRotation;

        float t = 0f;

        float seedX = Random.value * 1000f;
        float seedY = Random.value * 2000f;
        float seedR = Random.value * 3000f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = 1f - (t / dur);
            float nx = Mathf.PerlinNoise(seedX, t * 25f) * 2f - 1f;
            float ny = Mathf.PerlinNoise(seedY, t * 25f) * 2f - 1f;
            float nr = Mathf.PerlinNoise(seedR, t * 25f) * 2f - 1f;

            Vector2 offset = new(nx * posAmp * k, ny * posAmp * k);
            float rotZ = nr * rotAmp * k;

            rt.anchoredPosition = baseAnchoredPos + offset;
            rt.localRotation = Quaternion.Euler(0, 0, rotZ);

            yield return null;
        }

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
                rt.anchoredPosition = baseAnchoredPos;
                rt.localRotation = baseRotation;
            }
        }
    }

    private void FitArtToCell()
    {
        var cell = (RectTransform)transform;
        if (!spriteImage || cell.rect.width <= 0f || cell.rect.height <= 0f)
            return;

        float side = Mathf.Min(cell.rect.width, cell.rect.height) * 0.9f;

        void Size(Image img)
        {
            if (!img)
                return;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(side, side);
            r.anchoredPosition = Vector2.zero;
            img.preserveAspect = true;
            img.type = Image.Type.Simple;
        }

        Size(spriteImage);
        Size(placeholderImage);
    }

    private float GetInnerSide()
    {
        var cell = (RectTransform)transform;
        float cellSide = Mathf.Min(cell.rect.width, cell.rect.height);

        float inner = Mathf.Max(0f, cellSide - spritePadding * 2f);

        if (spriteImage)
        {
            var r = spriteImage.rectTransform.rect;
            if (r.width > 0f && r.height > 0f)
                inner = Mathf.Min(inner, Mathf.Min(r.width, r.height));
        }
        if (placeholderImage && !revealed)
        {
            var r = placeholderImage.rectTransform.rect;
            if (r.width > 0f && r.height > 0f)
                inner = Mathf.Min(inner, Mathf.Min(r.width, r.height));
        }
        return inner;
    }

    private void LayoutHintIcons()
    {
        float inner = GetInnerSide();
        if (inner <= 0f)
            return;

        if (typeIconL)
            typeIconL.transform.SetAsLastSibling();
        if (typeIconR)
            typeIconR.transform.SetAsLastSibling();
        if (highlight)
            highlight.transform.SetAsLastSibling();

        static void Place(Image img, float size, Vector2 pos)
        {
            if (!img)
                return;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = true;
        }

        bool dual = typeIconR && typeIconR.enabled;

        if (dual)
        {
            float size = inner * 0.45f;
            float gap = Mathf.Min(size * 0.18f, inner * 0.12f);
            float half = size * 0.5f + gap * 0.5f;
            Place(typeIconL, size, new Vector2(-half, 0f));
            Place(typeIconR, size, new Vector2(+half, 0f));
        }
        else
        {
            float size = inner * 0.90f;
            Place(typeIconL, size, Vector2.zero);
        }
    }
}
