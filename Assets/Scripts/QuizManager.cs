using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static UnityEngine.Rendering.CoreUtils;

public class QuizManager : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField guessInput;
    public TMP_Text scoreText;
    public TMP_Text timerText;
    public Toggle dexOrderToggle;
    public Toggle noTimerToggle;
    public TMP_InputField minutesInput;

    [Header("Grid")]
    public PokemonCard cardPrefab;

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
    private const string SecretRevealAll = "revealall";

    private bool IsDialogOpen() => confirmDialog && confirmDialog.IsShowing;

    public Toast toast;
    private const int MIN_TOAST_LEN = 4;
    public TMP_Text quizTitle;
    public SectionHeader sectionHeaderPrefab;
    public SectionGroup sectionGroupPrefab;
    public Transform content;
    private List<SectionGroup> _builtSections = new();
    private Vector2 _lastVpSize;

    private readonly Dictionary<int, List<Pokemon>> megaFormsByBase = new();
    private readonly Dictionary<int, Pokemon> megaSlotPickByBase = new();
    private readonly Dictionary<int, PokemonCard> megaCardByBase = new();

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
        StartCoroutine(SpriteLibrary.Instance.PreloadAsync(targetList.Select(t => t.id)));
        TypeIconLibrary.Instance.Preload();
        if (hintTypeBtn)
            hintTypeBtn.onClick.AddListener(RevealTypeHintForOne);

        if (guessInput)
            guessInput.onValueChanged.AddListener(OnGuessChanged);

        if (noTimerToggle)
            noTimerToggle.onValueChanged.AddListener(_ => ResetTimerOnly());
        if (dexOrderToggle)
            dexOrderToggle.onValueChanged.AddListener(_ => RebuildGrid());

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
        EnsureUIContracts();
    }

    private static int BaseIdOf(Pokemon p) => p.baseId != 0 ? p.baseId : p.id;

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

        if (noTimerToggle)
            noTimerToggle.isOn = GameSettings.Minutes <= 0;
        if (minutesInput)
            minutesInput.text = GameSettings.Minutes > 0 ? GameSettings.Minutes.ToString() : "35";
        if (dexOrderToggle)
            dexOrderToggle.isOn = GameSettings.DexOrder;
        if (quizTitle)
            quizTitle.text = Helpers.GetGenTitle(generation);
        BuildTargetList();
        RebuildGrid();
        ResetTimerOnly();
        running = true;
        if (guessInput)
            guessInput.ActivateInputField();
    }

    private void DefocusUI()
    {
        if (guessInput && guessInput.isFocused)
            guessInput.DeactivateInputField();

        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void Update()
    {
        if (!running)
            return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            OnBackToMenuClicked();
#else
        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            OnBackToMenuClicked();
#endif

        if (!IsDialogOpen())
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
                card.Reveal();
            any = true;
        }

        if (any)
            UpdateScore();

        guessInput?.SetTextWithoutNotify(string.Empty);
        guessInput?.ActivateInputField();
        guessInput?.Select();

        if (solved.Count >= targetList.Count)
        {
            running = false;
            if (guessInput)
                guessInput.interactable = false;
        }
    }

    private void ShowNotInQuiz(string name)
    {
        toast?.Show($"{name} is not part of this quiz", 2f);

        if (guessInput)
        {
            guessInput.SetTextWithoutNotify(string.Empty);
            guessInput.ActivateInputField();
            guessInput.Select();
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

        // If exact entry exists in current quiz, use it.
        if (cardById.ContainsKey(guess.id))
            return guess;

        int baseId = BaseIdOf(guess);

        // In Gen 6, prefer the one mega card we actually spawned for this base species
        if (generation == 6 && megaSlotPickByBase.TryGetValue(baseId, out var megaPick))
            return megaPick;

        // Otherwise, any entry in this quiz with same base species (forms, etc.)
        return targetList.FirstOrDefault(p => BaseIdOf(p) == baseId);
    }

    public void OnBackToMenuClicked()
    {
        DefocusUI();
        if (!confirmDialog)
        {
            SceneManager.LoadScene("MainMenu");
            return;
        }

        confirmDialog.Show(
            title: "Leave quiz?",
            message: "Your progress will be lost. Go back to the main menu?",
            confirmLabel: "Yes, leave",
            cancelLabel: "Stay",
            confirmAction: () =>
            {
                SceneManager.LoadScene("MainMenu");
            }
        );
    }

    private bool TryAcceptMegaByBaseName(string text, bool commit)
    {
        if (generation != 6)
            return false; // only relevant in Gen 6
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var norm = GuessNormalizer.Key(text); // "charizard" -> "charizard"

        // Find the *base* species in the full database by normalized name
        Pokemon baseSpecies = null;
        foreach (var p in PokemonDatabase.Instance.All())
        {
            // We want the base species entry: either p.baseId == 0 or p.id == p.baseId
            var isBase = p.baseId == 0 || p.baseId == p.id;
            if (!isBase)
                continue;

            if (GuessNormalizer.Key(p.name) == norm)
            {
                baseSpecies = p;
                break;
            }

            // allow aliases on the base species too
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

        // If we created a mega slot for this base species, map to it
        int baseId = baseSpecies.baseId != 0 ? baseSpecies.baseId : baseSpecies.id;
        if (!megaSlotPickByBase.TryGetValue(baseId, out var pickedMega))
            return false; // no mega slot in this quiz, bail

        // Route through the normal handler so scoring / highlight stays consistent
        HandleCandidate(
            baseSpecies,
            text,
            commit /* doesn't matter; handler maps to mega */
        );
        return true;
    }

    private void RebuildGrid()
    {
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

        // Content layout (simple and proven)
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

        // Clear UI/state
        foreach (Transform c in content)
            Destroy(c.gameObject);
        cardById.Clear();
        solved.Clear();
        hinted.Clear();
        megaFormsByBase.Clear();
        megaSlotPickByBase.Clear();
        megaCardByBase.Clear();

        // Order targets
        var ordered = targetList.OrderBy(p => DexOrder.GetIndex(p)).ToList();

        // Main section (always)
        var main = Instantiate(sectionGroupPrefab, content);
        main.EnsureLayout();
        main.SetTitle(Helpers.GetGenTitle(generation));

        SectionGroup megas = null;

        // If Gen 6, create a Mega section
        if (generation == 6)
        {
            megas = Instantiate(sectionGroupPrefab, content);
            megas.EnsureLayout();
            megas.SetTitle("Mega Evolutions");
        }

        // First pass: non-megas to main; collect megas by base
        foreach (var p in ordered)
        {
            if (generation == 6 && Helpers.IsMega(p))
            {
                // collect by base id
                int baseKey = BaseIdOf(p);
                if (!megaFormsByBase.TryGetValue(baseKey, out var list))
                {
                    list = new List<Pokemon>();
                    megaFormsByBase[baseKey] = list;
                }
                list.Add(p);
                continue;
            }

            // normal (non-mega) card
            var card = Instantiate(cardPrefab, main.gridRoot);
            card.Bind(p);
            cardById[p.id] = card;
        }

        // Second pass: build ONE card per base for megas (Gen 6 only)
        if (generation == 6 && megas != null)
        {
            var rng = new System.Random();
            foreach (var kv in megaFormsByBase)
            {
                var forms = kv.Value;
                var pick = forms[rng.Next(forms.Count)]; // X or Y (or single form)

                var card = Instantiate(cardPrefab, megas.gridRoot);
                card.Bind(pick);

                megaSlotPickByBase[kv.Key] = pick;
                megaCardByBase[kv.Key] = card;

                cardById[pick.id] = card; // optional: still index by id
            }
        }

        // Minimal fitting (uses your existing FitSection)
        FitSection(main);
        if (generation == 6 && megas != null)
            FitSection(megas);

        UpdateScore();
    }

    private void FitSection(SectionGroup grp)
    {
        var grid = grp.gridRoot.GetComponent<GridLayoutGroup>();
        var fit =
            grp.gridRoot.GetComponent<GridAutoFit>()
            ?? grp.gridRoot.gameObject.AddComponent<GridAutoFit>();

        grid.spacing = new Vector2(16, 16);
        grid.childAlignment = TextAnchor.UpperLeft;

        fit.Viewport = scrollRect.viewport;
        fit.Header = grp.headerRect;
        fit.ItemCount = grp.CardCount;
        fit.OuterMarginX = 16;
        fit.OuterMarginY = 16;
        fit.Spacing = 16;
        fit.MinCols = 6;
        fit.MaxCols = 30;

        StartCoroutine(CoRecalc(fit));
    }

    private static IEnumerator CoRecalc(GridAutoFit fit)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        fit.Recalculate();
    }

    private void RevealTypeHintForOne()
    {
        var pool = targetList.Where(p => !solved.Contains(p.id) && !hinted.Contains(p.id)).ToList();

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
    }

    private void BuildTargetList()
    {
        var all = PokemonDatabase.Instance.All().AsEnumerable();

        if (generation > 0)
        {
            var genSet = all.Where(p => p.generation == generation);

            IEnumerable<Pokemon> extras = Enumerable.Empty<Pokemon>();

            if (generation == 6)
            {
                extras = all.Where(Helpers.IsMega);
            }
            else if (generation == 8)
            {
                extras = all.Where(p => Helpers.IsGmax(p) || Helpers.IsHisui(p));
            }
            else if (generation == 9)
            {
                extras = all.Where(p =>
                    Helpers.HasForm(p, "kitakami") || Helpers.HasForm(p, "blueberry")
                );
            }

            all = genSet.Concat(extras).Distinct();
        }

        if (GameSettings.TypeFilter != null && GameSettings.TypeFilter.Length > 0)
        {
            var allowed = new HashSet<string>(
                GameSettings.TypeFilter.Select(t => t.Trim().ToLowerInvariant())
            );
            all = all.Where(p =>
                p.types != null && p.types.Any(t => allowed.Contains(t.ToLowerInvariant()))
            );
        }

        DexOrder.LoadForGeneration(generation);

        if (generation == 7 || generation == 8)
            targetList = all.OrderBy(p => DexOrder.GetIndex(p)).ToList();
        else
            targetList = all.ToList();
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
        RebuildGrid();
        ResetTimerOnly();
        if (guessInput)
        {
            guessInput.text = string.Empty;
            guessInput.interactable = true;
            guessInput.ActivateInputField();
        }
        running = true;
    }

    private void UpdateScore()
    {
        if (scoreText)
            scoreText.text = $"{solved.Count} / {targetList.Count}";
    }

    private void OnGuessChanged(string currentText)
    {
        if (!running || IsDialogOpen())
            return;
        if (string.IsNullOrWhiteSpace(currentText))
            return;

        var trimmed = currentText.Trim().ToLowerInvariant();
        if (trimmed == SecretRevealAll)
        {
            RevealAll();

            guessInput.SetTextWithoutNotify(string.Empty);
            guessInput.ActivateInputField();
            guessInput.Select();
            return;
        }

        var keyOnly = GuessNormalizer.Key(currentText.Trim());
        if (keyOnly == "nidoran")
        {
            RevealByBaseIds(29, 32);
            return;
        }

        bool commit = char.IsWhiteSpace(currentText[currentText.Length - 1]);
        string raw = commit ? currentText.TrimEnd() : currentText;

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

        // 1) EXACT (digit-preserving)
        var exact = FindByExactPreserveDigits(text);
        if (exact != null)
        {
            var mappedFromExact = MapToTargetSpecies(exact);
            if (mappedFromExact != null && !cardById.ContainsKey(exact.id))
            {
                HandleCandidate(mappedFromExact, text, commit);
                return;
            }

            var targetIfExact = MapToTargetSpecies(exact);
            if (targetIfExact != null)
            {
                HandleCandidate(exact, text, commit);
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

        // --- NEW: while typing (no commit), accept base names that map to a mega slot (charizard/mewtwo) ---
        if (!commit && TryAcceptMegaByBaseName(text, commit: false))
            return;
        // --- end NEW ---

        // 2) No exact match and not committed: wait for more typing
        if (!commit)
            return;

        // 3) Committed: fuzzy
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
            if (!commit && HasInQuizContinuation(originalText))
                return;

            var keyNorm = GuessNormalizer.Key(originalText);
            var baseKey = StripDigits(keyNorm);
            if (!string.IsNullOrEmpty(baseKey) && baseKey != keyNorm)
            {
                var altGuess = FindByExactKey(baseKey);
                if (altGuess != null)
                {
                    var altTarget = MapToTargetSpecies(altGuess);
                    if (altTarget == null)
                    {
                        ShowNotInQuiz(altGuess.name);
                        guessInput?.SetTextWithoutNotify(string.Empty);
                        guessInput?.ActivateInputField();
                        guessInput?.Select();
                        return;
                    }
                    if (!solved.Contains(altTarget.id))
                    {
                        solved.Add(altTarget.id);
                        if (cardById.TryGetValue(altTarget.id, out var altCard))
                            altCard.Reveal();
                        UpdateScore();
                        guessInput?.SetTextWithoutNotify(string.Empty);
                        guessInput?.ActivateInputField();
                        guessInput?.Select();
                        if (solved.Count >= targetList.Count)
                        {
                            running = false;
                            if (guessInput)
                                guessInput.interactable = false;
                            toast?.Show(
                                $"Finished in {TimeSpan.FromSeconds(elapsed):hh\\:mm\\:ss}",
                                2.5f
                            );
                        }
                        return;
                    }

                    if (cardById.TryGetValue(altTarget.id, out var altAlready))
                    {
                        altAlready.FlashHighlight();
                    }
                    guessInput?.SetTextWithoutNotify(string.Empty);
                    guessInput?.ActivateInputField();
                    guessInput?.Select();
                    return;
                }
            }

            if (cardById.TryGetValue(target.id, out var already))
            {
                already.FlashHighlight();
            }
            guessInput?.SetTextWithoutNotify(string.Empty);
            guessInput?.ActivateInputField();
            guessInput?.Select();
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
            card.Reveal();
        UpdateScore();

        guessInput?.SetTextWithoutNotify(string.Empty);
        guessInput?.ActivateInputField();
        guessInput?.Select();

        if (solved.Count >= targetList.Count)
        {
            running = false;
            if (guessInput)
                guessInput.interactable = false;
            toast?.Show($"Finished in {TimeSpan.FromSeconds(elapsed):hh\\:mm\\:ss}", 2.5f);
        }
    }

    private static string StripDigits(string key)
    {
        if (string.IsNullOrEmpty(key))
            return key;
        System.Text.StringBuilder sb = new();
        foreach (var ch in key)
            if (!char.IsDigit(ch))
                sb.Append(ch);
        return sb.ToString();
    }

    private static Pokemon FindByExactKey(string normalizedKey)
    {
        foreach (var p in PokemonDatabase.Instance.All())
        {
            if (GuessNormalizer.Key(p.name) == normalizedKey)
                return p;
            if (p.aliases != null)
                foreach (var a in p.aliases)
                    if (GuessNormalizer.Key(a) == normalizedKey)
                        return p;
        }
        return null;
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

    private void RevealAll()
    {
        foreach (var p in targetList)
        {
            if (!solved.Contains(p.id))
                solved.Add(p.id);
            if (cardById.TryGetValue(p.id, out var card))
                card.Reveal();
        }

        UpdateScore();

        running = false;
        if (guessInput)
            guessInput.interactable = false;
        toast?.Show($"Finished in {TimeSpan.FromSeconds(elapsed):hh\\:mm\\:ss}", 2.5f);
    }
}
