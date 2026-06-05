using System.Collections;
using TMPro;
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
    private string evolutionStageHintText;
    private string firstLetterHintText;
    private RectTransform textHintRoot;
    private TMP_Text textHintLabel;
    private TMP_Text evolutionStageHintLabel;
    private TMP_Text firstLetterHintLabel;
    private Image textHintBackground;
    private VerticalLayoutGroup textHintLayout;
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
    public bool IsShadowed { get; private set; }

    [SerializeField]
    Image background;

    [SerializeField]
    Outline endStateOutline;

    [SerializeField, Range(0f, 0.15f)]
    float borderPctOfSide = 0.06f;

    [SerializeField, Range(1f, 12f)]
    float borderMinPx = 3f;

    [SerializeField, Range(1f, 24f)]
    float borderMaxPx = 10f;

    static readonly Color BorderGreen = new(0f, 1f, 0f, 1f);
    static readonly Color BorderRed = new(1f, 0f, 0f, 1f);
    Color _normalColor = Color.white;
    Color _shadowColor = new Color(0f, 0f, 0f, 1f);
    public bool HintVisible => hintVisible;
    public int PokemonId => Bound != null ? Bound.id : 0;
    public Sprite CurrentSprite => spriteImage ? spriteImage.sprite : null;

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

    public void BindForPreview(Pokemon p)
    {
        Bound = p;
        IsRevealed = true;
        IsShadowed = false;

        data = p;
        loadedSprite = SpriteLibrary.Instance.ByPokemon(p);

        if (spriteImage)
        {
            spriteImage.sprite = loadedSprite;
            spriteImage.color = Color.white;
            spriteImage.enabled = true;
        }

        if (placeholderImage)
            placeholderImage.enabled = false;

        HideTypeIcons();
        ClearTextHints();
        StopAllCoroutines();
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
        ShowEndState(guessed ? BorderGreen : BorderRed);
    }

    public void ShowEndState(Color borderColor)
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
            endStateOutline.effectColor = borderColor;
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
        IsShadowed = false;
        spriteImage.sprite = null;
        data = p;
        revealed = false;
        hintVisible = false;
        ClearTextHints();

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
            IsShadowed = true;

            // overwrite any type hint
            HideTypeIcons();

            if (!spriteImage.sprite && loadedSprite != null)
                spriteImage.sprite = loadedSprite;

            spriteImage.enabled = true;
            spriteImage.color = _shadowColor;

            if (placeholderImage)
                placeholderImage.enabled = false;

            LayoutTextHints();
        }
        else
        {
            IsShadowed = false;
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
        IsShadowed = false;
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
        ClearTextHints();
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
            LayoutTextHints();
        }
    }

    public void ShowEvolutionStageHint(int stage, int totalStages)
    {
        if (IsRevealed)
            return;

        evolutionStageHintText = FormatEvolutionStageHint(stage, totalStages);
        RefreshTextHints();
    }

    public void ShowFirstLetterHint(string pokemonName)
    {
        if (IsRevealed || string.IsNullOrWhiteSpace(pokemonName))
            return;

        string trimmed = pokemonName.Trim();
        firstLetterHintText = trimmed.Length > 0 ? $"First: {trimmed[0]}" : null;
        RefreshTextHints();
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

    private void ClearTextHints()
    {
        evolutionStageHintText = null;
        firstLetterHintText = null;

        if (evolutionStageHintLabel)
            evolutionStageHintLabel.gameObject.SetActive(false);
        if (firstLetterHintLabel)
            firstLetterHintLabel.gameObject.SetActive(false);

        if (textHintRoot)
            textHintRoot.gameObject.SetActive(false);
    }

    private static string FormatEvolutionStageHint(int stage, int totalStages)
    {
        if (stage <= 0)
            return "Stage ?";

        if (totalStages <= 1)
            return "Single stage";

        return $"Stage {stage}/{totalStages}";
    }

    private void RefreshTextHints()
    {
        if (IsRevealed)
        {
            ClearTextHints();
            return;
        }

        bool hasEvolution = !string.IsNullOrWhiteSpace(evolutionStageHintText);
        bool hasFirstLetter = !string.IsNullOrWhiteSpace(firstLetterHintText);
        if (!hasEvolution && !hasFirstLetter)
        {
            if (textHintRoot)
                textHintRoot.gameObject.SetActive(false);
            return;
        }

        EnsureTextHintLabel();
        if (!textHintRoot || !textHintLabel)
            return;

        textHintRoot.gameObject.SetActive(true);
        evolutionStageHintLabel.gameObject.SetActive(hasEvolution);
        firstLetterHintLabel.gameObject.SetActive(hasFirstLetter);
        evolutionStageHintLabel.text = hasEvolution ? evolutionStageHintText : string.Empty;
        firstLetterHintLabel.text = hasFirstLetter ? firstLetterHintText : string.Empty;

        LayoutTextHints();
    }

    private void EnsureTextHintLabel()
    {
        if (textHintRoot && textHintLabel && evolutionStageHintLabel && firstLetterHintLabel)
            return;

        var rootGo = new GameObject("TextHints", typeof(RectTransform), typeof(Image));
        rootGo.transform.SetParent(transform, false);
        textHintRoot = (RectTransform)rootGo.transform;
        textHintBackground = rootGo.GetComponent<Image>();
        textHintBackground.color = new Color(0f, 0f, 0f, 0.72f);
        textHintBackground.raycastTarget = false;

        textHintLayout = rootGo.AddComponent<VerticalLayoutGroup>();
        textHintLayout.childAlignment = TextAnchor.UpperLeft;
        textHintLayout.childControlWidth = true;
        textHintLayout.childControlHeight = true;
        textHintLayout.childForceExpandWidth = true;
        textHintLayout.childForceExpandHeight = false;
        textHintLayout.spacing = 0f;
        textHintLayout.padding = new RectOffset(4, 4, 2, 2);

        evolutionStageHintLabel = CreateTextHintLine("StageLabel");
        firstLetterHintLabel = CreateTextHintLine("FirstLetterLabel");
        textHintLabel = evolutionStageHintLabel;
    }

    private TMP_Text CreateTextHintLine(string objectName)
    {
        var labelGo = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(textHintRoot, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = new Vector2(0f, 1f);
        labelRt.anchorMax = new Vector2(1f, 1f);
        labelRt.pivot = new Vector2(0f, 1f);

        var label = labelGo.GetComponent<TMP_Text>();
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = true;
        label.fontSizeMin = 6f;
        label.fontSizeMax = 14f;
        label.lineSpacing = 0f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;

        var layout = labelGo.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.flexibleHeight = 0f;

        var shadow = labelGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(1f, -1f);
        shadow.useGraphicAlpha = true;

        labelGo.SetActive(false);
        return label;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (hintVisible)
            LayoutHintIcons();

        if (textHintRoot && textHintRoot.gameObject.activeSelf)
            LayoutTextHints();

        FitImageAsCenteredSquare(spriteImage, spritePadding);
        FitImageAsCenteredSquare(placeholderImage, spritePadding);
        FitArtToCell();
        FitBackgroundToCell();
        UpdateBorderThickness();
    }

    private void FitBackgroundToCell()
    {
        if (!background)
            return;

        var cell = (RectTransform)transform;
        if (cell.rect.width <= 0f || cell.rect.height <= 0f)
            return;

        float side = Mathf.Min(cell.rect.width, cell.rect.height);

        var brt = background.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(side, side);
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

    private void LayoutTextHints()
    {
        if (!textHintRoot || !textHintRoot.gameObject.activeSelf)
            return;

        float inner = GetInnerSide();
        if (inner <= 0f)
            return;

        textHintRoot.SetAsLastSibling();
        if (highlight)
            highlight.transform.SetAsLastSibling();

        int lines =
            !string.IsNullOrWhiteSpace(evolutionStageHintText)
            && !string.IsNullOrWhiteSpace(firstLetterHintText)
                ? 2
                : 1;

        var cell = (RectTransform)transform;
        float cellW = Mathf.Max(1f, cell.rect.width);
        float cellH = Mathf.Max(1f, cell.rect.height);
        float pad = Mathf.Clamp(inner * 0.06f, 2f, 6f);
        float lineHeight = Mathf.Clamp(inner * 0.15f, 8f, 14f);
        float verticalPadding = Mathf.Clamp(inner * 0.04f, 2f, 5f);
        float maxHeight = Mathf.Max(8f, cellH - pad * 2f);
        float desiredHeight = lines * lineHeight + verticalPadding * 2f;

        textHintRoot.anchorMin = textHintRoot.anchorMax = new Vector2(0f, 1f);
        textHintRoot.pivot = new Vector2(0f, 1f);
        textHintRoot.anchoredPosition = new Vector2(pad, -pad);
        textHintRoot.sizeDelta = new Vector2(
            Mathf.Max(12f, cellW - pad * 2f),
            Mathf.Min(maxHeight, desiredHeight)
        );

        if (textHintLayout)
        {
            int insetX = Mathf.RoundToInt(Mathf.Clamp(inner * 0.045f, 2f, 5f));
            int insetY = Mathf.RoundToInt(verticalPadding);
            textHintLayout.padding = new RectOffset(insetX, insetX, insetY, insetY);
            textHintLayout.spacing = lines == 2 ? Mathf.Clamp(inner * 0.01f, 0f, 1f) : 0f;
        }

        float minFont = Mathf.Clamp(inner * 0.075f, 5f, 9f);
        float maxFont = Mathf.Clamp(inner * 0.135f, 8f, 14f);
        ApplyHintLineSizing(evolutionStageHintLabel, lineHeight, minFont, maxFont);
        ApplyHintLineSizing(firstLetterHintLabel, lineHeight, minFont, maxFont);
    }

    private static void ApplyHintLineSizing(
        TMP_Text label,
        float lineHeight,
        float minFont,
        float maxFont
    )
    {
        if (!label)
            return;

        label.fontSizeMin = minFont;
        label.fontSizeMax = Mathf.Max(minFont, maxFont);
        if (label.TryGetComponent(out LayoutElement layout))
        {
            layout.minHeight = lineHeight;
            layout.preferredHeight = lineHeight;
        }
    }
}
