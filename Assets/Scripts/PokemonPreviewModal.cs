using UnityEngine;
using UnityEngine.UI;

public class PokemonPreviewModal : MonoBehaviour
{
    public static PokemonPreviewModal Instance { get; private set; }

    [Header("Prefab to show inside modal")]
    [SerializeField]
    private PokemonCard cardPrefab;

    [Header("Sizing")]
    [SerializeField]
    private float cardScale = 8.0f;

    private Canvas _canvas;
    private Image _backdrop;
    private RectTransform _host;
    private PokemonCard _cardInstance;

    private PokemonCard _sourceCard;
    private bool _visible;

    public bool IsVisible => _visible;
    private int _currentPokemonId = -1;
    private bool _currentShadowPreview;

    public static PokemonPreviewModal Ensure(PokemonCard cardPrefab)
    {
        if (Instance)
            return Instance;

        var go = new GameObject("PokemonPreviewModal");
        DontDestroyOnLoad(go);

        var modal = go.AddComponent<PokemonPreviewModal>();
        modal.cardPrefab = cardPrefab;
        modal.BuildUI();
        Instance = modal;

        modal.HideImmediate();
        return modal;
    }

    private void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void BuildUI()
    {
        // Canvas
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000; // ensure above your UI

        gameObject.AddComponent<GraphicRaycaster>();
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Backdrop
        var bgGo = new GameObject("Backdrop");
        bgGo.transform.SetParent(transform, false);
        _backdrop = bgGo.AddComponent<Image>();
        _backdrop.color = new Color(0f, 0f, 0f, 0.75f);
        _backdrop.raycastTarget = false;

        var bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        // Host (center container)
        var hostGo = new GameObject("Host");
        hostGo.transform.SetParent(transform, false);
        _host = hostGo.AddComponent<RectTransform>();
        _host.anchorMin = new Vector2(0.5f, 0.5f);
        _host.anchorMax = new Vector2(0.5f, 0.5f);
        _host.pivot = new Vector2(0.5f, 0.5f);
        _host.anchoredPosition = Vector2.zero;
        _host.sizeDelta = new Vector2(1, 1); // card handles its own layout
    }

    public void Show(PokemonCard source)
    {
        if (!source)
            return;

        var p = source.Pokemon ?? source.Bound;
        if (p == null)
            return;

        bool showShadow = source.IsShadowed && !source.IsRevealed;

        // If already showing this exact pokemon, do nothing (prevents Bind/Reveal flicker).
        if (_visible && _currentPokemonId == p.id && _currentShadowPreview == showShadow)
            return;

        _currentPokemonId = p.id;
        _currentShadowPreview = showShadow;

        _sourceCard = source;

        if (_cardInstance == null)
        {
            if (!cardPrefab)
            {
                Debug.LogError("[PokemonPreviewModal] Missing cardPrefab.");
                return;
            }

            _cardInstance = Instantiate(cardPrefab, _host);
            _cardInstance.ClearEndState(); // optional: keep it clean

            // Ensure preview card doesn't itself trigger hover logic
            var hover = _cardInstance.GetComponent<PokemonCardHover>();
            if (hover)
                hover.enabled = false;

            // Prevent it from blocking clicks if you want backdrop to receive them:
            // (leave enabled if you want to interact with it)
            var g = _cardInstance.GetComponent<Graphic>();
            if (g)
                g.raycastTarget = false;
        }

        // Bind same Pokémon and copy reveal state
        _cardInstance.Bind(p);

        if (showShadow)
        {
            _cardInstance.SetShadowMode(true);
        }
        else
        {
            var spr = source.CurrentSprite;
            if (spr != null)
            {
                _cardInstance.Reveal(spr); // guarantees no placeholder
            }
            else
            {
                // Fallback if for some reason source sprite is missing
                _cardInstance.Reveal(); // uses loadedSprite if available
            }
        }
        if (!showShadow && source.HintVisible && p != null && p.types != null)
            _cardInstance.ShowTypeHint(p.types);

        // Big scale
        var rt = (RectTransform)_cardInstance.transform;
        rt.localScale = Vector3.one * cardScale;

        gameObject.SetActive(true);
        _visible = true;
    }

    public void Hide()
    {
        _visible = false;
        _sourceCard = null;
        _currentShadowPreview = false;
        gameObject.SetActive(false);
    }

    private void HideImmediate()
    {
        _visible = false;
        _sourceCard = null;
        _currentShadowPreview = false;
        gameObject.SetActive(false);
    }
}
