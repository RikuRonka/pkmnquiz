using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class QuizManager : MonoBehaviour, IQuizProgress
{
    [Header("UI")]
    [SerializeField]
    private Image backgroundImage;
    public TMP_InputField guessInput;
    public event Action<Pokemon> OnPokemonSolved;
    public event Action OnQuizReset;

    [SerializeField]
    TMP_Text spellingHelpText;
    public TMP_Text scoreText;
    public TMP_Text timerText;
    public Button giveUpBtn;

    [SerializeField]
    private Slider cardSizeSlider;

    [SerializeField]
    private TMP_Text cardSizeLabel;

    [SerializeField]
    private int minColsLarge = 6;

    [SerializeField]
    private int maxColsSmall = 30;
    private int currentCols;

    private readonly List<GridAutoFit> _fits = new();
    const string KEY_COLS = "card_cols";
    public FinishedDialog finishedDialog;

    [Header("Grid")]
    public PokemonCard cardPrefab;

    [Header("Loader")]
    public LoadingManager loaderPrefab;
    private LoadingManager _loader;

    [Header("Config")]
    public int generation = 1;

    [Header("Menu Buttons")]
    public Button backToMenuBtn;
    public Button resetBtn;
    public Button hintTypeBtn;

    [Header("Dialogs")]
    public ConfirmDialog confirmDialog;
    private List<Pokemon> targetList = new();
    private readonly Dictionary<int, PokemonCard> cardById = new();
    private readonly HashSet<int> solved = new();
    private readonly HashSet<int> hinted = new();
    public IReadOnlyCollection<int> SolvedIds => solved;
    public IReadOnlyCollection<int> HintedIds => hinted;
    public IReadOnlyCollection<int> ShadowedIds => shadowed;
    public int CurrentQuizGeneration => generation;
    public string CurrentTypeFilter => selectedType;
    public float ElapsedSeconds => elapsed;
    public bool IsQuizRunning => running;
    public bool IsQuizFinished => IsComplete() || (finishedDialog && finishedDialog.IsShowing);
    public bool IsReadyForSavedMultiplayerSessionRestore => cardById.Count > 0;
    public bool HasPendingSavedMultiplayerSessionRestore =>
        _pendingSavedMultiplayerSession != null;

    private float elapsed;
    private bool running;
    private bool _processingNetworkGuess;
    private bool _suppressInputRefocus;
    public ScrollRect scrollRect;

    public Toast toast;
    private const int MIN_TOAST_LEN = 4;
    public MultiplayerGuessFeedback LastNetworkGuessFeedback { get; private set; }

    public enum MultiplayerGuessFeedbackKind
    {
        None = 0,
        NotInQuiz = 1,
        AlreadyGuessed = 2,
    }

    public readonly struct MultiplayerGuessFeedback
    {
        public readonly MultiplayerGuessFeedbackKind Kind;
        public readonly int PokemonId;
        public readonly string Message;
        public readonly float Duration;

        public bool HasValue => Kind != MultiplayerGuessFeedbackKind.None;

        public MultiplayerGuessFeedback(
            MultiplayerGuessFeedbackKind kind,
            int pokemonId,
            string message,
            float duration
        )
        {
            Kind = kind;
            PokemonId = pokemonId;
            Message = message ?? string.Empty;
            Duration = Mathf.Max(0.05f, duration);
        }

        public static MultiplayerGuessFeedback NotInQuiz(string message, float duration)
        {
            return new MultiplayerGuessFeedback(
                MultiplayerGuessFeedbackKind.NotInQuiz,
                0,
                message,
                duration
            );
        }

        public static MultiplayerGuessFeedback AlreadyGuessed(
            int pokemonId,
            string message,
            float duration
        )
        {
            return new MultiplayerGuessFeedback(
                MultiplayerGuessFeedbackKind.AlreadyGuessed,
                pokemonId,
                message,
                duration
            );
        }
    }

    const string KEY_PAUSE_ON_FOCUS_LOSS = "pause_on_focus_loss";
    const string KEY_BACKGROUND_COLOR_VALUE = "quiz_background_color_value";
    private const int BaseContentTopPadding = 32;
    private const int MultiplayerContentTopPadding = 32;
    private const float SingleplayerHeaderExtraHeight = 24f;
    private const float MultiplayerGridRightPaddingMin = 390f;
    private const float BackgroundColorSliderWidth = 153f;
    private const float BackgroundColorSliderHeight = 20f;
    private const float BackgroundColorSliderHandleSize = 14f;
    private const float BackgroundColorSliderHandleShadowSize = 18f;
    private const float BackgroundColorSliderRightPadding = 12f;
    private const float BackgroundColorSliderTopPadding = 94f;
    private const float BackgroundColorSliderMultiplayerRightPadding = 24f;
    private const float BackgroundColorSliderMultiplayerTopPadding = 174f;
    private const float BackgroundColorBlackStop = 0.08f;
    private const float BackgroundColorRainbowStart = 0.13f;
    bool _pauseOnFocusLossEnabled = true;

    public SectionHeader sectionHeaderPrefab;
    public SectionGroup sectionGroupPrefab;
    public Transform content;
    private int _buildToken;

    [Header("Dev/Test")]
    public Button testBtn;
    public bool testIncludeAliases;
    public float testDelay = 0.02f;

    [SerializeField]
    Toggle alwaysScrollToggle;
    Coroutine _scrollRoutine;
    Coroutine _scrollResetRoutine;
    Coroutine _localBuildRoutine;
    Coroutine _networkStateApplyRoutine;
    Coroutine _scrollRestoreRoutine;
    int _scrollToken;
    int _appliedContentTopPadding = -1;
    private RectTransform _scrollViewportRt;
    private RectTransform _scrollRectRt;
    private Vector2 _originalViewportOffsetMin;
    private Vector2 _originalViewportOffsetMax;
    private Vector2 _originalScrollRectOffsetMin;
    private Vector2 _originalScrollRectOffsetMax;
    private RectTransform _backgroundImageRt;
    private Vector2 _originalBackgroundImageOffsetMin;
    private Vector2 _originalBackgroundImageOffsetMax;
    private bool _viewportOffsetsCaptured;
    private bool _scrollRectOffsetsCaptured;
    private bool _backgroundImageOffsetsCaptured;
    private bool _multiplayerRightDockApplied;
    private bool _singleplayerHeaderExtraApplied;
    private bool _endStateBordersShowing;
    private Slider backgroundColorSlider;
    private Image scrollBackgroundImage;
    private Texture2D backgroundColorGradientTexture;
    private Sprite backgroundColorGradientSprite;
    private Texture2D backgroundColorHandleTexture;
    private Sprite backgroundColorHandleSprite;
    private readonly List<SectionGroup> _sections = new();
    int _hintUsedCount;
    int _shadowUsedCount;

    [SerializeField]
    string selectedType;
    string TypeDisplay =>
        string.IsNullOrEmpty(selectedType)
            ? null
            : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                selectedType.ToLowerInvariant()
            );
    private readonly Dictionary<int, List<Pokemon>> megaFormsByBase = new();
    private readonly Dictionary<int, Pokemon> megaSlotPickByBase = new();
    private readonly Dictionary<int, PokemonCard> megaCardByBase = new();
    private readonly Dictionary<string, List<int>> megaByBaseName = new();

    private readonly Dictionary<string, int> expeditionByBaseName = new();
    private readonly Dictionary<int, Pokemon> expeditionPickByBase = new();
    private readonly Dictionary<int, PokemonCard> expeditionCardByBase = new();
    private readonly Dictionary<int, Pokemon> pokemonById = new();
    private readonly Dictionary<int, Pokemon> gmaxPickByBase = new();

    private readonly Dictionary<string, int> gmaxByBaseName = new();
    private readonly Dictionary<int, PokemonCard> gmaxCardByBase = new();

    private readonly Dictionary<int, Pokemon> hisuiPickByBase = new();
    private readonly Dictionary<int, PokemonCard> hisuiCardByBase = new();
    private readonly Dictionary<string, int> hisuiByBaseName = new();
    private readonly Dictionary<int, Pokemon> regionalPickByBase = new();
    private readonly Dictionary<int, PokemonCard> regionalCardByBase = new();
    private readonly Dictionary<string, int> regionalByBaseName = new();
    private readonly Dictionary<int, Pokemon> lumiosePickByBase = new();
    private readonly Dictionary<int, Pokemon> hyperspacePickByBase = new();
    private readonly Dictionary<int, PokemonCard> lumioseCardByBase = new();
    private readonly Dictionary<int, PokemonCard> hyperspaceCardByBase = new();
    private readonly Dictionary<string, int> lumioseByBaseName = new();
    private readonly Dictionary<string, int> hyperspaceByBaseName = new();
    private readonly HashSet<int> shadowed = new();
    private SavedQuizSessionSnapshot _pendingSavedMultiplayerSession;
    private bool redirectingToMainMenu;

    [SerializeField]
    Toggle spellingHelpToggle;
    const string KEY_SPELLING_HELP = "spelling_help";
    bool _spellingHelpEnabled = true;
    const string KEY_ALWAYS_SCROLL = "always_scroll_to_pokemon";
    bool _alwaysScrollEnabled = true;
    private bool _testRunning;
    private bool _testCancel;
    private bool _localSessionDiscarded;

    private sealed class SavedQuizSessionSnapshot
    {
        public readonly List<int> SolvedIds;
        public readonly List<int> HintedIds;
        public readonly List<int> ShadowedIds;
        public readonly float Elapsed;
        public readonly bool Running;

        public SavedQuizSessionSnapshot(
            IReadOnlyList<int> solvedIds,
            IReadOnlyList<int> hintedIds,
            IReadOnlyList<int> shadowedIds,
            float elapsed,
            bool running
        )
        {
            SolvedIds = solvedIds != null ? new List<int>(solvedIds) : new List<int>();
            HintedIds = hintedIds != null ? new List<int>(hintedIds) : new List<int>();
            ShadowedIds = shadowedIds != null ? new List<int>(shadowedIds) : new List<int>();
            Elapsed = Mathf.Max(0f, elapsed);
            Running = running;
        }
    }

    [Header("Audio")]
    [SerializeField]
    AudioSource sfx;

    [SerializeField]
    private float keyboardScrollSpeed = 0.5f;

    [SerializeField]
    private float keyboardColumnRepeatDelay = 0.12f;

    private float _nextKeyboardColumnTime;

    [SerializeField]
    AudioClip correctSfx;

    [SerializeField]
    AudioClip duplicateSfx;

    [SerializeField]
    AudioClip backgroundMusic;

    [SerializeField, Range(0f, 1f)]
    float sfxVolume = 1f;

    [Header("Pause UI")]
    [SerializeField]
    PauseMenu pauseMenu;

    [SerializeField]
    CanvasGroup gridGroup;

    [SerializeField]
    Button pauseBtn;

    [SerializeField]
    Button shadowsBtn;
    private readonly List<int> _hintShadowOrder = new();

    private void Awake()
    {
        Application.runInBackground = true;
        if (ShouldRedirectAccidentalQuizStartup())
        {
            redirectingToMainMenu = true;
            SceneManager.LoadScene("MainMenu");
            return;
        }

        PokemonDatabase.Instance.LoadIfNeeded();
        var dupes = PokemonDatabase
            .Instance.All()
            .GroupBy(p => p.id)
            .Where(g => g.Count() > 1)
            .Select(g => new { id = g.Key, names = string.Join(", ", g.Select(p => p.name)) })
            .ToList();

        if (dupes.Count > 0)
        {
            Debug.LogError(
                "[PokemonDB] Duplicate IDs detected:\n"
                    + string.Join("\n", dupes.Select(d => $"{d.id}: {d.names}"))
            );
        }
        TypeIconLibrary.Instance.Preload();
        if (hintTypeBtn)
            hintTypeBtn.onClick.AddListener(RevealTypeHintForOne);

        if (guessInput)
        {
            guessInput.onValueChanged.AddListener(OnGuessChanged);
            guessInput.onSubmit.AddListener(_ =>
            {
                var txt = guessInput.text ?? string.Empty;
                OnGuessChanged(txt + " ");
            });
        }

        if (backToMenuBtn)
        {
            backToMenuBtn.onClick.RemoveAllListeners();
            backToMenuBtn.onClick.AddListener(OnBackToMenuClicked);
        }
        if (resetBtn)
        {
            resetBtn.onClick.RemoveAllListeners();
            resetBtn.onClick.AddListener(OnResetClicked);
        }
        if (confirmDialog)
        {
            bool wasActive = confirmDialog.gameObject.activeSelf;
            confirmDialog.gameObject.SetActive(true);

            confirmDialog.gameObject.SetActive(wasActive);
        }
        if (giveUpBtn)
        {
            giveUpBtn.onClick.RemoveAllListeners();
            giveUpBtn.onClick.AddListener(OnGiveUpClicked);
        }
        if (testBtn)
        {
            testBtn.onClick.RemoveAllListeners();
            testBtn.onClick.AddListener(() =>
            {
                if (!_testRunning)
                    StartCoroutine(CoAutoTypeAll());
                else
                    _testCancel = true;
            });
        }
        if (pauseBtn)
        {
            pauseBtn.onClick.RemoveAllListeners();
            pauseBtn.onClick.AddListener(TogglePause);
        }

        if (pauseMenu)
        {
            pauseMenu.OnResume = OnPauseMenuResume;
        }
        EnsureUIContracts();
    }

    int TotalGoal()
    {
        return (cardById != null && cardById.Count > 0) ? cardById.Count : targetList.Count;
    }

    bool IsComplete() => solved.Count >= TotalGoal();

    void PlayCorrect()
    {
        if (sfx && correctSfx)
            sfx.PlayOneShot(correctSfx, sfxVolume);
    }

    void PlayDuplicate()
    {
        if (sfx && duplicateSfx)
            sfx.PlayOneShot(duplicateSfx, sfxVolume);
    }

    public void OnShadowsButtonClicked()
    {
        if (IsDialogOpen())
            return;

        if (QuizMultiplayerCoordinator.RequestRevealShadow())
            return;

        RevealNextShadow();
    }

    void SetGridVisible(bool visible)
    {
        if (!gridGroup)
        {
            if (scrollRect)
                scrollRect.gameObject.SetActive(visible);
            return;
        }
        gridGroup.alpha = visible ? 1f : 0f;
        gridGroup.blocksRaycasts = visible;
        gridGroup.interactable = visible;
    }

    void PauseGame()
    {
        if (pauseMenu && pauseMenu.IsShowing)
            return;
        if (!running)
            return;

        ShowPauseUi();
    }

    void ShowPauseUi()
    {
        if (!pauseMenu)
            return;

        running = false;
        if (guessInput)
        {
            guessInput.DeactivateInputField();
            guessInput.interactable = false;
        }

        pauseMenu.SetElapsed(System.TimeSpan.FromSeconds(elapsed));

        SetGridVisible(false);
        pauseMenu.Show();
    }

    void ResumeFromPause()
    {
        if (pauseMenu)
            pauseMenu.Hide();
        SetGridVisible(true);

        if (!IsComplete())
            running = true;
        if (guessInput)
        {
            guessInput.interactable = true;
            guessInput.ActivateInputField();
            guessInput.Select();
        }
    }

    void OnPauseMenuResume()
    {
        if (QuizMultiplayerCoordinator.RequestPause(paused: false))
            return;

        ResumeFromPause();
    }

    void RefocusGuess()
    {
        if (_suppressInputRefocus)
            return;

        if (!guessInput)
            return;

        guessInput.SetTextWithoutNotify(string.Empty);

        SetSpellingHelp(null);

        if (!guessInput.interactable)
            guessInput.interactable = true;

        guessInput.ActivateInputField();
        guessInput.Select();
    }

    public void TogglePause()
    {
        if (!pauseMenu)
            return;

        if (QuizMultiplayerCoordinator.RequestPause(!pauseMenu.IsShowing))
            return;

        if (pauseMenu.IsShowing)
            ResumeFromPause();
        else
            PauseGame();
    }

    IEnumerator CoAutoTypeAll()
    {
        if (!guessInput)
            yield break;

        if (cardById == null || cardById.Count == 0)
        {
            BuildTargetList();
            RebuildGrid();
            yield return null;
        }

        _testRunning = true;
        _testCancel = false;
        running = true;
        if (giveUpBtn)
            giveUpBtn.interactable = true;
        guessInput.interactable = true;

        RefocusGuess();

        var testList = pokemonById.Values.Distinct().OrderBy(p => DexOrder.GetIndex(p)).ToList();

        int typed = 0;

        foreach (var p in testList)
        {
            if (_testCancel)
                break;
            if (solved.Contains(p.id))
                continue;

            yield return StartCoroutine(TypeAndCommit(p.name));
            typed++;

            if (p.baseId != 0 && p.baseId != p.id)
            {
                var baseMon = PokemonDatabase.Instance.All().FirstOrDefault(x => x.id == p.baseId);
                if (baseMon != null)
                    yield return StartCoroutine(TypeAndCommit(baseMon.name));
            }

            if (testIncludeAliases && p.aliases != null)
            {
                foreach (var a in p.aliases)
                {
                    if (_testCancel)
                        break;
                    yield return StartCoroutine(TypeAndCommit(a));
                    typed++;
                }
            }

            if (testDelay > 0f)
                yield return new WaitForSecondsRealtime(testDelay);
            if ((typed & 31) == 0)
                yield return null;
        }

        guessInput.ActivateInputField();
        guessInput.Select();
        _testRunning = false;
        _testCancel = false;
    }

    private void HandleKeyboardScroll()
    {
        if (!scrollRect || !scrollRect.content)
            return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = Keyboard.current;
        if (kb == null)
            return;

        float dir = 0f;

        if (kb.upArrowKey.isPressed)
            dir = 1f;
        else if (kb.downArrowKey.isPressed)
            dir = -1f;

        if (dir == 0f)
            return;
#else
        float dir = 0f;

        if (Input.GetKey(KeyCode.UpArrow))
            dir = 1f;
        else if (Input.GetKey(KeyCode.DownArrow))
            dir = -1f;

        if (dir == 0f)
            return;
#endif

        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
            scrollRect.verticalNormalizedPosition
                + dir * keyboardScrollSpeed * Time.unscaledDeltaTime
        );
        if (guessInput && guessInput.isFocused)
        {
            guessInput.caretPosition = guessInput.text.Length;
            guessInput.stringPosition = guessInput.text.Length;
        }
    }

    private void HandleKeyboardColumns()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = Keyboard.current;
        if (kb == null)
            return;

        bool left = kb.leftArrowKey.isPressed;
        bool right = kb.rightArrowKey.isPressed;
