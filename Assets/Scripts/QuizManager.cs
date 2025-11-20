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
    public TMP_Text scoreText;
    public TMP_Text timerText;
    public Button giveUpBtn;

    [SerializeField]
    private Slider cardSizeSlider;

    [SerializeField]
    private TMP_Text cardSizeLabel; // optional

    [SerializeField]
    private int minColsLarge = 6; // few columns = big cards

    [SerializeField]
    private int maxColsSmall = 30; // many columns = small cards
    private int currentCols;

    private readonly List<GridAutoFit> _fits = new();
    const string KEY_COLS = "card_cols";
    public FinishedDialog finishedDialog;

    [Header("Grid")]
    public PokemonCard cardPrefab;

    [Header("Loader")]
    public LoadingManager loaderPrefab; // <- drag your LoadingOverlay prefab here
    private LoadingManager _loader; // scene instance we create/use

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
    private float elapsed;
    private bool running;
    public ScrollRect scrollRect;

    public Toast toast;
    private const int MIN_TOAST_LEN = 4;

    public SectionHeader sectionHeaderPrefab;
    public SectionGroup sectionGroupPrefab;
    public Transform content;
    private int _buildToken;

    [Header("Dev/Test")]
    public Button testBtn; // assign in Inspector (can hide it in builds)
    public bool testIncludeAliases; // optional: also try aliases
    public float testDelay = 0.02f; // seconds between entries

    [SerializeField]
    Toggle alwaysScrollToggle;
    Coroutine _scrollRoutine;
    int _scrollToken;
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
            ); // "Water", "Bug"
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
    private readonly Dictionary<int, PokemonCard> lumioseCardByBase = new();
    private readonly Dictionary<string, int> lumioseByBaseName = new();
    private readonly HashSet<int> shadowed = new();

    bool _shadowMode;
    private bool _testRunning;
    private bool _testCancel;

    [Header("Audio")]
    [SerializeField]
    AudioSource sfx; // drag an AudioSource here

    [SerializeField]
    AudioClip correctSfx; // “ding”

    [SerializeField]
    AudioClip duplicateSfx; // “already guessed”

    [SerializeField]
    AudioClip backgroundMusic; // “already guessed”

    [SerializeField, Range(0f, 1f)]
    float sfxVolume = 1f;

    [Header("Pause UI")]
    [SerializeField]
    PauseMenu pauseMenu;

    [SerializeField]
    CanvasGroup gridGroup; // assign to the ScrollRect's root container (or the ScrollRect itself)

    [SerializeField]
    Button pauseBtn; // optional top-bar "Pause" button

    [SerializeField]
    Button shadowsBtn;

    private void Awake()
    {
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
                    _testCancel = true; // pressing again cancels
            });
        }
        if (pauseBtn)
        {
            pauseBtn.onClick.RemoveAllListeners();
            pauseBtn.onClick.AddListener(TogglePause);
        }

        if (pauseMenu)
        {
            pauseMenu.OnResume = ResumeFromPause;
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

        running = false;
        guessInput.DeactivateInputField();
        if (guessInput)
            guessInput.interactable = false;

        pauseMenu.SetElapsed(System.TimeSpan.FromSeconds(elapsed));

        SetGridVisible(false);
        pauseMenu.Show();
    }

    void ResumeFromPause()
    {
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

    void RefocusGuess()
    {
        if (!guessInput)
            return;
        guessInput.SetTextWithoutNotify(string.Empty);
        if (!guessInput.interactable)
            guessInput.interactable = true;
        guessInput.ActivateInputField();
        guessInput.Select();
    }

    public void TogglePause()
    {
        if (!pauseMenu)
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

        var testList = pokemonById
            .Values.Distinct()
            .OrderBy(p => DexOrder.GetIndex(p)) // nice, stable order
            .ToList();

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
        vlg.padding = new RectOffset(vlg.padding.left, vlg.padding.right, 32, vlg.padding.bottom);
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

        EnsureLoader();

        bool hasRouterParams =
            LoadingManager.Instance
            && (
                LoadingManager.Instance.PendingGen != 0
                || !string.IsNullOrEmpty(LoadingManager.Instance.PendingType)
            );

        if (!hasRouterParams)
            StartCoroutine(LocalBuildWithOverlay()); // <— new helper below
        UpdateTypeHintButtonVisibility();
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
#else
        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
        {
            if (IsDialogOpen())
                return;
            TogglePause();
        }
#endif

        if (!IsDialogOpen() && running)
        {
            elapsed += Time.deltaTime;
            if (timerText)
                timerText.text = TimeSpan.FromSeconds(elapsed).ToString(@"hh\:mm\:ss");
        }
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
            UpdateScore();

        guessInput?.SetTextWithoutNotify(string.Empty);
        guessInput?.ActivateInputField();
        guessInput?.Select();

        if (IsComplete())
        {
            running = false;
            if (guessInput)
                guessInput.interactable = false;
            if (finishedDialog)
                finishedDialog.Show(
                    solved.Count,
                    cardById.Count,
                    TimeSpan.FromSeconds(elapsed),
                    gaveUp: false,
                    hintsUsed: _hintUsedCount,
                    shadowsUsed: _shadowUsedCount
                );
        }
    }

    private void ShowNotInQuiz(string name)
    {
        toast.Show($"{name} is not part of this quiz", 2f);

        if (guessInput)
        {
            RefocusGuess();
        }
    }

    void MaybeScrollTo(Pokemon p, float duration = 0.25f)
    {
        if (!scrollRect || !scrollRect.content || !scrollRect.viewport)
            return;
        if (p == null)
            return;
        if (!cardById.TryGetValue(p.id, out var card) || !card)
            return;

        if (alwaysScrollToggle && alwaysScrollToggle.isOn)
        {
            if (_scrollRoutine != null)
            {
                StopCoroutine(_scrollRoutine);
            }

            _scrollRoutine = StartCoroutine(
                CoSmartScrollTo(card.GetComponent<RectTransform>(), duration, ++_scrollToken)
            );
        }
    }

    public void OnResetClicked()
    {
        DefocusUI();
        if (!confirmDialog)
        {
            ResetGame();
            return;
        }

        confirmDialog.Show(
            title: "Reset quiz?",
            message: "This will clear all revealed Pokémon and restart the timer.",
            confirmLabel: "Reset",
            cancelLabel: "Cancel",
            confirmAction: ResetGame
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

            if (generation == 6 && megaSlotPickByBase.TryGetValue(baseId, out var megaPick))
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
            || Helpers.IsLumioseMega(guess);

        if (isForm)
        {
            int baseId = guess.baseId != 0 ? guess.baseId : guess.id;

            if (generation == 6 && megaSlotPickByBase.TryGetValue(baseId, out var megaPick))
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

        static void LeaveNow()
        {
            LoadingManager.Instance?.CancelLoad(); // hides overlay + clears flags
            SceneManager.LoadScene("MainMenu");
        }

        if (!confirmDialog)
        {
            LeaveNow();
            return;
        }

        confirmDialog.Show(
            title: "Leave quiz?",
            message: "Your progress will be lost. Go back to the main menu?",
            confirmLabel: "Yes, leave",
            cancelLabel: "Stay",
            confirmAction: LeaveNow
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

    private bool TryAcceptMegaByBaseName(string text, bool commit)
    {
        if (generation != 6)
            return false;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var norm = GuessNormalizer.Key(text);

        Pokemon baseSpecies = null;
        foreach (var p in PokemonDatabase.Instance.All())
        {
            var isBase = p.baseId == 0 || p.baseId == p.id;
            if (!isBase)
                continue;

            if (GuessNormalizer.Key(p.name) == norm)
            {
                baseSpecies = p;
                break;
            }

            if (p.aliases != null)
            {
                foreach (var a in p.aliases)
                {
                    if (GuessNormalizer.Key(a) == norm)
                    {
                        baseSpecies = p;
                        break;
                    }
                }
                if (baseSpecies != null)
                    break;
            }
        }

        if (baseSpecies == null)
            return false;

        int baseId = baseSpecies.baseId != 0 ? baseSpecies.baseId : baseSpecies.id;
        if (!megaSlotPickByBase.TryGetValue(baseId, out var pickedMega))
            return false;

        HandleCandidate(
            baseSpecies,
            text,
            commit /* doesn't matter; handler maps to mega */
        );
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
        StopAllCoroutines();
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
    }

    private void RebuildGrid()
    {
        _fits.Clear();
        _sections.Clear();
        _buildToken++;
        _hintUsedCount = 0;
        _shadowUsedCount = 0;
        StopAllCoroutines();
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
        cardById.Clear();
        pokemonById.Clear();
        solved.Clear();
        hinted.Clear();
        shadowed.Clear();
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

        var ordered = targetList;

        var main = Instantiate(sectionGroupPrefab, content);
        main.EnsureLayout();
        SetMainTitle(main);
        _sections.Add(main);

        SectionGroup megaKalosGen = null,
            megaHoennGen = null,
            paldeaExpeditions = null,
            gmaxSec = null,
            hisuiSec = null,
            lumioseMegasSec = null,
            alolaUnknown = null;

        var allDb = PokemonDatabase.Instance.All();

        foreach (var m in allDb.Where(Helpers.IsMega).Where(MatchesType))
        {
            if (Helpers.IsLumioseMega(m))
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
            FinalizeSection(unknownSec);
            UpdateScore();
            return;
        }

        if (generation == 6)
        {
            megaKalosGen = Instantiate(sectionGroupPrefab, content);
            megaKalosGen.EnsureLayout();
            megaKalosGen.SetTitle("Mega Evolution - Kalos", false);

            megaHoennGen = Instantiate(sectionGroupPrefab, content);
            megaHoennGen.EnsureLayout();
            megaHoennGen.SetTitle("Mega Evolution - Hoenn", false);
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

            lumioseMegasSec = Instantiate(sectionGroupPrefab, content);
            lumioseMegasSec.EnsureLayout();
            lumioseMegasSec.SetTitle("Mega Evolution - Lumiose", false);
        }

        var expeditionPool = new List<Pokemon>();
        var gmaxPoolGen = new List<Pokemon>();
        var hisuiPoolGen = new List<Pokemon>();
        var lumioseMegaPool = new List<Pokemon>();
        var alolaUnknownPool = new List<Pokemon>();

        foreach (var p in ordered)
        {
            if (generation == 6 && Helpers.IsMega(p))
            {
                int baseKey = BaseIdOf(p);
                if (!megaFormsByBase.TryGetValue(baseKey, out var list))
                    megaFormsByBase[baseKey] = list = new List<Pokemon>();
                list.Add(p);
                continue;
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

            if (generation == 9 && Helpers.IsLumioseMega(p))
            {
                lumioseMegaPool.Add(p);
                continue;
            }

            var card = Instantiate(cardPrefab, main.gridRoot);
            card.ClearEndState();
            card.Bind(p);
            cardById[p.id] = card;
            pokemonById[p.id] = p;
        }

        if (generation == 6 && (megaKalosGen != null || megaHoennGen != null))
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
        if (alolaUnknown != null)
        {
            foreach (var p in alolaUnknownPool.OrderBy(x => DexOrder.GetIndex(x)))
            {
                var c = Instantiate(cardPrefab, alolaUnknown.gridRoot);
                c.ClearEndState();
                c.Bind(p);
                cardById[p.id] = c;
                pokemonById[p.id] = p;
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
        if (lumioseMegasSec != null)
        {
            foreach (var p in lumioseMegaPool.OrderBy(x => x.id))
            {
                var c = Instantiate(cardPrefab, lumioseMegasSec.gridRoot);
                c.ClearEndState();
                c.Bind(p);
                cardById[p.id] = c;
                pokemonById[p.id] = p;

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

            lumioseMegasSec.SetCardCount(lumioseMegasSec.gridRoot.childCount);
            FitSection(lumioseMegasSec);
        }
        UpdateScore();
        bool noSubSections =
            generation > 0
            && megaHoennGen == null
            && megaKalosGen == null
            && gmaxSec == null
            && hisuiSec == null
            && paldeaExpeditions == null;

        main.SetHeaderGap(noSubSections);
    }

    bool HasTypeFilter => !string.IsNullOrEmpty(selectedType);

    void ApplyShadowMode(bool enable, bool lockButton = false)
    {
        _shadowMode = enable;

        foreach (var card in cardById.Values)
        {
            if (card)
                card.SetShadowMode(enable);
        }
        if (hintTypeBtn)
            hintTypeBtn.interactable = !enable;

        if (shadowsBtn && lockButton)
        {
            shadowsBtn.interactable = false;
            shadowsBtn.GetComponent<UiButtonHover>().RefreshDisabledVisual();
        }
    }

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
        UpdateTypeHintButtonVisibility();
        StopAllCoroutines();
    }

    public void StartGenQuiz(int gen)
    {
        selectedType = null;
        GameSettings.TypeFilter = null;
        generation = gen;
        UpdateTypeHintButtonVisibility();
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
                || Helpers.IsGmax(p)
                || Helpers.IsHisui(p)
                || Helpers.IsRegionalForm(p)
                || // <- regional forms only
                (includeExpeditions && Helpers.IsPaldeaExpeditionOrBloodmoon(p));

            if (!isVariant)
                continue;

            if (!solved.Contains(kv.Key))
            {
                solved.Add(kv.Key);
                kv.Value.Reveal();
            }
        }
    }

    private void RevealNextShadow()
    {
        // 1) Prefer cards that currently have a type hint
        Pokemon pick = null;

        foreach (var p in targetList)
        {
            if (solved.Contains(p.id) || shadowed.Contains(p.id))
                continue;

            if (cardById.TryGetValue(p.id, out var card) && card && card.HintVisible)
            {
                pick = p;
                break;
            }
        }

        // 2) If none have a hint, pick first unsolved & unshadowed in dex order
        pick ??= targetList.FirstOrDefault(p => !solved.Contains(p.id) && !shadowed.Contains(p.id));

        if (pick == null)
            return; // nothing left to shadow

        shadowed.Add(pick.id);

        if (cardById.TryGetValue(pick.id, out var targetCard) && targetCard)
        {
            targetCard.SetShadowMode(true);
            _shadowUsedCount++;
        }
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
        ApplyColumnsToAllSections();
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

    private void ApplyColumnsToAllSections()
    {
        if (cardSizeLabel)
            cardSizeLabel.text = $"{currentCols} cols"; // optional

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
    }

    void UpdateTypeHintButtonVisibility()
    {
        //if (hintTypeBtn)
        // hintTypeBtn.gameObject.SetActive(!HasTypeFilter); // hide when doing a TYPE quiz
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

        fit.Recalculate(); // safe now
    }

    private void RevealTypeHintForOne()
    {
        // Only pick cards that are:
        // - not solved
        // - not already hinted
        // - not already shadowed  <-- NEW
        var pool = targetList
            .Where(p =>
                !solved.Contains(p.id) && !hinted.Contains(p.id) && !shadowed.Contains(p.id)
            )
            .ToList();

        if (pool.Count == 0)
            return;

        var pick = pool[0];
        hinted.Add(pick.id);

        if (!cardById.TryGetValue(pick.id, out var card) || card == null)
        {
            Debug.LogWarning($"[Hint] No card for id {pick.id}.");
            return;
        }

        card.ShowTypeHint(pick.types);
        _hintUsedCount++;
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
            );
        }
        else if (generation > 0)
        {
            var genSet = all.Where(p => p.generation == generation);
            IEnumerable<Pokemon> extras = Enumerable.Empty<Pokemon>();

            if (generation == 6)
            {
                var megasDistinctByBase = all.Where(p =>
                        Helpers.IsMega(p) && !Helpers.IsLumioseMega(p)
                    )
                    .GroupBy(p => p.baseId != 0 ? p.baseId : p.id)
                    .Select(g => g.First());
                extras = megasDistinctByBase;
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
                extras = all.Where(p => Helpers.IsPaldeaExpedition(p) || Helpers.IsLumioseMega(p));
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

        // Full quiz & type quizzes (generation == 0) → mix of all gens
        if (generation == 0)
        {
            ordered = all.OrderBy(p => p.generation) // primary: gen 1 → 9
                .ThenBy(p => DexOrder.GetIndex(p)) // secondary: custom per-gen order
                .ToList();
        }
        else
        {
            // Single-gen quizzes already filtered by gen,
            // so we just keep your existing DexOrder logic.
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
        if (timerText)
            timerText.text = "00:00:00";
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

    private void OnGuessChanged(string currentText)
    {
        if (!running || IsDialogOpen())
            return;
        if (string.IsNullOrWhiteSpace(currentText))
            return;

        bool commit = char.IsWhiteSpace(currentText[^1]);
        string raw = commit ? currentText.TrimEnd() : currentText;
        if (generation == 8 || generation == 0 && commit)
        {
            if (TryAcceptGmaxByBaseName(currentText.Trim(), commit: true))
                return;
            if (TryAcceptHisuiByBaseName(currentText.Trim(), commit: true))
                return;
        }

        if (
            generation == 9
            && commit
            && TryAcceptExpeditionByBaseName(currentText.Trim(), commit: true)
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

        if (!commit && TryAcceptMegaByBaseName(text, commit: false))
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
                ShowNotInQuiz(guess.name);
            return;
        }

        if (solved.Contains(target.id))
        {
            bool exact = IsExactNameOrAlias(originalText, target);

            if (!commit)
            {
                if (exact && !HasDifferentSpeciesContinuation(originalText, target))
                {
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

        if (generation == 0)
        {
            int baseKey = target.baseId != 0 ? target.baseId : target.id;

            RevealAllVariantsForBase(baseKey, includeExpeditions: false);
        }

        UpdateScore();
        RefocusGuess();

        if (IsComplete())
        {
            running = false;
            if (guessInput)
                guessInput.interactable = false;

            finishedDialog.Show(
                guessed: solved.Count,
                total: cardById.Count,
                elapsed: TimeSpan.FromSeconds(elapsed),
                gaveUp: false,
                hintsUsed: _hintUsedCount,
                shadowsUsed: _shadowUsedCount
            );
        }
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
        if (IsDialogOpen())
            return;

        void DoGiveUp()
        {
            DefocusUI();
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
        var guessedIds = new HashSet<int>(solved);

        foreach (var kv in cardById)
        {
            int id = kv.Key;
            var card = kv.Value;

            card.Reveal();

            bool guessed = guessedIds.Contains(id);
            card.ShowEndState(guessed);
        }

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

        RebuildGrid();
        Step(0.90f);
        yield return null;

        ApplyColumnsToAllSections();
        Canvas.ForceUpdateCanvases();

        UpdateScore();
        Step(1.00f);
        yield return null;
    }
}