#else
        bool left = Input.GetKey(KeyCode.LeftArrow);
        bool right = Input.GetKey(KeyCode.RightArrow);
#endif

        if (!left && !right)
            return;

        // Keep the input caret locked to the end every frame while holding arrows.
        if (guessInput && guessInput.isFocused)
        {
            int end = guessInput.text?.Length ?? 0;
            guessInput.caretPosition = end;
            guessInput.stringPosition = end;
        }

        // Only change columns at repeat speed.
        if (Time.unscaledTime < _nextKeyboardColumnTime)
            return;

        int delta = left ? 1 : -1;

        currentCols = Mathf.Clamp(currentCols + delta, minColsLarge, maxColsSmall);
        PlayerPrefs.SetInt(KEY_COLS, currentCols);

        if (cardSizeSlider)
        {
            float t = Mathf.InverseLerp(maxColsSmall, minColsLarge, currentCols);
            cardSizeSlider.SetValueWithoutNotify(t);
        }

        ApplyColumnsToAllSections(preserveScroll: true);

        _nextKeyboardColumnTime = Time.unscaledTime + keyboardColumnRepeatDelay;
    }

    IEnumerator TypeAndCommit(string s)
    {
        var commit = s + " ";
        guessInput.text = commit;

        yield return null;

        if (testDelay > 0f)
            yield return new WaitForSecondsRealtime(testDelay);
        else
            yield return null;
    }

    void SetMainTitle(SectionGroup sec)
    {
        if (!string.IsNullOrEmpty(TypeDisplay))
        {
            var icon = TypeIconLibrary.Instance.Get(selectedType);
            sec.SetTitle($"All {TypeDisplay} types", isMain: true, icon: icon);
            return;
        }

        if (generation == 0)
            sec.SetTitle("Full Quiz (Gen 1–9)", isMain: true, icon: null);
        else
            sec.SetTitle(Helpers.GetGenTitle(generation), isMain: true, icon: null);
    }

    private void AttachBackgroundColorSlider(SectionGroup main)
    {
        if (!main || !main.headerRect)
            return;

        DestroyBackgroundColorSlider();
        var parent = GetBackgroundColorSliderParent(main);
        if (!parent)
            return;

        var root = new GameObject("Background Color Slider", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.transform.SetAsLastSibling();
        var rootRt = (RectTransform)root.transform;
        rootRt.sizeDelta = new Vector2(BackgroundColorSliderWidth, BackgroundColorSliderHeight);
        PositionBackgroundColorSlider(rootRt, parent != main.headerRect);

        var trackGo = new GameObject("Palette Track", typeof(RectTransform));
        trackGo.transform.SetParent(root.transform, false);
        var trackRt = (RectTransform)trackGo.transform;
        trackRt.anchorMin = new Vector2(0f, 0.5f);
        trackRt.anchorMax = new Vector2(1f, 0.5f);
        trackRt.pivot = new Vector2(0.5f, 0.5f);
        trackRt.sizeDelta = new Vector2(0f, 10f);
        trackRt.anchoredPosition = Vector2.zero;

        var trackImage = trackGo.AddComponent<Image>();
        trackImage.sprite = GetBackgroundColorGradientSprite();
        trackImage.type = Image.Type.Simple;

        var slideAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
        slideAreaGo.transform.SetParent(root.transform, false);
        var slideAreaRt = (RectTransform)slideAreaGo.transform;
        slideAreaRt.anchorMin = Vector2.zero;
        slideAreaRt.anchorMax = Vector2.one;
        float handleInset = BackgroundColorSliderHandleShadowSize * 0.5f;
        slideAreaRt.offsetMin = new Vector2(handleInset, 0f);
        slideAreaRt.offsetMax = new Vector2(-handleInset, 0f);

        var handleGo = new GameObject("Handle", typeof(RectTransform));
        handleGo.transform.SetParent(slideAreaGo.transform, false);
        var handleRt = (RectTransform)handleGo.transform;
        handleRt.sizeDelta = new Vector2(
            BackgroundColorSliderHandleShadowSize,
            BackgroundColorSliderHandleShadowSize
        );

        var shadowImage = handleGo.AddComponent<Image>();
        shadowImage.sprite = GetBackgroundColorHandleSprite();
        shadowImage.type = Image.Type.Simple;
        shadowImage.preserveAspect = true;
        shadowImage.color = new Color(0f, 0f, 0f, 0.58f);

        var handleFaceGo = new GameObject("Face", typeof(RectTransform));
        handleFaceGo.transform.SetParent(handleGo.transform, false);
        var handleFaceRt = (RectTransform)handleFaceGo.transform;
        handleFaceRt.anchorMin = handleFaceRt.anchorMax = new Vector2(0.5f, 0.5f);
        handleFaceRt.pivot = new Vector2(0.5f, 0.5f);
        handleFaceRt.anchoredPosition = Vector2.zero;
        handleFaceRt.sizeDelta = new Vector2(
            BackgroundColorSliderHandleSize,
            BackgroundColorSliderHandleSize
        );

        var handleImage = handleFaceGo.AddComponent<Image>();
        handleImage.sprite = GetBackgroundColorHandleSprite();
        handleImage.type = Image.Type.Simple;
        handleImage.preserveAspect = true;
        handleImage.color = Color.white;

        backgroundColorSlider = root.AddComponent<Slider>();
        backgroundColorSlider.minValue = 0f;
        backgroundColorSlider.maxValue = 1f;
        backgroundColorSlider.wholeNumbers = false;
        backgroundColorSlider.direction = Slider.Direction.LeftToRight;
        backgroundColorSlider.fillRect = null;
        backgroundColorSlider.handleRect = handleRt;
        backgroundColorSlider.targetGraphic = handleImage;
        backgroundColorSlider.SetValueWithoutNotify(CurrentLocalBackgroundHue());
        backgroundColorSlider.onValueChanged.AddListener(OnBackgroundColorSliderChanged);
    }

    private RectTransform GetBackgroundColorSliderParent(SectionGroup main)
    {
        var canvas = scrollRect ? scrollRect.GetComponentInParent<Canvas>() : null;
        if (!canvas && backgroundImage)
            canvas = backgroundImage.GetComponentInParent<Canvas>();
        if (canvas && canvas.transform is RectTransform canvasRt)
            return canvasRt;

        return main ? main.headerRect : null;
    }

    private void PositionBackgroundColorSlider(RectTransform sliderRt, bool anchoredToFixedCanvas)
    {
        if (!sliderRt)
            return;

        if (anchoredToFixedCanvas)
        {
            bool multiplayerUi = QuizNetworkRuntime.IsMultiplayerActive || GameSettings.IsMultiplayer;
            float rightPadding = multiplayerUi
                ? BackgroundColorSliderMultiplayerRightPadding
                : BackgroundColorSliderRightPadding;
            float topPadding = multiplayerUi
                ? BackgroundColorSliderMultiplayerTopPadding
                : BackgroundColorSliderTopPadding;

            sliderRt.anchorMin = sliderRt.anchorMax = new Vector2(1f, 1f);
            sliderRt.pivot = new Vector2(1f, 1f);
            sliderRt.anchoredPosition = new Vector2(-rightPadding, -topPadding);
            return;
        }

        sliderRt.anchorMin = sliderRt.anchorMax = new Vector2(1f, 0.5f);
        sliderRt.pivot = new Vector2(1f, 0.5f);
        sliderRt.anchoredPosition = new Vector2(-BackgroundColorSliderRightPadding, 0f);
    }

    private void DestroyBackgroundColorSlider()
    {
        if (!backgroundColorSlider)
            return;

        var sliderObject = backgroundColorSlider.gameObject;
        backgroundColorSlider = null;
        if (Application.isPlaying)
            Destroy(sliderObject);
        else
            DestroyImmediate(sliderObject);
    }

    private void OnBackgroundColorSliderChanged(float hue)
    {
        hue = Mathf.Clamp01(hue);
        ApplyLocalBackgroundColor(hue);
        PlayerPrefs.SetFloat(KEY_BACKGROUND_COLOR_VALUE, hue);
    }

    private void ApplySavedLocalBackgroundColor()
    {
        if (!PlayerPrefs.HasKey(KEY_BACKGROUND_COLOR_VALUE))
            return;

        ApplyLocalBackgroundColor(PlayerPrefs.GetFloat(KEY_BACKGROUND_COLOR_VALUE));
    }

    private void ApplyLocalBackgroundColor(float value)
    {
        value = Mathf.Clamp01(value);
        ApplyQuizBackgroundColor(BackgroundColorFromSliderValue(value));
        ApplyQuizTitleColor(IsBlackBackgroundSliderValue(value));
    }

    private float CurrentLocalBackgroundHue()
    {
        if (PlayerPrefs.HasKey(KEY_BACKGROUND_COLOR_VALUE))
            return Mathf.Clamp01(PlayerPrefs.GetFloat(KEY_BACKGROUND_COLOR_VALUE));

        var currentBackground = GetPrimaryQuizBackgroundImage();
        if (currentBackground)
        {
            Color.RGBToHSV(currentBackground.color, out float hue, out _, out float value);
            if (value <= 0.05f)
                return 0f;

            return Mathf.Lerp(BackgroundColorRainbowStart, 1f, Mathf.Clamp01(hue));
        }

        return 0.55f;
    }

    private static Color BackgroundColorFromSliderValue(float value)
    {
        value = Mathf.Clamp01(value);
        if (value <= BackgroundColorBlackStop)
            return Color.black;

        var firstColor = Color.HSVToRGB(0f, 0.42f, 1f);
        if (value < BackgroundColorRainbowStart)
        {
            float blend = Mathf.InverseLerp(
                BackgroundColorBlackStop,
                BackgroundColorRainbowStart,
                value
            );
            return Color.Lerp(Color.black, firstColor, blend);
        }

        float hue = Mathf.InverseLerp(BackgroundColorRainbowStart, 1f, value);
        return Color.HSVToRGB(hue, 0.42f, 1f);
    }

    private static bool IsBlackBackgroundSliderValue(float value)
    {
        return Mathf.Clamp01(value) <= BackgroundColorBlackStop;
    }

    private bool ShouldUseLightQuizTitles()
    {
        return IsBlackBackgroundSliderValue(CurrentLocalBackgroundHue());
    }

    private void ApplyQuizTitleColor(bool useLightTitles)
    {
        Color titleColor = useLightTitles ? Color.white : Color.black;
        foreach (var sec in _sections)
        {
            if (sec)
                sec.SetTitleColor(titleColor);
        }
    }

    private void ApplyQuizBackgroundColor(Color color)
    {
        foreach (var image in EnumerateQuizBackgroundImages())
            image.color = color;
    }

    private Image GetPrimaryQuizBackgroundImage()
    {
        if (scrollRect)
        {
            if (scrollRect.viewport && scrollRect.viewport.TryGetComponent(out Image viewportImage))
                return viewportImage;

            if (scrollRect.TryGetComponent(out Image scrollImage))
                return scrollImage;

            if (content && content.TryGetComponent(out Image contentImage))
                return contentImage;
        }

        return backgroundImage;
    }

    private IEnumerable<Image> EnumerateQuizBackgroundImages()
    {
        var seen = new HashSet<Image>();

        void Add(Image image)
        {
            if (image)
                seen.Add(image);
        }

        Add(backgroundImage);

        if (scrollRect)
        {
            if (scrollRect.TryGetComponent(out Image scrollImage))
                Add(scrollImage);

            if (scrollRect.viewport && scrollRect.viewport.TryGetComponent(out Image viewportImage))
                Add(viewportImage);

            if (content && content.TryGetComponent(out Image contentImage))
                Add(contentImage);

            if (seen.Count <= (backgroundImage ? 1 : 0))
                Add(EnsureScrollBackgroundImage());
        }

        foreach (var image in seen)
            yield return image;
    }

    private Image EnsureScrollBackgroundImage()
    {
        if (scrollBackgroundImage)
            return scrollBackgroundImage;

        RectTransform host = scrollRect
            ? scrollRect.viewport ?? scrollRect.transform as RectTransform
            : null;
        if (!host)
            return null;

        scrollBackgroundImage = host.gameObject.GetComponent<Image>();
        if (!scrollBackgroundImage)
            scrollBackgroundImage = host.gameObject.AddComponent<Image>();

        scrollBackgroundImage.raycastTarget = true;
        return scrollBackgroundImage;
    }

    private Sprite GetBackgroundColorGradientSprite()
    {
        if (backgroundColorGradientSprite)
            return backgroundColorGradientSprite;

        const int width = 192;
        backgroundColorGradientTexture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

        for (int x = 0; x < width; x++)
        {
            float hue = width <= 1 ? 0f : x / (float)(width - 1);
            backgroundColorGradientTexture.SetPixel(x, 0, BackgroundColorFromSliderValue(hue));
        }

        backgroundColorGradientTexture.Apply();
        backgroundColorGradientSprite = Sprite.Create(
            backgroundColorGradientTexture,
            new Rect(0f, 0f, width, 1f),
            new Vector2(0.5f, 0.5f),
            100f
        );
        backgroundColorGradientSprite.hideFlags = HideFlags.HideAndDontSave;
        return backgroundColorGradientSprite;
    }

    private Sprite GetBackgroundColorHandleSprite()
    {
        if (backgroundColorHandleSprite)
            return backgroundColorHandleSprite;

        const int size = 32;
        backgroundColorHandleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

        float center = (size - 1) * 0.5f;
        float radius = center;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                backgroundColorHandleTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        backgroundColorHandleTexture.Apply();
        backgroundColorHandleSprite = Sprite.Create(
            backgroundColorHandleTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f
        );
        backgroundColorHandleSprite.hideFlags = HideFlags.HideAndDontSave;
        return backgroundColorHandleSprite;
    }

    private void DestroyBackgroundColorGradient()
    {
        if (backgroundColorGradientSprite)
            Destroy(backgroundColorGradientSprite);
        if (backgroundColorGradientTexture)
            Destroy(backgroundColorGradientTexture);
        if (backgroundColorHandleSprite)
            Destroy(backgroundColorHandleSprite);
        if (backgroundColorHandleTexture)
            Destroy(backgroundColorHandleTexture);

        backgroundColorGradientSprite = null;
        backgroundColorGradientTexture = null;
        backgroundColorHandleSprite = null;
        backgroundColorHandleTexture = null;
    }

    private bool IsDialogOpen()
    {
        if (!confirmDialog)
            return false;

        return confirmDialog.gameObject.activeInHierarchy && confirmDialog.IsShowing;
    }

    private static int BaseIdOf(Pokemon p) => p.baseId != 0 ? p.baseId : p.id;

    void EnsureLoader()
    {
        if (_loader && _loader.gameObject.scene.IsValid())
            return;

        if (LoadingManager.Instance)
        {
            _loader = LoadingManager.Instance;
            return;
        }

        if (loaderPrefab)
        {
            _loader = Instantiate(loaderPrefab);
            _loader.gameObject.SetActive(true);
            return;
        }

        Debug.LogWarning("No LoadingManager instance or prefab assigned; loader UI will not show.");
    }

    private void EnsureUIContracts()
    {
        if (!content)
            content = scrollRect ? scrollRect.content : null;
        if (!content)
            return;

        var crt = content as RectTransform;
        var vlg = crt.GetOrAdd<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = 16f;
        _appliedContentTopPadding = BaseContentTopPadding;
        vlg.padding = new RectOffset(
            vlg.padding.left,
            vlg.padding.right,
            BaseContentTopPadding,
            vlg.padding.bottom
        );
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = crt.GetOrAdd<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void Start()
    {
        if (redirectingToMainMenu)
            return;

        if (GameSettings.Generation.HasValue)
            generation = GameSettings.Generation.Value;

        if (GameSettings.TypeFilter != null && GameSettings.TypeFilter.Length > 0)
        {
            selectedType = GameSettings.TypeFilter[0].Trim().ToLowerInvariant();
            generation = 0;

            if (backgroundImage && GameSettings.TypeBgColor.HasValue)
                backgroundImage.color = GameSettings.TypeBgColor.Value;
        }
        else
        {
            selectedType = null;
        }

        ApplySavedLocalBackgroundColor();
        _pauseOnFocusLossEnabled = PlayerPrefs.GetInt(KEY_PAUSE_ON_FOCUS_LOSS, 1) == 1;

        EnsureLoader();
        SetSpellingHelp(null);
        bool loaderIsBuildingThisScene =
            LoadingManager.Instance && LoadingManager.Instance.IsLoading;

        if (!loaderIsBuildingThisScene)
            _localBuildRoutine = StartCoroutine(LocalBuildWithOverlay());
        ResetTimerOnly();
        running = true;
        guessInput?.ActivateInputField();
        if (giveUpBtn)
            giveUpBtn.interactable = true;
        currentCols = PlayerPrefs.GetInt(
            KEY_COLS,
            Mathf.RoundToInt((minColsLarge + maxColsSmall) * 0.5f)
        );

        if (cardSizeSlider)
        {
            float t = Mathf.InverseLerp(maxColsSmall, minColsLarge, currentCols);
            cardSizeSlider.SetValueWithoutNotify(t);
            cardSizeSlider.onValueChanged.AddListener(OnCardSizeSliderChanged);
        }

        ApplyColumnsToAllSections();
        _spellingHelpEnabled = PlayerPrefs.GetInt(KEY_SPELLING_HELP, 1) == 1;

        if (spellingHelpToggle)
        {
            spellingHelpToggle.SetIsOnWithoutNotify(_spellingHelpEnabled);
            spellingHelpToggle.onValueChanged.RemoveAllListeners();
            spellingHelpToggle.onValueChanged.AddListener(OnSpellingHelpToggleChanged);
        }

        ApplySpellingHelpState();
        _alwaysScrollEnabled = PlayerPrefs.GetInt(KEY_ALWAYS_SCROLL, 1) == 1;

        if (alwaysScrollToggle)
        {
            alwaysScrollToggle.SetIsOnWithoutNotify(_alwaysScrollEnabled);
            alwaysScrollToggle.onValueChanged.RemoveAllListeners();
            alwaysScrollToggle.onValueChanged.AddListener(OnAlwaysScrollToggleChanged);
        }
    }

    private static bool ShouldRedirectAccidentalQuizStartup()
    {
#if UNITY_EDITOR
        GameSettings.ConsumeQuizLaunchArm();
        return false;
#else
        if (GameSettings.ConsumeQuizLaunchArm())
            return false;

        return !GameSettings.IsMultiplayer && !QuizNetworkRuntime.IsMultiplayerActive;
#endif
    }

    void OnAlwaysScrollToggleChanged(bool on)
    {
        _alwaysScrollEnabled = on;
        PlayerPrefs.SetInt(KEY_ALWAYS_SCROLL, on ? 1 : 0);
    }

    private void OnApplicationQuit()
    {
        SaveLocalQuizSession();
        PlayerPrefs.Save();
    }

    void OnPauseOnFocusLossToggleChanged(bool on)
    {
        _pauseOnFocusLossEnabled = on;
        PlayerPrefs.SetInt(KEY_PAUSE_ON_FOCUS_LOSS, on ? 1 : 0);
    }

    void OnSpellingHelpToggleChanged(bool on)
    {
        _spellingHelpEnabled = on;
        PlayerPrefs.SetInt(KEY_SPELLING_HELP, on ? 1 : 0);
        ApplySpellingHelpState();
    }

    void ApplySpellingHelpState()
    {
        if (!_spellingHelpEnabled)
            SetSpellingHelp(null);
    }

    IEnumerator LocalBuildWithOverlay()
    {
        string title;
        if (!string.IsNullOrEmpty(TypeDisplay))
            title = $"Loading {TypeDisplay} type quiz…";
        else if (generation == 0)
            title = "Loading Full Quiz…";
        else
            title = $"Loading {Helpers.GetGenTitle(generation)}…";

        _loader?.Show(title, immediate: true);

        yield return StartCoroutine(
            BuildWithExternalProgress(t => _loader?.SetProgress(t), 0f, 1f)
        );

        _loader?.Hide();
        _localBuildRoutine = null;
    }

    private void DefocusUI()
    {
        if (guessInput && guessInput.isFocused)
            guessInput.DeactivateInputField();

        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            if (IsDialogOpen())
                return;

            TogglePause();
        }

        if (kb != null && kb.pKey.wasPressedThisFrame && !IsTextInputFocused())
        {
            if (IsDialogOpen())
                return;

            TogglePause();
        }
#else
        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
        {
            if (IsDialogOpen())
                return;
            TogglePause();
        }

        if (UnityEngine.Input.GetKeyDown(KeyCode.P) && !IsTextInputFocused())
        {
            if (IsDialogOpen())
                return;
            TogglePause();
        }
#endif

        if (!IsDialogOpen() && running)
        {
            if (!QuizMultiplayerCoordinator.IsClientOnly)
                elapsed += Time.deltaTime;

            SetTimerText();
        }
        if (!IsDialogOpen())
        {
            HandleKeyboardScroll();
            HandleKeyboardColumns();
        }
    }

    private bool IsTextInputFocused()
    {
        if (guessInput && guessInput.isFocused)
            return true;

        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (!selected)
            return false;

        var input = selected.GetComponentInParent<TMP_InputField>();
        return input && input.interactable && input.isFocused;
    }

    private void RevealByBaseIds(params int[] baseIds)
    {
        bool any = false;
        foreach (var baseId in baseIds)
        {
            var target = targetList.FirstOrDefault(p =>
                (p.baseId != 0 ? p.baseId : p.id) == baseId
            );
            if (target == null)
                continue;

            if (solved.Contains(target.id))
            {
                if (cardById.TryGetValue(target.id, out var already))
                {
                    already.FlashHighlight();
                }
                continue;
            }

            solved.Add(target.id);
            if (cardById.TryGetValue(target.id, out var card))
            {
                card.Reveal();
                MaybeScrollTo(target);
            }

            any = true;
        }

        if (any)
        {
            UpdateScore();
            SaveLocalQuizSession();
        }

        RefocusGuess();

        ShowFinishedIfComplete();
    }

    private void ShowNotInQuiz(string name, Pokemon p = null)
    {
        string message = BuildNotInQuizMessage(name, p, out float duration);
        CaptureNetworkGuessFeedback(MultiplayerGuessFeedback.NotInQuiz(message, duration));

        toast.Show(message, duration);

        RefocusGuess();
    }

    private string BuildNotInQuizMessage(string name, Pokemon p, out float duration)
    {
        string reason = ExplainWhyNotInQuiz(p);
        duration = string.IsNullOrEmpty(reason) ? 2f : 2.5f;

        if (!string.IsNullOrEmpty(reason))
            return $"{name} is not part of this quiz - {reason}";

        return $"{name} is not part of this quiz";
    }

    private void CaptureAlreadyGuessedFeedback(Pokemon target)
    {
        if (target == null)
            return;

        var feedback = MultiplayerGuessFeedback.AlreadyGuessed(
            target.id,
            $"{target.name} was already guessed",
            1.6f
        );
        CaptureNetworkGuessFeedback(feedback);

        if (toast)
            toast.Show(feedback.Message, feedback.Duration);
    }

    private void CaptureNetworkGuessFeedback(MultiplayerGuessFeedback feedback)
    {
        if (_processingNetworkGuess && feedback.HasValue)
            LastNetworkGuessFeedback = feedback;
    }

    void MaybeScrollTo(Pokemon p, float duration = 0.25f)
    {
        if (!scrollRect || !scrollRect.content || !scrollRect.viewport)
            return;
        if (p == null)
            return;
        if (!cardById.TryGetValue(p.id, out var card) || !card)
            return;

        if (!_alwaysScrollEnabled)
            return;

        if (_scrollRoutine != null)
            StopCoroutine(_scrollRoutine);

        _scrollRoutine = StartCoroutine(
            CoSmartScrollTo(card.GetComponent<RectTransform>(), duration, ++_scrollToken)
        );
    }

    public void OnResetClicked()
    {
        if (QuizNetworkRuntime.IsMultiplayerClientOnly)
        {
            ApplyMultiplayerUiState();
            return;
        }
        if (resetBtn && !resetBtn.interactable)
            return;

        DefocusUI();

        void DoReset()
        {
            if (QuizMultiplayerCoordinator.RequestReset())
                return;

            ClearLocalQuizSession();
            ResetGame();
        }

        if (!confirmDialog)
        {
            DoReset();
            return;
        }

        confirmDialog.Show(
            title: "Reset quiz?",
            message: "This will clear all revealed Pokémon and restart the timer.",
            confirmLabel: "Reset",
            cancelLabel: "Cancel",
            confirmAction: DoReset
        );
    }

    private Pokemon MapToTargetSpecies(Pokemon guess)
    {
        if (guess == null)
            return null;

        if (cardById.ContainsKey(guess.id))
            return guess;
        {
            int baseId = guess.baseId != 0 ? guess.baseId : guess.id;

            if (
                generation == 0
                && HasTypeFilter
                && megaSlotPickByBase.TryGetValue(baseId, out var typeMegaPick)
                && cardById.ContainsKey(typeMegaPick.id)
            )
            {
                return typeMegaPick;
            }

            if (
                (generation == 0 || generation == 10)
                && megaSlotPickByBase.TryGetValue(baseId, out var megaPick)
            )
                return megaPick;
            if (
                (generation == 8 || generation == 0)
                && gmaxPickByBase.TryGetValue(baseId, out var gmaxPick)
            )
                return gmaxPick;
            if (
                (generation == 8 || generation == 0)
                && hisuiPickByBase.TryGetValue(baseId, out var hisuiPick)
            )
                return hisuiPick;
            if (
                (generation == 9 || generation == 0)
                && expeditionPickByBase.TryGetValue(baseId, out var expPick)
            )
                return expPick;
            if (
                (generation == 9 || generation == 0)
                && lumiosePickByBase.TryGetValue(baseId, out var lumiosePick)
            )
                return lumiosePick;

            if (
                (generation == 9 || generation == 0)
                && hyperspacePickByBase.TryGetValue(baseId, out var hyperspacePick)
            )
                return hyperspacePick;

            var regional = targetList.FirstOrDefault(p =>
                Helpers.IsRegionalForm(p) && ((p.baseId != 0 ? p.baseId : p.id) == baseId)
            );
            if (regional != null && cardById.ContainsKey(regional.id))
                return regional;

            var baseEntry = targetList.FirstOrDefault(p =>
                !Helpers.IsMega(p) && ((p.baseId != 0 ? p.baseId : p.id) == baseId)
            );
            if (baseEntry != null && cardById.ContainsKey(baseEntry.id))
                return baseEntry;
        }

        bool isForm =
            Helpers.IsMega(guess)
            || Helpers.IsGmax(guess)
            || Helpers.IsHisui(guess)
            || (
                typeof(Helpers).GetMethod("IsRegionalForm") != null && Helpers.IsRegionalForm(guess)
            )
            || Helpers.IsPaldeaExpeditionOrBloodmoon(guess)
            || Helpers.IsLumioseMega(guess)
            || Helpers.IsHyperspaceMega(guess);

        if (isForm)
        {
            int baseId = guess.baseId != 0 ? guess.baseId : guess.id;

            if (
                (generation == 0 || generation == 10)
                && megaSlotPickByBase.TryGetValue(baseId, out var megaPick)
            )
                return megaPick;

            if (
                (generation == 9 || generation == 0)
                && expeditionPickByBase.TryGetValue(baseId, out var expPick)
            )
                return expPick;

            var baseEntry = targetList.FirstOrDefault(p =>
                !Helpers.IsMega(p) && ((p.baseId != 0 ? p.baseId : p.id) == baseId)
            );

            if (baseEntry != null && cardById.ContainsKey(baseEntry.id))
                return baseEntry;
        }

        return null;
    }

    public void OnBackToMenuClicked()
    {
        DefocusUI();

        void LeaveNow()
        {
            if (QuizNetworkRuntime.IsMultiplayerServer)
                QuizMultiplayerCoordinator.SaveCurrentQuizSessionForLobby(this);

            if (QuizMultiplayerCoordinator.RequestReturnToMenu())
                return;

            LoadingManager.Instance?.CancelLoad();
            if (QuizNetworkRuntime.IsMultiplayerClientOnly)
            {
                QuizMultiplayerCoordinator.NotifyLocalPlayerLeavingQuiz();
                QuizNetworkRuntime.ReturnToLobbyMenu(keepActiveQuizSelection: true);
                SceneManager.LoadScene("MainMenu");
                return;
            }

            SaveLocalQuizSession();
            QuizNetworkRuntime.Shutdown();
            SceneManager.LoadScene("MainMenu");
        }

        if (!confirmDialog)
        {
            LeaveNow();
            return;
        }

        var prompt = BuildBackToMenuPrompt();
        confirmDialog.Show(
            title: prompt.title,
            message: prompt.message,
            confirmLabel: prompt.confirmLabel,
            cancelLabel: "Stay",
            confirmAction: LeaveNow
        );
    }

    private (string title, string message, string confirmLabel) BuildBackToMenuPrompt()
    {
        bool finished = IsComplete() || (finishedDialog && finishedDialog.IsShowing);

        if (QuizNetworkRuntime.IsMultiplayerClientOnly)
        {
            return (
                "Go to main menu?",
                "You will leave this quiz screen. The host's co-op session stays active, and you can return from the main menu while the host keeps it open.",
                "Go to menu"
            );
        }

        if (QuizNetworkRuntime.IsMultiplayerServer)
        {
            string message = finished
                ? "This quiz is finished. Return both players to the main menu? The co-op lobby and code will stay active."
                : "This closes the current quiz for both players. The co-op lobby and code will stay active for choosing another quiz.";
            return ("Return to lobby menu?", message, "Return");
        }

        if (finished)
            return ("Go to main menu?", "This quiz is finished. Go back to the main menu?", "Go to menu");

        return (
            "Go to main menu?",
            "Your current solo quiz progress will be saved until you restart the game. Go back to the main menu?",
            "Go to menu"
        );
    }

    private bool TryAcceptExpeditionByBaseName(string text, bool commit)
    {
        if (generation != 9 && generation != 0)
            return false;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var k = GuessNormalizer.Key(text);
        var hit = expeditionByBaseName.TryGetValue(k, out var baseId);

        if (!hit)
            return false;
        if (!expeditionPickByBase.TryGetValue(baseId, out var pick))
            return false;

        HandleCandidate(pick, text, commit);
        return true;
    }

    private bool TryAcceptAnyMegaByBaseName(string text, bool commit)
    {
        if (generation != 10 || string.IsNullOrWhiteSpace(text))
            return false;

        var k = GuessNormalizer.Key(text);

        var baseSpecies = PokemonDatabase
            .Instance.All()
            .FirstOrDefault(p =>
                (p.baseId == 0 || p.baseId == p.id)
                && (
                    GuessNormalizer.Key(p.name) == k
                    || (p.aliases != null && p.aliases.Any(a => GuessNormalizer.Key(a) == k))
                )
            );

        if (baseSpecies == null)
            return false;

        int baseId = baseSpecies.baseId != 0 ? baseSpecies.baseId : baseSpecies.id;

        var matches = pokemonById
            .Values.Where(p =>
                cardById.ContainsKey(p.id)
                && (Helpers.IsMega(p) || Helpers.IsLumioseMega(p) || Helpers.IsHyperspaceMega(p))
                && (p.baseId != 0 ? p.baseId : p.id) == baseId
            )
            .ToList();

        if (matches.Count == 0)
            return false;

        if (!commit)
            return true;

        bool anyNew = false;

        foreach (var p in matches)
        {
            if (solved.Contains(p.id))
                continue;

            solved.Add(p.id);

            if (cardById.TryGetValue(p.id, out var card))
            {
                card.Reveal();
                MaybeScrollTo(p);
            }

            OnPokemonSolved?.Invoke(p);
            PlayCorrect();
            anyNew = true;
        }

        if (!anyNew)
        {
            var target = matches.FirstOrDefault();
            CaptureAlreadyGuessedFeedback(target);
            if (target != null && cardById.TryGetValue(target.id, out var already))
            {
                already.FlashHighlight();
                MaybeScrollTo(target);
            }
            PlayDuplicate();
            RefocusGuess();
            return true;
        }

        UpdateScore();
        SaveLocalQuizSession();
        RefocusGuess();
        ShowFinishedIfComplete();

        return true;
    }

    private static readonly Dictionary<int, string> GenTitles = new()
    {
        { 1, "Kanto (Gen 1)" },
        { 2, "Johto (Gen 2)" },
        { 3, "Hoenn (Gen 3)" },
        { 4, "Sinnoh (Gen 4)" },
        { 5, "Unova (Gen 5)" },
        { 6, "Kalos (Gen 6)" },
        { 7, "Alola (Gen 7)" },
        { 8, "Galar (Gen 8)" },
        { 9, "Paldea (Gen 9)" },
    };

    private void OnDisable()
    {
        SaveLocalQuizSession();
        StopAllCoroutines();
        PlayerPrefs.Save();
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        DestroyBackgroundColorSlider();
        DestroyBackgroundColorGradient();
    }

    private void RebuildGrid()
    {
        _fits.Clear();
        _sections.Clear();
        _buildToken++;
        _hintUsedCount = 0;
        _shadowUsedCount = 0;
        CancelGridTransientCoroutines();
        if (!scrollRect || !scrollRect.viewport)
        {
            Debug.LogError("ScrollRect/Viewport missing");
            return;
        }
        if (!sectionGroupPrefab || !cardPrefab || !content)
        {
            Debug.LogError("Missing prefabs/refs");
            return;
        }

        var contentRt = (RectTransform)content;
        var vlg =
            contentRt.GetComponent<VerticalLayoutGroup>()
            ?? contentRt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 24;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var csf =
            contentRt.GetComponent<ContentSizeFitter>()
            ?? contentRt.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (Transform c in content)
            Destroy(c.gameObject);
        DestroyBackgroundColorSlider();
        cardById.Clear();
        pokemonById.Clear();
        solved.Clear();
        hinted.Clear();
        shadowed.Clear();
        _endStateBordersShowing = false;
        megaFormsByBase.Clear();
        finishedDialog.Hide();
        megaSlotPickByBase.Clear();
        megaCardByBase.Clear();
        expeditionByBaseName.Clear();
        expeditionPickByBase.Clear();
        expeditionCardByBase.Clear();
        gmaxPickByBase.Clear();
        gmaxCardByBase.Clear();
        gmaxByBaseName.Clear();
        hisuiPickByBase.Clear();
        hisuiCardByBase.Clear();
        hisuiByBaseName.Clear();
        regionalPickByBase.Clear();
        regionalCardByBase.Clear();
        regionalByBaseName.Clear();
        lumiosePickByBase.Clear();
        lumioseCardByBase.Clear();
        lumioseByBaseName.Clear();
        hyperspacePickByBase.Clear();
        hyperspaceCardByBase.Clear();
        hyperspaceByBaseName.Clear();
        _hintShadowOrder.Clear();

        var ordered = targetList;

        var main = Instantiate(sectionGroupPrefab, content);
        main.EnsureLayout();
        SetMainTitle(main);
        AttachBackgroundColorSlider(main);
        _sections.Add(main);

        SectionGroup megaKalosGen = null,
            megaHoennGen = null,
            paldeaExpeditions = null,
            gmaxSec = null,
            hisuiSec = null,
            lumioseMegasSec = null,
            hyperspaceMegasSec = null,
            alolaUnknown = null;

        var allDb = PokemonDatabase.Instance.All();

        foreach (var m in allDb.Where(Helpers.IsMega).Where(MatchesType))
        {
            if (Helpers.IsLumioseMega(m) || Helpers.IsHyperspaceMega(m))
                continue;
            int baseKey = BaseIdOf(m);
            if (!megaFormsByBase.TryGetValue(baseKey, out var list))
                megaFormsByBase[baseKey] = list = new List<Pokemon>();
            list.Add(m);
        }
        megaByBaseName.Clear();
        foreach (var kv in megaFormsByBase)
        {
            var baseId = kv.Key;
            var megaIds = kv.Value.Select(m => m.id).ToList();

            var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
            void AddBaseKey(string s)
            {
                var k = GuessNormalizer.Key(s);
                if (!string.IsNullOrEmpty(k))
                    megaByBaseName[k] = megaIds;
            }

            if (baseMon != null)
            {
                AddBaseKey(baseMon.name);
                if (baseMon.aliases != null)
                    foreach (var a in baseMon.aliases)
                        AddBaseKey(a);
            }
        }
        var g9ExpPoolF = allDb
            .Where(Helpers.IsPaldeaExpeditionOrBloodmoon)
            .Where(MatchesType)
            .ToList();
        var gmaxPoolF = allDb
            .Where(Helpers.IsGmax)
            .Where(MatchesType)
            .OrderBy(p => DexOrder.GetIndex(p))
            .ToList();
        var hisuiPoolF = allDb
            .Where(Helpers.IsHisui)
            .Where(MatchesType)
            .OrderBy(p => DexOrder.GetIndex(p))
            .ToList();
        var lumiosePoolF = allDb.Where(Helpers.IsLumioseMega).Where(MatchesType).ToList();
        var hyperspacePoolF = allDb.Where(Helpers.IsHyperspaceMega).Where(MatchesType).ToList();

        if (generation == 0)
        {
            var mainByGen = new Dictionary<int, SectionGroup>();
            SectionGroup megaKalos = null;
            SectionGroup megaHoenn = null;
            SectionGroup gen9Expeditions = null;
            SectionGroup fullGmax = null;
            SectionGroup fullHisui = null;
            SectionGroup fullLumioseMegas = null;
            SectionGroup unknownSec = null;
            SectionGroup fullHyperspaceMegas = null;

            foreach (var g in ordered.Select(p => p.generation).Distinct().OrderBy(x => x))
            {
                var sec = Instantiate(sectionGroupPrefab, content);
                sec.EnsureLayout();
                string baseTitle = GenTitles.TryGetValue(g, out var t) ? t : $"Gen {g}";
                sec.SetTitle(baseTitle, false);
                mainByGen[g] = sec;
                _sections.Add(sec);

                if (g == 6)
                {
                    megaKalos = Instantiate(sectionGroupPrefab, content);
                    megaKalos.EnsureLayout();
                    megaKalos.SetTitle("Mega Evolution - Kalos", false);
                    _sections.Add(megaKalos);

                    megaHoenn = Instantiate(sectionGroupPrefab, content);
                    megaHoenn.EnsureLayout();
                    megaHoenn.SetTitle("Mega Evolution - Hoenn", false);
                    _sections.Add(megaHoenn);
                }
                if (g == 7)
                {
                    unknownSec = Instantiate(sectionGroupPrefab, content);
                    unknownSec.EnsureLayout();
                    unknownSec.SetTitle("Unknown", false);
                    _sections.Add(unknownSec);
                }
                if (g == 8)
                {
                    if (gmaxPoolF.Count > 0)
                    {
                        fullGmax = Instantiate(sectionGroupPrefab, content);
                        fullGmax.EnsureLayout();
                        fullGmax.SetTitle("Gigantamax (Gen 8)", false);
                        _sections.Add(fullGmax);
                    }
                    if (hisuiPoolF.Count > 0)
                    {
                        fullHisui = Instantiate(sectionGroupPrefab, content);
                        fullHisui.EnsureLayout();
                        fullHisui.SetTitle("Hisui (Gen 8)", false);
                        _sections.Add(fullHisui);
                    }
                }
                if (g == 9 & g9ExpPoolF.Count > 0)
                {
                    gen9Expeditions = Instantiate(sectionGroupPrefab, content);
                    gen9Expeditions.EnsureLayout();
                    gen9Expeditions.SetTitle("Paldea Expeditions", false);
                    _sections.Add(gen9Expeditions);
                }
                if (g == 9 && lumiosePoolF.Count > 0)
                {
                    fullLumioseMegas = Instantiate(sectionGroupPrefab, content);
                    fullLumioseMegas.EnsureLayout();
                    fullLumioseMegas.SetTitle("Mega Evolution - Lumiose", false);
                    _sections.Add(fullLumioseMegas);
                }
                if (g == 9 && hyperspacePoolF.Count > 0)
                {
                    fullHyperspaceMegas = Instantiate(sectionGroupPrefab, content);
                    fullHyperspaceMegas.EnsureLayout();
                    fullHyperspaceMegas.SetTitle("Mega Evolution - Hyperspace", false);
                    _sections.Add(fullHyperspaceMegas);
                }
            }

            foreach (var p in ordered)
            {
                if (p.id == 808 || p.id == 809)
                    continue;
                var sec = mainByGen[p.generation];
                var card = Instantiate(cardPrefab, sec.gridRoot);
                card.ClearEndState();
                card.Bind(p);
                cardById[p.id] = card;
                pokemonById[p.id] = p;
                _hintShadowOrder.Add(p.id);
                if (Helpers.IsRegionalForm(p))
                {
                    int baseId = p.baseId != 0 ? p.baseId : p.id;
                    regionalPickByBase[baseId] = p;
                    regionalCardByBase[baseId] = card;

                    var all = PokemonDatabase.Instance.All();
                    var baseMon = all.FirstOrDefault(x => x.id == baseId);
                    void AddKey(string s)
                    {
                        var k = GuessNormalizer.Key(s);
                        if (!string.IsNullOrEmpty(k))
                            regionalByBaseName[k] = baseId;
                    }
                    if (baseMon != null)
                    {
                        AddKey(baseMon.name);
                        if (baseMon.aliases != null)
                            foreach (var a in baseMon.aliases)
                                AddKey(a);
                    }
                }
            }
            if ((megaKalos != null || megaHoenn != null) && megaFormsByBase.Count > 0)
            {
                var rng = new System.Random();

                foreach (var kv in megaFormsByBase)
                {
                    int baseId = kv.Key;
                    var forms = kv.Value;
                    var pick = forms[rng.Next(forms.Count)];

                    var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                    int baseGen = baseMon?.generation ?? pick.generation;

                    SectionGroup targetSec;

                    if (baseGen == 3)
                        targetSec = megaHoenn;
                    else
                        targetSec = megaKalos;

                    if (targetSec == null)
                        continue;

                    var c = Instantiate(cardPrefab, targetSec.gridRoot);
                    c.ClearEndState();
                    c.Bind(pick);

                    megaSlotPickByBase[baseId] = pick;
                    megaCardByBase[baseId] = c;
                    cardById[pick.id] = c;
                    pokemonById[pick.id] = pick;
                    _hintShadowOrder.Add(pick.id);
                }
            }

            if (gen9Expeditions != null)
            {
                var expOrdered = g9ExpPoolF.OrderBy(p => DexOrder.GetIndex(p)).ToList();

                int iBlood = expOrdered.FindIndex(x => x.id == 1015);
                int iSini = expOrdered.FindIndex(x => x.id == 1014);
                if (iBlood >= 0 && iSini >= 0 && iBlood < iSini)
                {
                    var item = expOrdered[iBlood];
                    expOrdered.RemoveAt(iBlood);
                    iSini = expOrdered.FindIndex(x => x.id == 1014);
                    expOrdered.Insert(Math.Min(expOrdered.Count, iSini + 1), item);
                }

                foreach (var p in expOrdered)
                {
                    var c = Instantiate(cardPrefab, gen9Expeditions.gridRoot);
                    c.ClearEndState();
                    c.Bind(p);
                    cardById[p.id] = c;
                    pokemonById[p.id] = p;
                    _hintShadowOrder.Add(p.id);
                    int baseKey = p.baseId != 0 ? p.baseId : p.id;
                    expeditionPickByBase[baseKey] = p;
                    expeditionCardByBase[baseKey] = c;

                    void AddKey(string s)
                    {
                        var k = GuessNormalizer.Key(s);
                        if (!string.IsNullOrEmpty(k))
                            expeditionByBaseName[k] = baseKey;
                    }
                    AddKey(p.name);
                    if (p.aliases != null)
                        foreach (var a in p.aliases)
                            AddKey(a);
                    var idx = p.name.IndexOf('(');
                    if (idx > 0)
                        AddKey(p.name[..idx].Trim());

                    var baseMon = allDb.FirstOrDefault(x => x.id == baseKey);
                    if (baseMon != null)
                    {
                        AddKey(baseMon.name);
                        if (baseMon.aliases != null)
                            foreach (var a in baseMon.aliases)
                                AddKey(a);
                    }
                }
            }

            if (fullGmax != null)
            {
                foreach (var p in gmaxPoolF)
                {
                    var c = Instantiate(cardPrefab, fullGmax.gridRoot);
                    c.ClearEndState();
                    c.Bind(p);
                    cardById[p.id] = c;
                    pokemonById[p.id] = p;
                    _hintShadowOrder.Add(p.id);
                    int baseId = p.baseId != 0 ? p.baseId : p.id;
                    gmaxPickByBase[baseId] = p;
                    gmaxCardByBase[baseId] = c;

                    var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                    var baseName = baseMon?.name ?? BaseNameFrom(p.name);
                    AddKey(gmaxByBaseName, p.name, baseId);
                    if (p.aliases != null)
                        foreach (var a in p.aliases)
                            AddKey(gmaxByBaseName, a, baseId);
                    if (!string.IsNullOrEmpty(baseName))
                    {
                        AddKey(gmaxByBaseName, $"{baseName} gmax", baseId);
                        AddKey(gmaxByBaseName, $"gmax {baseName}", baseId);
                        AddKey(gmaxByBaseName, $"{baseName} gigantamax", baseId);
                        AddKey(gmaxByBaseName, $"gigantamax {baseName}", baseId);
                    }
                    if (baseMon?.aliases != null)
                        foreach (var a in baseMon.aliases)
                        {
                            AddKey(gmaxByBaseName, $"{a} gmax", baseId);
                            AddKey(gmaxByBaseName, $"gmax {a}", baseId);
                            AddKey(gmaxByBaseName, $"{a} gigantamax", baseId);
                            AddKey(gmaxByBaseName, $"gigantamax {a}", baseId);
                        }
                }
            }

            if (fullHisui != null)
            {
                foreach (var p in hisuiPoolF)
                {
                    var c = Instantiate(cardPrefab, fullHisui.gridRoot);
                    c.ClearEndState();
                    c.Bind(p);
                    cardById[p.id] = c;
                    pokemonById[p.id] = p;
                    _hintShadowOrder.Add(p.id);
                    int baseId = p.baseId != 0 ? p.baseId : p.id;
                    hisuiPickByBase[baseId] = p;
                    hisuiCardByBase[baseId] = c;

                    var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                    var baseName = baseMon?.name ?? BaseNameFrom(p.name);
                    AddKey(hisuiByBaseName, p.name, baseId);
                    if (p.aliases != null)
                        foreach (var a in p.aliases)
                            AddKey(hisuiByBaseName, a, baseId);
                    if (!string.IsNullOrEmpty(baseName))
                    {
                        AddKey(hisuiByBaseName, $"hisuian {baseName}", baseId);
                        AddKey(hisuiByBaseName, $"{baseName} hisui", baseId);
                        AddKey(hisuiByBaseName, $"hisui {baseName}", baseId);
                    }
                    if (baseMon?.aliases != null)
                        foreach (var a in baseMon.aliases)
                        {
                            AddKey(hisuiByBaseName, $"hisuian {a}", baseId);
                            AddKey(hisuiByBaseName, $"{a} hisui", baseId);
                            AddKey(hisuiByBaseName, $"hisui {a}", baseId);
                        }
                }
            }

            if (fullLumioseMegas != null)
            {
                foreach (var p in lumiosePoolF.OrderBy(x => x.id))
                {
                    var c = Instantiate(cardPrefab, fullLumioseMegas.gridRoot);
                    c.ClearEndState();
                    c.Bind(p);
                    cardById[p.id] = c;
                    pokemonById[p.id] = p;
                    _hintShadowOrder.Add(p.id);
                    int baseId = p.baseId != 0 ? p.baseId : p.id;
                    lumiosePickByBase[baseId] = p;
                    lumioseCardByBase[baseId] = c;

                    var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                    var baseName = baseMon?.name ?? BaseNameFrom(p.name);

                    AddKey(lumioseByBaseName, p.name, baseId);
                    if (p.aliases != null)
                        foreach (var a in p.aliases)
                            AddKey(lumioseByBaseName, a, baseId);

                    if (!string.IsNullOrEmpty(baseName))
                    {
                        AddKey(lumioseByBaseName, baseName, baseId);
                        AddKey(lumioseByBaseName, $"{baseName} mega", baseId);
                        AddKey(lumioseByBaseName, $"mega {baseName}", baseId);
                    }

                    if (baseMon?.aliases != null)
                        foreach (var a in baseMon.aliases)
                        {
                            AddKey(lumioseByBaseName, a, baseId);
                            AddKey(lumioseByBaseName, $"{a} mega", baseId);
                            AddKey(lumioseByBaseName, $"mega {a}", baseId);
                        }
                }

                fullLumioseMegas.SetCardCount(fullLumioseMegas.gridRoot.childCount);
                FitSection(fullLumioseMegas);
            }
            if (fullHyperspaceMegas != null)
            {
                foreach (var p in hyperspacePoolF.OrderBy(x => x.id))
                {
                    var c = Instantiate(cardPrefab, fullHyperspaceMegas.gridRoot);
                    c.ClearEndState();
                    c.Bind(p);
                    cardById[p.id] = c;
                    pokemonById[p.id] = p;
                    _hintShadowOrder.Add(p.id);
                    int baseId = p.baseId != 0 ? p.baseId : p.id;
                    hyperspacePickByBase[baseId] = p;
                    hyperspaceCardByBase[baseId] = c;

                    var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                    var baseName = baseMon?.name ?? BaseNameFrom(p.name);

                    AddKey(hyperspaceByBaseName, p.name, baseId);
                    if (p.aliases != null)
                        foreach (var a in p.aliases)
                            AddKey(hyperspaceByBaseName, a, baseId);

                    if (!string.IsNullOrEmpty(baseName))
                    {
                        AddKey(hyperspaceByBaseName, baseName, baseId);
                        AddKey(hyperspaceByBaseName, $"{baseName} mega", baseId);
                        AddKey(hyperspaceByBaseName, $"mega {baseName}", baseId);
                    }

                    if (baseMon?.aliases != null)
                        foreach (var a in baseMon.aliases)
                        {
                            AddKey(hyperspaceByBaseName, a, baseId);
                            AddKey(hyperspaceByBaseName, $"{a} mega", baseId);
                            AddKey(hyperspaceByBaseName, $"mega {a}", baseId);
                        }
                }

                fullHyperspaceMegas.SetCardCount(fullHyperspaceMegas.gridRoot.childCount);
                FitSection(fullHyperspaceMegas);
            }
            if (unknownSec != null)
            {
                var unknowns = allDb
                    .Where(p => p.id == 808 || p.id == 809)
                    .Where(MatchesType)
                    .ToList();

                foreach (var p in unknowns)
                {
                    var c = Instantiate(cardPrefab, unknownSec.gridRoot);
                    c.ClearEndState();
                    c.Bind(p);
                    cardById[p.id] = c;
                    pokemonById[p.id] = p;
                    _hintShadowOrder.Add(p.id);
                }

                unknownSec.SetCardCount(unknownSec.gridRoot.childCount);
                FitSection(unknownSec);
            }
            foreach (var sec in mainByGen.Values)
            {
                sec.SetCardCount(sec.gridRoot.childCount);
                FitSection(sec);
            }
            if (megaKalos != null)
            {
                megaKalos.SetCardCount(megaKalos.gridRoot.childCount);
                FitSection(megaKalos);
            }
            if (megaHoenn != null)
            {
                megaHoenn.SetCardCount(megaHoenn.gridRoot.childCount);
                FitSection(megaHoenn);
            }
            if (gen9Expeditions != null)
            {
                gen9Expeditions.SetCardCount(gen9Expeditions.gridRoot.childCount);
                FitSection(gen9Expeditions);
            }
            if (fullGmax != null)
            {
                fullGmax.SetCardCount(fullGmax.gridRoot.childCount);
                FitSection(fullGmax);
            }
            if (fullHisui != null)
            {
                fullHisui.SetCardCount(fullHisui.gridRoot.childCount);
                FitSection(fullHisui);
            }
            if (fullLumioseMegas != null)
            {
                fullLumioseMegas.SetCardCount(fullLumioseMegas.gridRoot.childCount);
                FitSection(fullLumioseMegas);
            }

            if (fullHyperspaceMegas != null)
            {
                fullHyperspaceMegas.SetCardCount(fullHyperspaceMegas.gridRoot.childCount);
                FitSection(fullHyperspaceMegas);
            }

            unknownSec.SetCardCount(unknownSec.gridRoot.childCount);
            FitSection(unknownSec);
            foreach (var sec in mainByGen.Values)
                FinalizeSection(sec);

            FinalizeSection(megaKalos);
            FinalizeSection(megaHoenn);
            FinalizeSection(gen9Expeditions);
            FinalizeSection(fullGmax);
            FinalizeSection(fullHisui);
            FinalizeSection(fullLumioseMegas);
            FinalizeSection(fullHyperspaceMegas);
            FinalizeSection(unknownSec);
            ApplyQuizTitleColor(ShouldUseLightQuizTitles());
            RebuildHintShadowOrderFromSections();
            UpdateScore();
            ApplyPendingSavedMultiplayerSessionRestore();
            return;
        }

        if (generation == 10)
        {
            megaKalosGen = Instantiate(sectionGroupPrefab, content);
            megaKalosGen.EnsureLayout();
            megaKalosGen.SetTitle("Mega Evolution - Kalos", false);

            megaHoennGen = Instantiate(sectionGroupPrefab, content);
            megaHoennGen.EnsureLayout();
            megaHoennGen.SetTitle("Mega Evolution - Hoenn", false);

            lumioseMegasSec = Instantiate(sectionGroupPrefab, content);
            lumioseMegasSec.EnsureLayout();
            lumioseMegasSec.SetTitle("Mega Evolution - Lumiose", false);

            hyperspaceMegasSec = Instantiate(sectionGroupPrefab, content);
            hyperspaceMegasSec.EnsureLayout();
            hyperspaceMegasSec.SetTitle("Mega Evolution - Hyperspace", false);
        }
        if (generation == 7)
        {
            alolaUnknown = Instantiate(sectionGroupPrefab, content);
            alolaUnknown.EnsureLayout();
            alolaUnknown.SetTitle("Unknown", false);
        }
        if (generation == 8)
        {
            gmaxSec = Instantiate(sectionGroupPrefab, content);
            gmaxSec.EnsureLayout();
            gmaxSec.SetTitle("Gigantamax (Gen 8)", false);
            hisuiSec = Instantiate(sectionGroupPrefab, content);
            hisuiSec.EnsureLayout();
            hisuiSec.SetTitle("Hisui (Gen 8)", false);
        }
        if (generation == 9)
        {
            paldeaExpeditions = Instantiate(sectionGroupPrefab, content);
            paldeaExpeditions.EnsureLayout();
            paldeaExpeditions.SetTitle("Paldea Expeditions", false);
        }

        var expeditionPool = new List<Pokemon>();
        var gmaxPoolGen = new List<Pokemon>();
        var hisuiPoolGen = new List<Pokemon>();
        var lumioseMegaPool = new List<Pokemon>();
        var hyperspaceMegaPool = new List<Pokemon>();
        var alolaUnknownPool = new List<Pokemon>();

        foreach (var p in ordered)
        {
            if (generation == 10)
            {
                if (Helpers.IsLumioseMega(p))
                {
                    lumioseMegaPool.Add(p);
                    continue;
                }
                if (Helpers.IsHyperspaceMega(p))
                {
                    hyperspaceMegaPool.Add(p);
                    continue;
                }
                if (Helpers.IsMega(p))
                {
                    int baseKey = BaseIdOf(p);
                    if (!megaFormsByBase.TryGetValue(baseKey, out var list))
                        megaFormsByBase[baseKey] = list = new List<Pokemon>();
                    list.Add(p);
                    continue;
                }
            }
            if (generation == 7 && Helpers.IsAlolaUnknown(p))
            {
                alolaUnknownPool.Add(p);
                continue;
            }

            if (generation == 8 && Helpers.IsGmax(p))
            {
                gmaxPoolGen.Add(p);
                continue;
            }
            if (generation == 8 && Helpers.IsHisui(p))
            {
                if (!Helpers.IsPaldeaExpeditionOrBloodmoon(p))
                    hisuiPoolGen.Add(p);
                continue;
            }
            if (generation == 9 && Helpers.IsPaldeaExpeditionOrBloodmoon(p))
            {
                expeditionPool.Add(p);
                continue;
            }

            var card = Instantiate(cardPrefab, main.gridRoot);
            card.ClearEndState();
            card.Bind(p);
            cardById[p.id] = card;
            pokemonById[p.id] = p;
            _hintShadowOrder.Add(p.id);
        }

        if (generation == 10 && (megaKalosGen != null || megaHoennGen != null))
        {
            var rng = new System.Random();

            foreach (var kv in megaFormsByBase)
            {
                int baseId = kv.Key;
                var forms = kv.Value;
                var pick = forms[rng.Next(forms.Count)];

                var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                int baseGen = baseMon?.generation ?? pick.generation;

                SectionGroup targetSec;
                if (baseGen == 3)
                    targetSec = megaHoennGen;
                else
                    targetSec = megaKalosGen;

                if (targetSec == null)
                    continue;

                var card = Instantiate(cardPrefab, targetSec.gridRoot);
                card.ClearEndState();
                card.Bind(pick);
                megaSlotPickByBase[baseId] = pick;
                megaCardByBase[baseId] = card;
                cardById[pick.id] = card;
                pokemonById[pick.id] = pick;
                _hintShadowOrder.Add(pick.id);
            }
        }

        if (generation == 10 && lumioseMegasSec != null)
        {
            foreach (var p in lumioseMegaPool.OrderBy(x => x.id))
            {
                var c = Instantiate(cardPrefab, lumioseMegasSec.gridRoot);
                c.ClearEndState();
                c.Bind(p);
                cardById[p.id] = c;
                pokemonById[p.id] = p;
                _hintShadowOrder.Add(p.id);

                int baseId = p.baseId != 0 ? p.baseId : p.id;
                lumiosePickByBase[baseId] = p;
                lumioseCardByBase[baseId] = c;

                var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                var baseName = baseMon?.name ?? BaseNameFrom(p.name);

                AddKey(lumioseByBaseName, p.name, baseId);
                if (p.aliases != null)
                    foreach (var a in p.aliases)
                        AddKey(lumioseByBaseName, a, baseId);

                if (!string.IsNullOrEmpty(baseName))
                {
                    AddKey(lumioseByBaseName, baseName, baseId);
                    AddKey(lumioseByBaseName, $"{baseName} mega", baseId);
                    AddKey(lumioseByBaseName, $"mega {baseName}", baseId);
                }

                if (baseMon?.aliases != null)
                    foreach (var a in baseMon.aliases)
                    {
                        AddKey(lumioseByBaseName, a, baseId);
                        AddKey(lumioseByBaseName, $"{a} mega", baseId);
                        AddKey(lumioseByBaseName, $"mega {a}", baseId);
                    }
            }
        }

        if (generation == 10 && hyperspaceMegasSec != null)
        {
            foreach (var p in hyperspaceMegaPool.OrderBy(x => x.id))
            {
                var c = Instantiate(cardPrefab, hyperspaceMegasSec.gridRoot);
                c.ClearEndState();
                c.Bind(p);
                cardById[p.id] = c;
                pokemonById[p.id] = p;
                _hintShadowOrder.Add(p.id);

                int baseId = p.baseId != 0 ? p.baseId : p.id;
                hyperspacePickByBase[baseId] = p;
                hyperspaceCardByBase[baseId] = c;

                var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                var baseName = baseMon?.name ?? BaseNameFrom(p.name);

                AddKey(hyperspaceByBaseName, p.name, baseId);
                if (p.aliases != null)
                    foreach (var a in p.aliases)
                        AddKey(hyperspaceByBaseName, a, baseId);

                if (!string.IsNullOrEmpty(baseName))
                {
                    AddKey(hyperspaceByBaseName, baseName, baseId);
                    AddKey(hyperspaceByBaseName, $"{baseName} mega", baseId);
                    AddKey(hyperspaceByBaseName, $"mega {baseName}", baseId);
                }

                if (baseMon?.aliases != null)
                    foreach (var a in baseMon.aliases)
                    {
                        AddKey(hyperspaceByBaseName, a, baseId);
                        AddKey(hyperspaceByBaseName, $"{a} mega", baseId);
                        AddKey(hyperspaceByBaseName, $"mega {a}", baseId);
                    }
            }
        }

        if (generation == 8 && hisuiSec)
        {
            foreach (var p in hisuiPoolGen.OrderBy(p => DexOrder.GetIndex(p)))
            {
                var card = Instantiate(cardPrefab, hisuiSec.gridRoot);
                card.ClearEndState();
                card.Bind(p);
                cardById[p.id] = card;
                pokemonById[p.id] = p;
                _hintShadowOrder.Add(p.id);

                int baseId = p.baseId != 0 ? p.baseId : p.id;
                hisuiPickByBase[baseId] = p;
                hisuiCardByBase[baseId] = card;

                var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                var baseName = baseMon?.name ?? BaseNameFrom(p.name);
                AddKey(hisuiByBaseName, p.name, baseId);
                if (p.aliases != null)
                    foreach (var a in p.aliases)
                        AddKey(hisuiByBaseName, a, baseId);
                if (!string.IsNullOrEmpty(baseName))
                {
                    AddKey(hisuiByBaseName, $"hisuian {baseName}", baseId);
                    AddKey(hisuiByBaseName, $"{baseName} hisui", baseId);
                    AddKey(hisuiByBaseName, $"hisui {baseName}", baseId);
                }
                if (baseMon?.aliases != null)
                    foreach (var a in baseMon.aliases)
                    {
                        AddKey(hisuiByBaseName, $"hisuian {a}", baseId);
                        AddKey(hisuiByBaseName, $"{a} hisui", baseId);
                        AddKey(hisuiByBaseName, $"hisui {a}", baseId);
                    }
            }
        }
        if (generation == 8 && gmaxSec)
        {
            foreach (var p in gmaxPoolGen.OrderBy(p => DexOrder.GetIndex(p)))
            {
                var c = Instantiate(cardPrefab, gmaxSec.gridRoot);
                c.ClearEndState();
                c.Bind(p);
                cardById[p.id] = c;
                pokemonById[p.id] = p;
                _hintShadowOrder.Add(p.id);

                int baseId = p.baseId != 0 ? p.baseId : p.id;
                gmaxPickByBase[baseId] = p;
                gmaxCardByBase[baseId] = c;

                var baseMon = allDb.FirstOrDefault(x => x.id == baseId);
                var baseName = baseMon?.name ?? BaseNameFrom(p.name);

                AddKey(gmaxByBaseName, p.name, baseId);
                if (p.aliases != null)
                    foreach (var a in p.aliases)
                        AddKey(gmaxByBaseName, a, baseId);

                if (!string.IsNullOrEmpty(baseName))
                {
                    AddKey(gmaxByBaseName, $"{baseName} gmax", baseId);
                    AddKey(gmaxByBaseName, $"gmax {baseName}", baseId);
                    AddKey(gmaxByBaseName, $"{baseName} gigantamax", baseId);
                    AddKey(gmaxByBaseName, $"gigantamax {baseName}", baseId);
                }

                if (baseMon?.aliases != null)
                    foreach (var a in baseMon.aliases)
                    {
                        AddKey(gmaxByBaseName, $"{a} gmax", baseId);
                        AddKey(gmaxByBaseName, $"gmax {a}", baseId);
                        AddKey(gmaxByBaseName, $"{a} gigantamax", baseId);
                        AddKey(gmaxByBaseName, $"gigantamax {a}", baseId);
                    }
            }
        }
        if (generation == 9 && paldeaExpeditions)
        {
            var expOrdered = expeditionPool.OrderBy(p => DexOrder.GetIndex(p)).ToList();
            int iBlood = expOrdered.FindIndex(x => x.id == 1015);
            int iSini = expOrdered.FindIndex(x => x.id == 1014);
            if (iBlood >= 0 && iSini >= 0 && iBlood < iSini)
            {
                var item = expOrdered[iBlood];
                expOrdered.RemoveAt(iBlood);
                iSini = expOrdered.FindIndex(x => x.id == 1014);
                expOrdered.Insert(Math.Min(expOrdered.Count, iSini + 1), item);
            }

            foreach (var p in expOrdered)
            {
                var c = Instantiate(cardPrefab, paldeaExpeditions.gridRoot);
                c.ClearEndState();
                c.Bind(p);
                cardById[p.id] = c;
                pokemonById[p.id] = p;
                _hintShadowOrder.Add(p.id);

                int baseKey = p.baseId != 0 ? p.baseId : p.id;
                expeditionPickByBase[baseKey] = p;
                expeditionCardByBase[baseKey] = c;

                void AddKey(string s)
                {
                    var k = GuessNormalizer.Key(s);
                    if (!string.IsNullOrEmpty(k))
                        expeditionByBaseName[k] = baseKey;
                }

                AddKey(p.name);
                if (p.aliases != null)
                    foreach (var a in p.aliases)
                        AddKey(a);

                var idx = p.name.IndexOf('(');
                if (idx > 0)
                    AddKey(p.name[..idx].Trim());

                var baseMon = allDb.FirstOrDefault(x => x.id == baseKey);
                if (baseMon != null)
                {
                    AddKey(baseMon.name);
                    if (baseMon.aliases != null)
                        foreach (var a in baseMon.aliases)
                            AddKey(a);
                }
            }
        }

        main.SetCardCount(main.gridRoot.childCount);
        FitSection(main);
        if (megaKalosGen != null)
        {
            megaKalosGen.SetCardCount(megaKalosGen.gridRoot.childCount);
            FitSection(megaKalosGen);
        }
        if (megaHoennGen != null)
        {
            megaHoennGen.SetCardCount(megaHoennGen.gridRoot.childCount);
            FitSection(megaHoennGen);
        }
        if (lumioseMegasSec != null)
        {
            lumioseMegasSec.SetCardCount(lumioseMegasSec.gridRoot.childCount);
            FitSection(lumioseMegasSec);
        }
        if (hyperspaceMegasSec != null)
        {
            hyperspaceMegasSec.SetCardCount(hyperspaceMegasSec.gridRoot.childCount);
            FitSection(hyperspaceMegasSec);
        }
        if (alolaUnknown != null)
        {
            foreach (var p in alolaUnknownPool.OrderBy(x => DexOrder.GetIndex(x)))
            {
                var c = Instantiate(cardPrefab, alolaUnknown.gridRoot);
                c.ClearEndState();
                c.Bind(p);
                cardById[p.id] = c;
                pokemonById[p.id] = p;
                _hintShadowOrder.Add(p.id);
            }

            alolaUnknown.SetCardCount(alolaUnknown.gridRoot.childCount);
            FitSection(alolaUnknown);
        }
        if (paldeaExpeditions != null)
        {
            paldeaExpeditions.SetCardCount(paldeaExpeditions.gridRoot.childCount);
            FitSection(paldeaExpeditions);
        }
        if (gmaxSec != null)
        {
            gmaxSec.SetCardCount(gmaxSec.gridRoot.childCount);
            FitSection(gmaxSec);
        }
        if (hisuiSec != null)
        {
            hisuiSec.SetCardCount(hisuiSec.gridRoot.childCount);
            FitSection(hisuiSec);
        }

        RebuildHintShadowOrderFromSections();
        UpdateScore();
        bool noSubSections =
            generation > 0
            && megaHoennGen == null
            && megaKalosGen == null
            && gmaxSec == null
            && hisuiSec == null
            && paldeaExpeditions == null;

        main.SetHeaderGap(noSubSections);
        ApplyQuizTitleColor(ShouldUseLightQuizTitles());
        QueueResetScrollToTop();
        ApplyPendingSavedMultiplayerSessionRestore();
    }

    bool HasTypeFilter => !string.IsNullOrEmpty(selectedType);

    bool MatchesType(Pokemon p)
    {
        if (!HasTypeFilter)
            return true;
        if (p?.types == null)
            return false;
        for (int i = 0; i < p.types.Length; i++)
            if (string.Equals(p.types[i], selectedType, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public void StartTypeQuiz(string typeKey)
    {
        selectedType = typeKey.ToLowerInvariant();
        generation = 0;
        StopAllCoroutines();
    }

    public void StartGenQuiz(int gen)
    {
        selectedType = null;
        GameSettings.TypeFilter = null;
        generation = gen;
        StopAllCoroutines();
    }

    static void AddKey(Dictionary<string, int> map, string s, int baseId)
    {
        var k = GuessNormalizer.Key(s);
        if (!string.IsNullOrEmpty(k))
            map[k] = baseId;
    }

    static string BaseNameFrom(string name)
    {
        var i = name.IndexOf('(');
        return i > 0 ? name[..i].Trim() : name.Trim();
    }

    private void RebuildHintShadowOrderFromSections()
    {
        _hintShadowOrder.Clear();
        var seen = new HashSet<int>();

        if (!content)
            return;

        foreach (Transform secTr in content)
        {
            var sec = secTr.GetComponent<SectionGroup>();
            if (!sec || !sec.gridRoot)
                continue;

            foreach (Transform cardTr in sec.gridRoot)
            {
                var card = cardTr.GetComponent<PokemonCard>();
                if (!card)
                    continue;

                int id = card.PokemonId;
                if (id != 0 && seen.Add(id))
                {
                    _hintShadowOrder.Add(id);
                }
            }
        }
    }

    private void ApplyContentTopPadding(bool preserveScroll)
    {
        if (!content)
            return;

        int targetTopPadding =
            (QuizNetworkRuntime.IsMultiplayerActive || GameSettings.IsMultiplayer)
                ? MultiplayerContentTopPadding
                : BaseContentTopPadding;
        if (_appliedContentTopPadding == targetTopPadding)
            return;

        float scrollOffsetY = 0f;
        bool hasScrollSnapshot = preserveScroll && TryCaptureScrollOffset(out scrollOffsetY);

        var contentRt = content as RectTransform;
        if (!contentRt)
            return;

        var vlg = contentRt.GetOrAdd<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(
            vlg.padding.left,
            vlg.padding.right,
            targetTopPadding,
            vlg.padding.bottom
        );
        _appliedContentTopPadding = targetTopPadding;

        LayoutRebuilder.MarkLayoutForRebuild(contentRt);
        Canvas.ForceUpdateCanvases();

        if (hasScrollSnapshot)
            RestoreScrollOffsetAfterLayout(scrollOffsetY);
    }

    private void ApplyMultiplayerRightDock(bool enabled, bool preserveScroll)
    {
        if (!scrollRect || !scrollRect.viewport)
            return;

        var scrollRt = scrollRect.GetComponent<RectTransform>();
        if (!_scrollRectOffsetsCaptured || _scrollRectRt != scrollRt)
        {
            _scrollRectRt = scrollRt;
            if (_scrollRectRt)
            {
                _originalScrollRectOffsetMin = _scrollRectRt.offsetMin;
                _originalScrollRectOffsetMax = _scrollRectRt.offsetMax;
            }
            _scrollRectOffsetsCaptured = true;
            _multiplayerRightDockApplied = false;
        }

        if (!_viewportOffsetsCaptured || _scrollViewportRt != scrollRect.viewport)
        {
            _scrollViewportRt = scrollRect.viewport;
            _originalViewportOffsetMin = _scrollViewportRt.offsetMin;
            _originalViewportOffsetMax = _scrollViewportRt.offsetMax;
            _viewportOffsetsCaptured = true;
            _multiplayerRightDockApplied = false;
        }

        var backgroundRt = backgroundImage ? backgroundImage.rectTransform : null;
        if (!_backgroundImageOffsetsCaptured || _backgroundImageRt != backgroundRt)
        {
            _backgroundImageRt = backgroundRt;
            if (_backgroundImageRt)
            {
                _originalBackgroundImageOffsetMin = _backgroundImageRt.offsetMin;
                _originalBackgroundImageOffsetMax = _backgroundImageRt.offsetMax;
            }
            _backgroundImageOffsetsCaptured = true;
            _singleplayerHeaderExtraApplied = false;
        }

        bool singleplayerHeaderExtra = !enabled;
        if (
            _multiplayerRightDockApplied == enabled
            && _singleplayerHeaderExtraApplied == singleplayerHeaderExtra
        )
            return;

        float scrollOffsetY = 0f;
        bool hasScrollSnapshot = preserveScroll && TryCaptureScrollOffset(out scrollOffsetY);

        if (_scrollRectRt)
        {
            _scrollRectRt.offsetMin = _originalScrollRectOffsetMin;
            _scrollRectRt.offsetMax = _originalScrollRectOffsetMax;
        }
        _scrollViewportRt.offsetMin = _originalViewportOffsetMin;
        _scrollViewportRt.offsetMax = _originalViewportOffsetMax;
        if (_backgroundImageRt)
        {
            _backgroundImageRt.offsetMin = _originalBackgroundImageOffsetMin;
            _backgroundImageRt.offsetMax = _originalBackgroundImageOffsetMax;
        }

        float rightPadding = Mathf.Max(
            MultiplayerGridRightPaddingMin,
            (
                (_scrollRectRt ? _scrollRectRt.parent : _scrollViewportRt.parent) as RectTransform
            )?.rect.width * 0.20f ?? MultiplayerGridRightPaddingMin
        );

        if (enabled)
        {
            if (_scrollRectRt && _scrollRectRt != _scrollViewportRt)
            {
                _scrollRectRt.offsetMax = new Vector2(
                    _originalScrollRectOffsetMax.x - rightPadding,
                    _originalScrollRectOffsetMax.y
                );
            }
            else
            {
                _scrollViewportRt.offsetMax = new Vector2(
                    _originalViewportOffsetMax.x - rightPadding,
                    _originalViewportOffsetMax.y
                );
            }
        }
        else
        {
            ApplyAdditionalTopOffset(_scrollRectRt ? _scrollRectRt : _scrollViewportRt, SingleplayerHeaderExtraHeight);
            ApplyAdditionalTopOffset(_backgroundImageRt, SingleplayerHeaderExtraHeight);
        }

        _multiplayerRightDockApplied = enabled;
        _singleplayerHeaderExtraApplied = singleplayerHeaderExtra;

        Canvas.ForceUpdateCanvases();
        if (_scrollRectRt)
            LayoutRebuilder.MarkLayoutForRebuild(_scrollRectRt);
        ApplyColumnsToAllSections();

        if (hasScrollSnapshot)
            RestoreScrollOffsetAfterLayout(scrollOffsetY);
    }

    private static void ApplyAdditionalTopOffset(RectTransform rt, float offset)
    {
        if (!rt)
            return;

        var offsetMax = rt.offsetMax;
        offsetMax.y -= offset;
        rt.offsetMax = offsetMax;
    }

    private void CancelGridTransientCoroutines()
    {
        _scrollToken++;

        if (_scrollRoutine != null)
        {
            StopCoroutine(_scrollRoutine);
            _scrollRoutine = null;
        }

        if (_scrollResetRoutine != null)
        {
            StopCoroutine(_scrollResetRoutine);
            _scrollResetRoutine = null;
        }

        if (_scrollRestoreRoutine != null)
        {
            StopCoroutine(_scrollRestoreRoutine);
            _scrollRestoreRoutine = null;
        }
    }

    private void CancelPendingResetScrollToTop()
    {
        if (_scrollResetRoutine == null)
            return;

        StopCoroutine(_scrollResetRoutine);
        _scrollResetRoutine = null;
    }

    private void CancelActiveSmartScroll()
    {
        _scrollToken++;

        if (_scrollRoutine == null)
            return;

        StopCoroutine(_scrollRoutine);
        _scrollRoutine = null;
    }

    private bool TryCaptureScrollOffset(out float offsetY)
    {
        offsetY = 0f;
        if (!scrollRect || !scrollRect.content || !scrollRect.viewport)
            return false;

        Canvas.ForceUpdateCanvases();
        float maxOffset = Mathf.Max(0f, scrollRect.content.rect.height - scrollRect.viewport.rect.height);
        if (maxOffset <= 0f)
            return false;

        offsetY = Mathf.Clamp(scrollRect.content.anchoredPosition.y, 0f, maxOffset);
        return true;
    }

    private void RestoreScrollOffsetAfterLayout(float offsetY)
    {
        if (_scrollRestoreRoutine != null)
        {
            StopCoroutine(_scrollRestoreRoutine);
            _scrollRestoreRoutine = null;
        }

        RestoreScrollOffset(offsetY);

        if (isActiveAndEnabled)
            _scrollRestoreRoutine = StartCoroutine(CoRestoreScrollOffset(offsetY, _buildToken));
    }

    private IEnumerator CoRestoreScrollOffset(float offsetY, int token)
    {
        for (int i = 0; i < 2; i++)
        {
            yield return null;
            if (token != _buildToken)
                yield break;

            RebuildSectionContentLayoutImmediate();
            RestoreScrollOffset(offsetY);
        }

        _scrollRestoreRoutine = null;
    }

    private void RestoreScrollOffset(float offsetY)
    {
        if (!scrollRect || !scrollRect.content || !scrollRect.viewport)
            return;

        Canvas.ForceUpdateCanvases();
        float maxOffset = Mathf.Max(0f, scrollRect.content.rect.height - scrollRect.viewport.rect.height);

        scrollRect.StopMovement();
        scrollRect.velocity = Vector2.zero;

        if (maxOffset <= 0f)
        {
            scrollRect.verticalNormalizedPosition = 1f;
            return;
        }

        var anchored = scrollRect.content.anchoredPosition;
        anchored.y = Mathf.Clamp(offsetY, 0f, maxOffset);
        scrollRect.content.anchoredPosition = anchored;
        scrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01(anchored.y / maxOffset);
    }

    private bool IsNearTopScrollOffset(float offsetY)
    {
        if (!scrollRect || !scrollRect.viewport)
            return offsetY <= 1f;

        float topLockRange = Mathf.Max(120f, scrollRect.viewport.rect.height * 0.18f);
        return offsetY <= topLockRange;
    }

    private void QueueResetScrollToTop()
    {
        if (!scrollRect || !scrollRect.content)
            return;

        if (_scrollResetRoutine != null)
        {
            StopCoroutine(_scrollResetRoutine);
            _scrollResetRoutine = null;
        }

        ResetScrollToTopImmediate();

        if (isActiveAndEnabled)
            _scrollResetRoutine = StartCoroutine(CoResetScrollToTop(_buildToken));
    }

    private IEnumerator CoResetScrollToTop(int token)
    {
        for (int i = 0; i < 3; i++)
        {
            yield return null;
            if (token != _buildToken)
                yield break;

            Canvas.ForceUpdateCanvases();
        }

        foreach (var fit in _fits)
        {
            if (fit)
                fit.Recalculate();
        }

        RebuildSectionContentLayoutImmediate();

        if (scrollRect && scrollRect.content)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);

        Canvas.ForceUpdateCanvases();

        if (token == _buildToken)
            ResetScrollToTopImmediate();

        _scrollResetRoutine = null;
    }

    private void ResetScrollToTopImmediate()
    {
        if (!scrollRect || !scrollRect.content)
            return;

        scrollRect.StopMovement();
        scrollRect.velocity = Vector2.zero;

        var anchored = scrollRect.content.anchoredPosition;
        anchored.y = 0f;
        scrollRect.content.anchoredPosition = anchored;
        scrollRect.verticalNormalizedPosition = 1f;
    }

    private bool TryAcceptGmaxByBaseName(string text, bool commit)
    {
        if ((generation != 8 && generation != 0) || string.IsNullOrWhiteSpace(text))
            return false;
        var k = GuessNormalizer.Key(text);
        if (!gmaxByBaseName.TryGetValue(k, out var baseId))
            return false;
        if (!gmaxPickByBase.TryGetValue(baseId, out var pick))
            return false;
        HandleCandidate(pick, text, commit);
        return true;
    }

    private bool TryAcceptHisuiByBaseName(string text, bool commit)
    {
        if ((generation != 8 && generation != 0) || string.IsNullOrWhiteSpace(text))
            return false;
        var k = GuessNormalizer.Key(text);
        if (!hisuiByBaseName.TryGetValue(k, out var baseId))
            return false;
        if (!hisuiPickByBase.TryGetValue(baseId, out var pick))
            return false;
        HandleCandidate(pick, text, commit);
        return true;
    }

    private bool TryAcceptHyperspaceByBaseName(string text, bool commit)
    {
        if ((generation != 9 && generation != 0) || string.IsNullOrWhiteSpace(text))
            return false;

        var k = GuessNormalizer.Key(text);

        if (!hyperspaceByBaseName.TryGetValue(k, out var baseId))
            return false;

        if (!commit)
            return true;

        RevealAllByBaseId(baseId);
        return true;
    }

    private void RevealAllVariantsForBase(int baseKey, bool includeExpeditions = false)
    {
        foreach (var kv in cardById)
        {
            if (!pokemonById.TryGetValue(kv.Key, out var p))
                continue;

            int b = p.baseId != 0 ? p.baseId : p.id;
            if (b != baseKey)
                continue;

            bool isVariant =
                Helpers.IsMega(p)
                || Helpers.IsLumioseMega(p)
                || Helpers.IsHyperspaceMega(p)
                || Helpers.IsGmax(p)
                || Helpers.IsHisui(p)
                || Helpers.IsRegionalForm(p)
                || (includeExpeditions && Helpers.IsPaldeaExpeditionOrBloodmoon(p));

            if (!isVariant)
                continue;

            if (!solved.Contains(kv.Key))
            {
                solved.Add(kv.Key);
                kv.Value.Reveal();
            }
        }
    }

    private int RevealNextShadow()
    {
        Pokemon pick = null;
        int pickId = 0;

        foreach (var id in _hintShadowOrder)
        {
            if (solved.Contains(id) || shadowed.Contains(id))
                continue;

            if (!cardById.TryGetValue(id, out var card) || !card)
                continue;

            if (card.HintVisible)
            {
                pickId = id;
                pokemonById.TryGetValue(id, out pick);
                break;
            }
        }

        if (pick == null)
        {
            foreach (var id in _hintShadowOrder)
            {
                if (solved.Contains(id) || shadowed.Contains(id))
                    continue;

                if (!cardById.ContainsKey(id))
                    continue;

                pickId = id;
                pokemonById.TryGetValue(id, out pick);
                break;
            }
        }

        if (pick == null || pickId == 0)
            return 0;

        if (cardById.TryGetValue(pickId, out var targetCard) && targetCard)
        {
            shadowed.Add(pickId);
            targetCard.SetShadowMode(true);
            _shadowUsedCount++;
            SaveLocalQuizSession();
            return pickId;
        }

        return 0;
    }

    private void FitSection(SectionGroup grp)
    {
        var grid =
            grp.gridRoot.GetComponent<GridLayoutGroup>()
            ?? grp.gridRoot.gameObject.AddComponent<GridLayoutGroup>();
        grid.spacing = new Vector2(16, 16);
        grid.childAlignment = TextAnchor.UpperLeft;

        var fit =
            grp.gridRoot.GetComponent<GridAutoFit>()
            ?? grp.gridRoot.gameObject.AddComponent<GridAutoFit>();

        fit.Viewport = scrollRect ? scrollRect.viewport : null;
        fit.Header = grp.headerRect;
        fit.ItemCount = grp.CardCount;
        fit.OuterMarginX = 16;
        fit.OuterMarginY = 16;
        fit.Spacing = 16;

        fit.MinCols = currentCols;
        fit.MaxCols = currentCols;

        _fits.Add(fit);
        fit.Recalculate();
        grp.RefreshPreferredHeight();

        StartCoroutine(CoRecalcSafe(fit, _buildToken));
    }

    private void OnCardSizeSliderChanged(float t)
    {
        int cols = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(maxColsSmall, minColsLarge, t)),
            Mathf.Min(minColsLarge, maxColsSmall),
            Mathf.Max(minColsLarge, maxColsSmall)
        );

        if (cols == currentCols)
            return;
        currentCols = cols;
        PlayerPrefs.SetInt(KEY_COLS, currentCols);
        ApplyColumnsToAllSections(preserveScroll: true);
    }

    void FinalizeSection(SectionGroup sec)
    {
        if (!sec)
            return;

        int count = sec.gridRoot.childCount;
        if (count <= 0)
        {
            Destroy(sec.gameObject);
            return;
        }

        sec.SetCardCount(count);
        FitSection(sec);
    }

    private void ApplyColumnsToAllSections(bool preserveScroll = false)
    {
        float scrollOffsetY = 0f;
        bool hasScrollSnapshot = preserveScroll && TryCaptureScrollOffset(out scrollOffsetY);
        if (preserveScroll)
        {
            CancelActiveSmartScroll();
            CancelPendingResetScrollToTop();
        }

        if (cardSizeLabel)
            cardSizeLabel.text = $"{currentCols} cols";

        foreach (var fit in _fits)
        {
            if (!fit)
                continue;
            fit.MinCols = currentCols;
            fit.MaxCols = currentCols;
            fit.Recalculate();
        }

        foreach (var sec in _sections)
        {
            if (!sec)
                continue;
            sec.UpdateHeaderForCols(currentCols, minColsLarge, maxColsSmall);
        }

        RebuildSectionContentLayoutImmediate();

        if (hasScrollSnapshot)
        {
            if (IsNearTopScrollOffset(scrollOffsetY))
                RestoreScrollOffsetAfterLayout(0f);
            else
                RestoreScrollOffsetAfterLayout(scrollOffsetY);
        }
    }

    private IEnumerator CoRecalcSafe(GridAutoFit fit, int token)
    {
        if (!fit)
            yield break;

        yield return null;
        if (token != _buildToken || !fit || !fit.gameObject)
            yield break;

        Canvas.ForceUpdateCanvases();

        yield return null;
        if (token != _buildToken || !fit || !fit.isActiveAndEnabled)
            yield break;

        if (!fit.gameObject.activeInHierarchy)
            yield break;

        fit.Recalculate();
        RebuildSectionContentLayoutImmediate();
    }

    private void RebuildSectionContentLayoutImmediate()
    {
        Canvas.ForceUpdateCanvases();

        foreach (var sec in _sections)
        {
            if (!sec)
                continue;

            sec.RefreshPreferredHeight();
            if (sec.transform is RectTransform secRt)
                LayoutRebuilder.ForceRebuildLayoutImmediate(secRt);
        }

        if (content is RectTransform contentRt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);

        Canvas.ForceUpdateCanvases();
    }

    private void RevealTypeHintForOne()
    {
        if (QuizMultiplayerCoordinator.RequestRevealType())
            return;

        RevealTypeHintForOneInternal();
    }

    private int RevealTypeHintForOneInternal()
    {
        int pickId = 0;

        foreach (var id in _hintShadowOrder)
        {
            if (solved.Contains(id) || hinted.Contains(id) || shadowed.Contains(id))
                continue;

            if (!cardById.ContainsKey(id))
                continue;

            pickId = id;
            break;
        }

        if (pickId == 0)
            return 0;

        return ApplyTypeHintToId(pickId) ? pickId : 0;
    }

    private bool ApplyTypeHintToId(int pickId)
    {
        if (pickId == 0 || hinted.Contains(pickId))
            return false;

        if (!cardById.TryGetValue(pickId, out var card) || card == null)
        {
            Debug.LogWarning($"[Hint] No card for id {pickId}.");
            return false;
        }

        if (!pokemonById.TryGetValue(pickId, out var p) || p == null || p.types == null)
            return false;

        hinted.Add(pickId);
        card.ShowTypeHint(p.types);
        _hintUsedCount++;
        SaveLocalQuizSession();
        return true;
    }

    private void BuildTargetList()
    {
        _hintUsedCount = 0;
        _shadowUsedCount = 0;
        var all = PokemonDatabase.Instance.All().AsEnumerable();

        if (generation == 0)
        {
            all = all.Where(p =>
                p.generation >= 1
                && p.generation <= 9
                && !Helpers.IsMega(p)
                && !Helpers.IsGmax(p)
                && !Helpers.IsHisui(p)
                && !Helpers.IsPaldeaExpedition(p)
                && !Helpers.IsLumioseMega(p)
                && !Helpers.IsHyperspaceMega(p)
            );
        }
        else if (generation == 10)
        {
            // Mega Evolutions only
            all = all.Where(p =>
                (Helpers.IsMega(p) || Helpers.IsLumioseMega(p) || Helpers.IsHyperspaceMega(p))
                && !Helpers.IsGmax(p)
            );
        }
        else if (generation > 0)
        {
            var genSet = all.Where(p =>
                p.generation == generation
                && !Helpers.IsMega(p)
                && !Helpers.IsLumioseMega(p)
                && !Helpers.IsHyperspaceMega(p)
            );
            IEnumerable<Pokemon> extras = Enumerable.Empty<Pokemon>();

            if (generation == 6)
            {
                // Exclude megas from Kalos
                genSet = genSet.Where(p =>
                    !Helpers.IsMega(p) && !Helpers.IsLumioseMega(p) && !Helpers.IsHyperspaceMega(p)
                );
                extras = Enumerable.Empty<Pokemon>();
            }
            else if (generation == 8)
            {
                extras = all.Where(p =>
                    Helpers.IsGmax(p)
                    || (Helpers.IsHisui(p) && !Helpers.IsPaldeaExpeditionOrBloodmoon(p))
                );
            }
            else if (generation == 9)
            {
                // Only Paldea Expeditions, no Lumiose Megas or Hyperspace Megas
                extras = all.Where(p => Helpers.IsPaldeaExpedition(p));
            }

            all = genSet.Concat(extras).Distinct();
        }

        if (HasTypeFilter)
        {
            string key = selectedType.ToLowerInvariant();
            all = all.Where(p =>
                p.types != null
                && p.types.Any(t => string.Equals(t, key, StringComparison.OrdinalIgnoreCase))
            );
        }
        else if (GameSettings.TypeFilter != null && GameSettings.TypeFilter.Length > 0)
        {
            var allowed = new HashSet<string>(
                GameSettings.TypeFilter.Select(t => t.Trim().ToLowerInvariant())
            );
            all = all.Where(p =>
                p.types != null && p.types.Any(t => allowed.Contains(t.ToLowerInvariant()))
            );
        }

        List<Pokemon> ordered;

        if (generation == 0 || generation == 10)
        {
            ordered = all.OrderBy(p => p.generation).ThenBy(p => DexOrder.GetIndex(p)).ToList();
        }
        else
        {
            ordered = all.OrderBy(p => DexOrder.GetIndex(p)).ToList();
        }

        targetList = ordered;

        if (generation == 9)
        {
            var taurosForms = ordered.Where(Helpers.IsPaldeaTauros).ToList();
            var taurosOne = taurosForms.FirstOrDefault();
            if (taurosForms.Count > 0)
            {
                ordered.RemoveAll(Helpers.IsPaldeaTauros);
                ordered.Add(taurosOne);
            }

            int iWoo = ordered.FindIndex(p => p.id == 980);
            int iClod = ordered.FindIndex(p => GuessNormalizer.Key(p.name) == "clodsire");

            if (iWoo >= 0 && iClod >= 0 && iWoo != iClod - 1)
            {
                var w = ordered[iWoo];
                ordered.RemoveAt(iWoo);
                iClod = ordered.FindIndex(p => GuessNormalizer.Key(p.name) == "clodsire");
                ordered.Insert(Math.Max(0, iClod), w);
            }

            int iTau = ordered.FindIndex(p => p.baseId == 128 && p.formKey == "paldea");
            int iGra = ordered.FindIndex(p => GuessNormalizer.Key(p.name) == "grafaiai");

            if (iTau >= 0 && iGra >= 0 && iTau != iGra + 1)
            {
                var t = ordered[iTau];
                ordered.RemoveAt(iTau);
                iGra = ordered.FindIndex(p => GuessNormalizer.Key(p.name) == "grafaiai");
                ordered.Insert(Math.Min(ordered.Count, iGra + 1), t);
            }
        }

        targetList = ordered.ToList();
    }

    private void ResetTimerOnly()
    {
        elapsed = 0f;
        SetTimerText();
    }

    private bool HasInQuizContinuation(string text)
    {
        var typed = KeyKeepDigits(text);
        if (string.IsNullOrEmpty(typed))
            return false;

        foreach (var p in PokemonDatabase.Instance.All())
        {
            var nk = KeyKeepDigits(p.name);
            if (nk.Length > typed.Length && nk.StartsWith(typed) && MapToTargetSpecies(p) != null)
            {
                return true;
            }

            if (p.aliases != null)
            {
                foreach (var a in p.aliases)
                {
                    var ak = KeyKeepDigits(a);
                    if (
                        ak.Length > typed.Length
                        && ak.StartsWith(typed)
                        && MapToTargetSpecies(p) != null
                    )
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private void ResetGame()
    {
        _pendingSavedMultiplayerSession = null;
        OnQuizReset?.Invoke();

        if (pauseMenu && pauseMenu.IsShowing)
            pauseMenu.Hide();
        SetGridVisible(true);

        if (giveUpBtn)
            giveUpBtn.interactable = true;

        if (shadowsBtn)
            shadowsBtn.interactable = true;
        if (hintTypeBtn)
            hintTypeBtn.interactable = true;

        if (pauseBtn)
            pauseBtn.interactable = true;
        BuildTargetList();
        RebuildGrid();
        ResetTimerOnly();
        if (guessInput)
        {
            RefocusGuess();
        }
        running = true;
        finishedDialog.Hide();
    }

    private void UpdateScore()
    {
        int total = cardById.Count;
        if (scoreText)
            scoreText.text = $"{solved.Count} / {total}";
    }

    private void ApplyMultiplayerUiState()
    {
        bool multiplayerUi = QuizNetworkRuntime.IsMultiplayerActive || GameSettings.IsMultiplayer;
        ApplyContentTopPadding(preserveScroll: true);
        ApplyMultiplayerRightDock(multiplayerUi, preserveScroll: true);

        if (!multiplayerUi)
            return;

        bool canUseQuizActions = running || (pauseMenu && pauseMenu.IsShowing);
        bool canUseHostActions = !QuizNetworkRuntime.IsMultiplayerClientOnly;

        if (giveUpBtn)
            giveUpBtn.interactable = canUseQuizActions && canUseHostActions;
        if (hintTypeBtn)
            hintTypeBtn.interactable = canUseQuizActions;
        if (shadowsBtn)
            shadowsBtn.interactable = canUseQuizActions;
        if (resetBtn)
            resetBtn.interactable = canUseHostActions;
        if (backToMenuBtn)
            backToMenuBtn.interactable = true;
        if (pauseBtn)
            pauseBtn.interactable = canUseQuizActions;
        if (testBtn)
            testBtn.interactable = canUseQuizActions;

        RefreshButtonVisual(giveUpBtn);
        RefreshButtonVisual(hintTypeBtn);
        RefreshButtonVisual(shadowsBtn);
        RefreshButtonVisual(resetBtn);
        RefreshButtonVisual(backToMenuBtn);
        RefreshButtonVisual(pauseBtn);
        RefreshButtonVisual(testBtn);
    }

    private static void RefreshButtonVisual(Button button)
    {
        if (button && button.TryGetComponent<UiButtonHover>(out var hover))
            hover.RefreshDisabledVisual();
    }

    public List<int> AcceptNetworkGuessOnServer(string currentText, bool suppressLocalInput)
    {
        if (!QuizNetworkRuntime.IsMultiplayerServer)
            return new List<int>();
        if (string.IsNullOrWhiteSpace(currentText))
            return new List<int>();

        var before = solved.ToHashSet();
        bool wasProcessing = _processingNetworkGuess;
        bool wasSuppressing = _suppressInputRefocus;

        LastNetworkGuessFeedback = default;
        _processingNetworkGuess = true;
        _suppressInputRefocus = suppressLocalInput;

        try
        {
            ProcessGuessChanged(currentText);
        }
        finally
        {
            _processingNetworkGuess = wasProcessing;
            _suppressInputRefocus = wasSuppressing;
        }

        return solved.Where(id => !before.Contains(id)).ToList();
    }

    public void ApplyNetworkState(
        int networkGeneration,
        string networkTypeFilter,
        IReadOnlyList<int> solvedIds,
        IReadOnlyList<int> hintedIds,
        IReadOnlyList<int> shadowedIds,
        float networkElapsed,
        bool networkRunning
    )
    {
        var normalizedType = string.IsNullOrWhiteSpace(networkTypeFilter)
            ? null
            : networkTypeFilter.Trim().ToLowerInvariant();

        var solvedSnapshot = solvedIds != null ? new List<int>(solvedIds) : new List<int>();
        var hintedSnapshot = hintedIds != null ? new List<int>(hintedIds) : new List<int>();
        var shadowedSnapshot = shadowedIds != null ? new List<int>(shadowedIds) : new List<int>();

        _pendingSavedMultiplayerSession = null;

        if (_networkStateApplyRoutine != null)
            StopCoroutine(_networkStateApplyRoutine);

        _networkStateApplyRoutine = StartCoroutine(
            CoApplyNetworkState(
                networkGeneration,
                normalizedType,
                solvedSnapshot,
                hintedSnapshot,
                shadowedSnapshot,
                networkElapsed,
                networkRunning
            )
        );
    }

    public void ApplySavedMultiplayerSession(
        IReadOnlyList<int> solvedIds,
        IReadOnlyList<int> hintedIds,
        IReadOnlyList<int> shadowedIds,
        float savedElapsed,
        bool savedRunning
    )
    {
        var snapshot = new SavedQuizSessionSnapshot(
            solvedIds,
            hintedIds,
            shadowedIds,
            savedElapsed,
            savedRunning
        );

        if (!IsReadyForSavedMultiplayerSessionRestore)
        {
            _pendingSavedMultiplayerSession = snapshot;
            elapsed = snapshot.Elapsed;
            SetTimerText();
            running = snapshot.Running;
            if (guessInput)
                guessInput.interactable = running;
            ApplyMultiplayerUiState();
            return;
        }

        ApplySavedMultiplayerSession(snapshot);
    }

    private void ApplySavedMultiplayerSession(SavedQuizSessionSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        _pendingSavedMultiplayerSession = null;

        elapsed = snapshot.Elapsed;
        SetTimerText();
        ApplyNetworkHintState(snapshot.HintedIds, snapshot.ShadowedIds);
        ApplyNetworkSolvedIds(snapshot.SolvedIds, clearInput: false, playSound: false);
        running = snapshot.Running && !IsComplete();
        if (!running && !IsComplete())
            running = true;

        if (guessInput)
            guessInput.interactable = running;

        UpdateScore();
        RefreshNetworkGridLayout();
        ApplyMultiplayerUiState();

        if (running)
            RefocusGuess();
    }

    private void ApplyPendingSavedMultiplayerSessionRestore()
    {
        if (_pendingSavedMultiplayerSession == null || !IsReadyForSavedMultiplayerSessionRestore)
            return;

        ApplySavedMultiplayerSession(_pendingSavedMultiplayerSession);
    }

    private void SaveLocalQuizSession()
    {
        if (!IsLocalQuizSessionAllowed())
            return;

        if (_localSessionDiscarded)
        {
            if (!running)
                return;

            _localSessionDiscarded = false;
        }

        if (solved.Count == 0)
        {
            SingleplayerQuizProgressStore.Remove(generation, selectedType);
            return;
        }

        SingleplayerQuizProgressStore.Save(
            new SingleplayerQuizProgressStore.Session(
                generation,
                selectedType,
                solved,
                hinted,
                shadowed,
                elapsed,
                running
            )
        );
    }

    private bool TryRestoreLocalQuizSession()
    {
        if (!IsLocalQuizSessionAllowed())
            return false;

        if (!SingleplayerQuizProgressStore.TryGet(generation, selectedType, out var session))
            return false;

        if (!session.Matches(generation, selectedType))
            return false;

        _localSessionDiscarded = false;
        ApplySavedMultiplayerSession(
            session.solvedIds,
            session.hintedIds,
            session.shadowedIds,
            session.elapsed,
            session.running
        );
        return true;
    }

    private void ClearLocalQuizSession()
    {
        if (!IsLocalQuizSessionAllowed())
            return;

        _localSessionDiscarded = true;
        SingleplayerQuizProgressStore.Remove(generation, selectedType);
    }

    private static bool IsLocalQuizSessionAllowed()
    {
        return !QuizNetworkRuntime.IsMultiplayerActive && !GameSettings.IsMultiplayer;
    }

    private IEnumerator CoApplyNetworkState(
        int networkGeneration,
        string normalizedType,
        IReadOnlyList<int> solvedIds,
        IReadOnlyList<int> hintedIds,
        IReadOnlyList<int> shadowedIds,
        float networkElapsed,
        bool networkRunning
    )
    {
        StopLocalBuildForNetworkState();

        generation = networkGeneration;
        selectedType = normalizedType;
        GameSettings.Generation = string.IsNullOrEmpty(normalizedType)
            ? networkGeneration
            : (int?)null;
        GameSettings.TypeFilter = string.IsNullOrEmpty(normalizedType) ? null : new[] { normalizedType };

        // Let any pending Destroy calls from an interrupted build clear before making
        // the authoritative multiplayer grid.
        yield return null;

        BuildTargetList();
        RebuildGrid();

        yield return null;

        RefreshNetworkGridLayout();
        elapsed = Mathf.Max(0f, networkElapsed);
        SetTimerText();
        ApplyNetworkHintState(hintedIds, shadowedIds);
        ApplyNetworkSolvedIds(solvedIds, clearInput: false, playSound: false);
        running = networkRunning && !IsComplete();
        RefreshNetworkGridLayout();
        ApplyMultiplayerUiState();
        _networkStateApplyRoutine = null;
    }

    private void StopLocalBuildForNetworkState()
    {
        if (_localBuildRoutine == null)
            return;

        StopCoroutine(_localBuildRoutine);
        _localBuildRoutine = null;
        _loader?.Hide();
    }

    private void RefreshNetworkGridLayout()
    {
        if (!pauseMenu || !pauseMenu.IsShowing)
            SetGridVisible(true);

        ApplyColumnsToAllSections();
        Canvas.ForceUpdateCanvases();

        foreach (var fit in _fits)
        {
            if (fit)
                fit.Recalculate();
        }

        if (scrollRect && scrollRect.content)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);

        Canvas.ForceUpdateCanvases();
    }

    private void ApplyNetworkHintState(
        IReadOnlyList<int> hintedIds,
        IReadOnlyList<int> shadowedIds
    )
    {
        if (hintedIds != null)
            foreach (var id in hintedIds)
                ApplyNetworkTypeHint(id);

        if (shadowedIds != null)
            foreach (var id in shadowedIds)
                ApplyNetworkShadow(id);
    }

    public void ApplyNetworkSolvedIds(
        IReadOnlyList<int> solvedIds,
        bool clearInput,
        bool playSound
    )
    {
        if (solvedIds == null || solvedIds.Count == 0)
        {
            if (clearInput)
                RefocusGuess();
            return;
        }

        bool anyNew = false;

        foreach (var id in solvedIds)
        {
            if (solved.Contains(id))
                continue;
            if (!cardById.TryGetValue(id, out var card))
                continue;

            solved.Add(id);
            card.Reveal();

            if (pokemonById.TryGetValue(id, out var p))
            {
                MaybeScrollTo(p);
                OnPokemonSolved?.Invoke(p);
            }

            anyNew = true;
        }

        if (anyNew)
        {
            if (playSound)
                PlayCorrect();
            UpdateScore();
        }

        if (clearInput)
            RefocusGuess();

        ShowFinishedIfComplete();
    }

    public void ApplyNetworkGuessFeedback(
        MultiplayerGuessFeedback feedback,
        bool clearInput
    )
    {
        if (!feedback.HasValue)
            return;

        switch (feedback.Kind)
        {
            case MultiplayerGuessFeedbackKind.NotInQuiz:
                if (toast && !string.IsNullOrWhiteSpace(feedback.Message))
                    toast.Show(feedback.Message, feedback.Duration);
                if (clearInput)
                    RefocusGuess();
                break;

            case MultiplayerGuessFeedbackKind.AlreadyGuessed:
                Pokemon target = null;
                if (feedback.PokemonId != 0)
                    pokemonById.TryGetValue(feedback.PokemonId, out target);

                if (
                    feedback.PokemonId != 0
                    && cardById.TryGetValue(feedback.PokemonId, out var card)
                )
                {
                    card.FlashHighlight();
                    if (target != null)
                        MaybeScrollTo(target);
                }

                if (toast)
                {
                    string message = !string.IsNullOrWhiteSpace(feedback.Message)
                        ? feedback.Message
                        : $"{(target != null ? target.name : "That Pokémon")} was already guessed";
                    toast.Show(message, feedback.Duration);
                }

                PlayDuplicate();
                if (clearInput)
                    RefocusGuess();
                break;
        }
    }

    public void ApplyNetworkTimer(float networkElapsed, bool networkRunning)
    {
        elapsed = Mathf.Max(0f, networkElapsed);
        running = networkRunning && !IsComplete();
        SetTimerText();

        if (guessInput)
            guessInput.interactable = running;
    }

    public void ApplyNetworkPause(bool paused, float networkElapsed)
    {
        elapsed = Mathf.Max(0f, networkElapsed);
        SetTimerText();

        if (paused)
        {
            if (pauseMenu && pauseMenu.IsShowing)
                return;

            ShowPauseUi();
            return;
        }

        ResumeFromPause();
    }

    public int ApplyNetworkRevealShadow()
    {
        return RevealNextShadow();
    }

    public void ApplyNetworkShadow(int id)
    {
        if (id == 0 || solved.Contains(id) || shadowed.Contains(id))
            return;
        if (!cardById.TryGetValue(id, out var card) || !card)
            return;

        shadowed.Add(id);
        card.SetShadowMode(true);
        _shadowUsedCount++;
        SaveLocalQuizSession();
    }

    public int ApplyNetworkRevealType()
    {
        return RevealTypeHintForOneInternal();
    }

    public void ApplyNetworkTypeHint(int id)
    {
        ApplyTypeHintToId(id);
    }

    public void ApplyNetworkReset()
    {
        ResetGame();
        ApplyMultiplayerUiState();
    }

    public void ApplyNetworkGiveUp()
    {
        RevealAll();
        ApplyMultiplayerUiState();
    }

    private void SetTimerText()
    {
        if (timerText)
            timerText.text = TimeSpan.FromSeconds(elapsed).ToString(@"hh\:mm\:ss");
    }

    private void ShowFinishedIfComplete()
    {
        if (!IsComplete())
            return;

        running = false;
        if (guessInput)
            guessInput.interactable = false;

        ApplyEndStateCardBorders();
        SaveLocalQuizSession();

        if (finishedDialog)
            finishedDialog.Show(
                guessed: solved.Count,
                total: cardById.Count,
                elapsed: TimeSpan.FromSeconds(elapsed),
                gaveUp: false,
                hintsUsed: _hintUsedCount,
                shadowsUsed: _shadowUsedCount
            );
    }

    public void RefreshMultiplayerFinishedDialog(bool gaveUp)
    {
        if (!QuizNetworkRuntime.IsMultiplayerActive || !finishedDialog || !finishedDialog.IsShowing)
            return;

        finishedDialog.Show(
            guessed: solved.Count,
            total: cardById.Count,
            elapsed: TimeSpan.FromSeconds(elapsed),
            gaveUp: gaveUp,
            hintsUsed: _hintUsedCount,
            shadowsUsed: _shadowUsedCount
        );
    }

    void SetSpellingHelp(string name)
    {
        if (!spellingHelpText)
            return;

        if (string.IsNullOrEmpty(name))
        {
            spellingHelpText.text = "";
        }
        else
        {
            spellingHelpText.text = name;
        }
    }

    private string ExplainWhyNotInQuiz(Pokemon p)
    {
        if (p == null)
            return null;

        // Generation mismatch
        if (generation > 0 && p.generation != generation)
        {
            return Helpers.GetGenTitle(p.generation);
        }

        // Type-filtered quiz
        if (HasTypeFilter)
        {
            if (
                p.types == null
                || !p.types.Any(t =>
                    string.Equals(t, selectedType, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var typeList = p.types != null ? string.Join("/", p.types) : "Unknown type";
                return $"Type: {typeList}";
            }
        }

        // Special forms
        if (Helpers.IsMega(p))
            return "Mega Evolution";
        if (Helpers.IsGmax(p))
            return "Gigantamax";
        if (Helpers.IsHisui(p))
            return "Hisui (Gen 8)";
        if (Helpers.IsPaldeaExpedition(p))
            return "Paldea Expedition";
        if (Helpers.IsRegionalForm(p))
            return "Regional form";

        return null;
    }

    string FindClosestPokemonName(string typed)
    {
        if (targetList == null || targetList.Count == 0)
            return null;

        string key = KeyKeepDigits(typed);
        if (string.IsNullOrEmpty(key))
            return null;

        Pokemon bestPokemon = null;
        string bestName = null;
        float bestScore = float.MaxValue;

        foreach (var p in targetList)
        {
            if (!cardById.ContainsKey(p.id))
                continue;

            if (solved.Contains(p.id))
                continue;

            void Consider(string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    return;

                string ck = KeyKeepDigits(candidate);
                if (string.IsNullOrEmpty(ck))
                    return;

                int dist = LevenshteinDistance(key, ck);
                float norm = (float)dist / Mathf.Max(ck.Length, key.Length);

                if (norm < bestScore)
                {
                    bestScore = norm;
                    bestPokemon = p;
                    bestName = candidate;
                }
            }

            Consider(p.name);
            if (p.aliases != null)
                foreach (var a in p.aliases)
                    Consider(a);
        }

        if (bestPokemon == null || bestScore > 0.5f)
            return null;

        return bestName;
    }

    static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        if (n == 0)
            return m;
        if (m == 0)
            return n;

        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
            d[i, 0] = i;
        for (int j = 0; j <= m; j++)
            d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            char s_i = s[i - 1];
            for (int j = 1; j <= m; j++)
            {
                char t_j = t[j - 1];
                int cost = (s_i == t_j) ? 0 : 1;

                d[i, j] = Mathf.Min(
                    Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }

        return d[n, m];
    }

    void UpdateSpellingHelp(string rawText, bool commit)
    {
        if (commit || string.IsNullOrWhiteSpace(rawText) || rawText.Length < 3)
        {
            SetSpellingHelp(null);
            return;
        }

        var exact = FindByExactPreserveDigits(rawText);
        if (exact != null)
        {
            var mapped = MapToTargetSpecies(exact);
            if (mapped != null && solved.Contains(mapped.id))
            {
                SetSpellingHelp(null);
                return;
            }
        }

        var suggestion = FindClosestPokemonName(rawText);
        if (suggestion == null)
        {
            SetSpellingHelp(null);
            return;
        }

        SetSpellingHelp(suggestion);
    }

    private void OnGuessChanged(string currentText)
    {
        if (!_processingNetworkGuess && QuizMultiplayerCoordinator.IsActive)
        {
            HandleMultiplayerLocalGuess(currentText);
            return;
        }

        ProcessGuessChanged(currentText);
    }

    private void HandleMultiplayerLocalGuess(string currentText)
    {
        if (!running || IsDialogOpen())
            return;
        if (string.IsNullOrWhiteSpace(currentText))
            return;

        bool commit = char.IsWhiteSpace(currentText[^1]);
        string raw = commit ? currentText.TrimEnd() : currentText;

        if (_spellingHelpEnabled)
            UpdateSpellingHelp(raw, commit);
        else
            SetSpellingHelp(null);

        QuizMultiplayerCoordinator.SubmitGuess(currentText);

        if (commit)
            RefocusGuess();
    }

    private void ProcessGuessChanged(string currentText)
    {
        if (!running || IsDialogOpen())
            return;
        if (string.IsNullOrWhiteSpace(currentText))
            return;

        bool commit = char.IsWhiteSpace(currentText[^1]);
        string raw = commit ? currentText.TrimEnd() : currentText;
        if (_spellingHelpEnabled)
            UpdateSpellingHelp(raw, commit);
        else
            SetSpellingHelp(null);
        if (generation == 8 || generation == 0 && commit)
        {
            if (TryAcceptGmaxByBaseName(currentText.Trim(), commit: true))
                return;
            if (TryAcceptHisuiByBaseName(currentText.Trim(), commit: true))
                return;
        }

        if (generation == 10 && TryAcceptAnyMegaByBaseName(currentText.Trim(), commit: true))
            return;

        if (
            generation == 9
            && commit
            && TryAcceptExpeditionByBaseName(currentText.Trim(), commit: true)
        )
            return;

        if (
            (generation == 9 || generation == 0)
            && commit
            && TryAcceptHyperspaceByBaseName(currentText.Trim(), commit: true)
        )
            return;

        if (generation == 6)
        {
            var k = GuessNormalizer.Key(currentText.Trim());
            if (
                !string.IsNullOrEmpty(k)
                && megaByBaseName.TryGetValue(k, out var ids)
                && ids.Count > 0
            )
            {
                Pokemon baseSpecies = null;
                foreach (var p in PokemonDatabase.Instance.All())
                {
                    bool isBase = p.baseId == 0 || p.baseId == p.id;
                    if (!isBase)
                        continue;

                    if (
                        GuessNormalizer.Key(p.name) == k
                        || (p.aliases != null && p.aliases.Any(a => GuessNormalizer.Key(a) == k))
                    )
                    {
                        baseSpecies = p;
                        break;
                    }
                }

                int baseId =
                    baseSpecies != null
                        ? (baseSpecies.baseId != 0 ? baseSpecies.baseId : baseSpecies.id)
                        : 0;
                bool baseInMain =
                    baseId != 0
                    && targetList.Any(p =>
                        !Helpers.IsMega(p) && (p.baseId != 0 ? p.baseId : p.id) == baseId
                    );

                if (!baseInMain)
                {
                    int pickId = ids[UnityEngine.Random.Range(0, ids.Count)];
                    var pick = PokemonDatabase.Instance.All().FirstOrDefault(p => p.id == pickId);
                    if (pick != null)
                    {
                        HandleCandidate(pick, currentText, commit: true);
                        return;
                    }
                }
            }
        }

        var keyOnly = GuessNormalizer.Key(currentText.Trim());
        if (keyOnly == "nidoran")
        {
            RevealByBaseIds(29, 32);
            return;
        }

        if (commit)
        {
            var ov = ExactNameOverrides.TryGet(raw);
            if (ov != null)
            {
                HandleCandidate(ov, raw, true);
                return;
            }
        }
        TryAcceptWithDisambiguation(raw, commit);
    }

    private static string KeyKeepDigits(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;
        s = s.Trim().ToLowerInvariant();

        s = s.Replace("é", "e");

        System.Text.StringBuilder sb = new();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                continue;
            }
        }
        return sb.ToString();
    }

    private static Pokemon FindByExactPreserveDigits(string text)
    {
        var ov = ExactNameOverrides.TryGet(text);
        if (ov != null)
            return ov;
        var k = KeyKeepDigits(text);
        if (string.IsNullOrEmpty(k))
            return null;

        Pokemon best = null;
        int bestLen = -1;

        foreach (var p in PokemonDatabase.Instance.All())
        {
            var pk = KeyKeepDigits(p.name);
            if (pk == k && pk.Length > bestLen)
            {
                best = p;
                bestLen = pk.Length;
            }

            if (p.aliases != null)
                foreach (var a in p.aliases)
                {
                    var ak = KeyKeepDigits(a);
                    if (ak == k && ak.Length > bestLen)
                    {
                        best = p;
                        bestLen = ak.Length;
                    }
                }
        }
        return best;
    }

    private void TryAcceptWithDisambiguation(string text, bool commit)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var exact = FindByExactPreserveDigits(text);
        if (exact != null)
        {
            if (cardById.ContainsKey(exact.id))
            {
                HandleCandidate(exact, text, commit);
                return;
            }

            var mapped = MapToTargetSpecies(exact);
            if (mapped != null)
            {
                HandleCandidate(mapped, text, commit);
                return;
            }

            if (!commit)
            {
                if (HasInQuizContinuation(text))
                    return;
                HandleCandidate(exact, text, true);
                return;
            }

            HandleCandidate(exact, text, true);
            return;
        }

        if (generation == 10 && TryAcceptAnyMegaByBaseName(text, commit))
            return;

        if (!commit)
            return;

        var fuzzy = PokemonDatabase.Instance.FindByGuess(text);
        if (fuzzy == null)
            return;

        HandleCandidate(fuzzy, text, commit);
    }

    private void HandleCandidate(Pokemon guess, string originalText, bool commit)
    {
        var target = MapToTargetSpecies(guess);

        if (target == null)
        {
            var key = GuessNormalizer.Key(originalText);
            bool exactTyped = IsExactNameOrAlias(originalText, guess);
            if ((commit && exactTyped) || (exactTyped && key.Length >= MIN_TOAST_LEN))
                ShowNotInQuiz(guess.name, guess);
            return;
        }

        if (solved.Contains(target.id))
        {
            bool exact = IsExactNameOrAlias(originalText, target);

            if (!commit)
            {
                if (exact && !HasDifferentSpeciesContinuation(originalText, target))
                {
                    CaptureAlreadyGuessedFeedback(target);
                    if (cardById.TryGetValue(target.id, out var c))
                    {
                        c.FlashHighlight();
                        MaybeScrollTo(target);
                    }
                    PlayDuplicate();
                    RefocusGuess();
                    return;
                }

                if (cardById.TryGetValue(target.id, out var soft))
                    soft.FlashHighlight();
                return;
            }

            CaptureAlreadyGuessedFeedback(target);
            if (cardById.TryGetValue(target.id, out var already))
            {
                already.FlashHighlight();
                MaybeScrollTo(target);
            }
            PlayDuplicate();
            RefocusGuess();
            return;
        }

        bool isExactForTarget = IsExactNameOrAlias(originalText, target);
        if (!isExactForTarget)
        {
            var ambKey = GuessNormalizer.Key(originalText);
            bool ambiguous = IsAmbiguousPrefix(ambKey, target);
            if (ambiguous && !commit)
                return;
        }

        solved.Add(target.id);
        if (cardById.TryGetValue(target.id, out var card))
        {
            card.Reveal();
            MaybeScrollTo(target);
        }
        PlayCorrect();
        OnPokemonSolved?.Invoke(target);

        if (generation == 0 || generation == 9)
        {
            int baseKey = target.baseId != 0 ? target.baseId : target.id;

            RevealAllVariantsForBase(baseKey, includeExpeditions: true);
        }

        UpdateScore();
        SaveLocalQuizSession();
        RefocusGuess();

        ShowFinishedIfComplete();
    }

    private void RevealAllByBaseId(int baseId)
    {
        bool any = false;

        var matches = targetList.Where(p => (p.baseId != 0 ? p.baseId : p.id) == baseId).ToList();

        foreach (var target in matches)
        {
            if (solved.Contains(target.id))
            {
                if (cardById.TryGetValue(target.id, out var already))
                    already.FlashHighlight();
                continue;
            }

            solved.Add(target.id);

            if (cardById.TryGetValue(target.id, out var card))
            {
                card.Reveal();
                MaybeScrollTo(target);
            }

            any = true;
        }

        if (any)
        {
            UpdateScore();
            SaveLocalQuizSession();
        }

        RefocusGuess();

        ShowFinishedIfComplete();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            PauseDueToFocusLoss();
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            PauseDueToFocusLoss();
        }
    }

    private void PauseDueToFocusLoss()
    {
        if (QuizNetworkRuntime.IsMultiplayerActive)
            return;

        if (!_pauseOnFocusLossEnabled)
            return;

        if (!running)
            return;

        if (IsDialogOpen())
            return;

        if (pauseMenu && pauseMenu.IsShowing)
            return;

        PauseGame();
    }

    bool HasDifferentSpeciesContinuation(string text, Pokemon currentTarget)
    {
        var typed = KeyKeepDigits(text);
        if (string.IsNullOrEmpty(typed))
            return false;

        int targetBase = currentTarget.baseId != 0 ? currentTarget.baseId : currentTarget.id;

        foreach (var p in targetList)
        {
            if (p.id == currentTarget.id)
                continue;

            var nk = KeyKeepDigits(p.name);
            if (nk.Length <= typed.Length)
                continue;
            if (!nk.StartsWith(typed))
                continue;

            int pBase = p.baseId != 0 ? p.baseId : p.id;
            if (pBase == targetBase)
                continue;

            return true;
        }
        return false;
    }

    IEnumerator CoSmartScrollTo(RectTransform target, float duration, int token)
    {
        if (!target)
            yield break;

        for (int i = 0; i < 3; i++)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
        }
        if (token != _scrollToken)
            yield break;

        var sr = scrollRect;
        float contentH = sr.content.rect.height;
        float viewH = sr.viewport.rect.height;
        if (contentH <= viewH)
            yield break;

        bool oldInertia = sr.inertia;
        Vector2 oldVel = sr.velocity;
        sr.inertia = false;
        sr.StopMovement();
        sr.velocity = Vector2.zero;

        float start = sr.verticalNormalizedPosition;
        float t = 0f;

        while (t < duration && token == _scrollToken)
        {
            t += Time.unscaledDeltaTime;
            float targetNorm = CalcTargetNorm(sr, target);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            sr.verticalNormalizedPosition = Mathf.Lerp(start, targetNorm, k);
            yield return null;
        }

        if (token == _scrollToken)
            sr.verticalNormalizedPosition = CalcTargetNorm(sr, target);

        sr.inertia = oldInertia;
        sr.velocity = oldVel;
    }

    static float CalcTargetNorm(ScrollRect sr, RectTransform target)
    {
        var viewport = sr.viewport;
        var content = sr.content;

        var contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
            viewport,
            content
        );
        var targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
            viewport,
            target
        );

        float contentH = contentBounds.size.y;
        float viewH = viewport.rect.height;
        float scrollable = Mathf.Max(1f, contentH - viewH);

        float contentTopY = contentBounds.center.y + contentBounds.extents.y;
        float targetTopY = targetBounds.center.y + targetBounds.extents.y;

        float fromTopPx = contentTopY - targetTopY;

        float padPx = 0.10f * viewH;
        fromTopPx = Mathf.Clamp(fromTopPx - padPx, 0f, scrollable);

        return 1f - (fromTopPx / scrollable);
    }

    private static bool IsExactNameOrAlias(string text, Pokemon p)
    {
        var k = GuessNormalizer.Key(text);
        if (string.IsNullOrEmpty(k) || p == null)
            return false;

        if (GuessNormalizer.Key(p.name) == k)
            return true;
        if (p.aliases != null)
            foreach (var a in p.aliases)
                if (GuessNormalizer.Key(a) == k)
                    return true;

        return false;
    }

    private bool IsAmbiguousPrefix(string key, Pokemon exact)
    {
        foreach (var p in targetList)
        {
            if (p.id == exact.id || solved.Contains(p.id))
                continue;

            if (GuessNormalizer.Key(p.name).StartsWith(key))
                return true;

            if (p.aliases != null)
                foreach (var a in p.aliases)
                    if (GuessNormalizer.Key(a).StartsWith(key))
                        return true;
        }
        return false;
    }

    private void OnGiveUpClicked()
    {
        if (QuizNetworkRuntime.IsMultiplayerClientOnly)
        {
            ApplyMultiplayerUiState();
            return;
        }

        if (IsDialogOpen())
            return;

        void DoGiveUp()
        {
            DefocusUI();
            if (QuizMultiplayerCoordinator.RequestGiveUp())
                return;

            RevealAll();
            if (giveUpBtn)
                giveUpBtn.interactable = false;
        }

        if (!confirmDialog)
        {
            DoGiveUp();
            return;
        }

        confirmDialog.Show(
            title: "Give up?",
            message: "This will reveal every Pokémon and stop the timer.",
            confirmLabel: "Reveal all",
            cancelLabel: "Cancel",
            confirmAction: DoGiveUp
        );
    }

    private void RevealAll()
    {
        ClearLocalQuizSession();
        var guessedIds = new HashSet<int>(solved);

        foreach (var kv in cardById)
        {
            var card = kv.Value;
            card.Reveal();
        }

        ApplyEndStateCardBorders(guessedIds);

        UpdateScore();
        running = false;
        if (guessInput)
            guessInput.interactable = false;

        if (shadowsBtn)
        {
            shadowsBtn.interactable = false;
            shadowsBtn.GetComponent<UiButtonHover>().RefreshDisabledVisual();
        }

        if (hintTypeBtn)
            hintTypeBtn.interactable = false;

        if (pauseBtn)
            pauseBtn.interactable = false;

        finishedDialog.Show(
            guessed: guessedIds.Count,
            total: cardById.Count,
            elapsed: TimeSpan.FromSeconds(elapsed),
            gaveUp: true,
            hintsUsed: _hintUsedCount,
            shadowsUsed: _shadowUsedCount
        );
        Canvas.ForceUpdateCanvases();
        StartCoroutine(RecalculateAfterLayout());
    }

    private void ApplyEndStateCardBorders(HashSet<int> guessedIds = null)
    {
        _endStateBordersShowing = true;
        guessedIds ??= new HashSet<int>(solved);

        foreach (var kv in cardById)
        {
            int id = kv.Key;
            var card = kv.Value;
            if (!card)
                continue;

            bool guessed = guessedIds.Contains(id);
            card.ShowEndState(QuizMultiplayerCoordinator.GetEndStateColorForPokemon(id, guessed));
        }
    }

    public void RefreshMultiplayerEndStateColors()
    {
        if (_endStateBordersShowing)
            ApplyEndStateCardBorders();
    }

    IEnumerator RecalculateAfterLayout()
    {
        yield return null;
        foreach (var fit in _fits)
        {
            if (fit)
                fit.Recalculate();
        }
    }

    public IEnumerator BuildWithExternalProgress(Action<float> report, float from, float to)
    {
        float span = Mathf.Max(0.0001f, to - from);
        void Step(float k) => report(from + k * span);

        BuildTargetList();
        Step(0.25f);
        yield return null;

        Step(0.40f);
        yield return null;

        bool multiplayerUi = QuizNetworkRuntime.IsMultiplayerActive || GameSettings.IsMultiplayer;
        ApplyContentTopPadding(preserveScroll: false);
        ApplyMultiplayerRightDock(multiplayerUi, preserveScroll: false);
        RebuildGrid();
        TryRestoreLocalQuizSession();
        if (!pauseMenu || !pauseMenu.IsShowing)
            SetGridVisible(true);
        Step(0.90f);
        yield return null;

        ApplyColumnsToAllSections();
        Canvas.ForceUpdateCanvases();

        UpdateScore();
        ApplyMultiplayerUiState();
        QuizMultiplayerCoordinator.Attach(this);
        Step(1.00f);
        yield return null;
    }
}
