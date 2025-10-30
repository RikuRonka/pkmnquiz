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
    public SectionGroup sectionGroupPrefab;   // assign in Inspector
    public Transform content;                 // ScrollView/Viewport/Content
    private List<SectionGroup> _builtSections = new();
    private Vector2 _lastVpSize;
    private void Awake()
    {
        PokemonDatabase.Instance.LoadIfNeeded();
        var dupes = PokemonDatabase.Instance.All()
        .GroupBy(p => p.id)
        .Where(g => g.Count() > 1)
        .Select(g => new { id = g.Key, names = string.Join(", ", g.Select(p => p.name)) })
        .ToList();

        if (dupes.Count > 0)
        {
            Debug.LogError("[PokemonDB] Duplicate IDs detected:\n" +
                           string.Join("\n", dupes.Select(d => $"{d.id}: {d.names}")));
        }
        StartCoroutine(SpriteLibrary.Instance.PreloadAsync(targetList.Select(t => t.id)));
        TypeIconLibrary.Instance.Preload();
        if (hintTypeBtn) hintTypeBtn.onClick.AddListener(RevealTypeHintForOne);

        if (guessInput) guessInput.onValueChanged.AddListener(OnGuessChanged);

        if (noTimerToggle) noTimerToggle.onValueChanged.AddListener(_ => ResetTimerOnly());
        if (dexOrderToggle) dexOrderToggle.onValueChanged.AddListener(_ => RebuildGrid());

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

    private void EnsureUIContracts()
    {
        // Content must have VLG + CSF for sections to stack and size
        if (!content) content = scrollRect ? scrollRect.content : null;
        if (!content) return;

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

        if (noTimerToggle) noTimerToggle.isOn = GameSettings.Minutes <= 0;
        if (minutesInput) minutesInput.text = GameSettings.Minutes > 0 ? GameSettings.Minutes.ToString() : "35";
        if (dexOrderToggle) dexOrderToggle.isOn = GameSettings.DexOrder;
        if (quizTitle) quizTitle.text = Helpers.GetGenTitle(generation);
        BuildTargetList();
        RebuildGrid();
        ResetTimerOnly();
        running = true;
        if (guessInput) guessInput.ActivateInputField();
    }


    private void DefocusUI()
    {
        if (guessInput && guessInput.isFocused)
            guessInput.DeactivateInputField();

        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void Update()
    {
        if (!running) return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            OnBackToMenuClicked();
#else
    if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
        OnBackToMenuClicked();
#endif

        if (!IsDialogOpen())              // don't count while confirm dialog is open
        {
            elapsed += Time.deltaTime;    // count up
            if (timerText)
                timerText.text = TimeSpan.FromSeconds(elapsed).ToString(@"hh\:mm\:ss");
        }
    }

    private void RevealByBaseIds(params int[] baseIds)
    {
        bool any = false;
        foreach (var baseId in baseIds)
        {
            // find the card that exists in THIS quiz for this base species (works with forms too)
            var target = targetList.FirstOrDefault(p => (p.baseId != 0 ? p.baseId : p.id) == baseId);
            if (target == null) continue;

            if (solved.Contains(target.id))
            {
                if (cardById.TryGetValue(target.id, out var already))
                {
                    already.FlashHighlight();
                }
                continue;
            }

            solved.Add(target.id);
            if (cardById.TryGetValue(target.id, out var card)) card.Reveal();
            any = true;
        }

        if (any) UpdateScore();

        // clear input & refocus
        guessInput?.SetTextWithoutNotify(string.Empty);
        guessInput?.ActivateInputField();
        guessInput?.Select();

        // check completion
        if (solved.Count >= targetList.Count)
        {
            running = false;
            if (guessInput) guessInput.interactable = false;
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
        if (!confirmDialog) { ResetGame(); return; }

        confirmDialog.Show(
            title: "Reset quiz?",
            message: "This will clear all revealed Pokémon and restart the timer.",
            confirmLabel: "Reset",
            cancelLabel: "Cancel",
            confirmAction: ResetGame
        );
    }

    // Map a guessed Pokémon to whatever version of that species exists in the current targetList.
    // e.g. guess = Kanto Raticate (id 20) while playing Gen7 -> returns Alolan Raticate.
    private Pokemon MapToTargetSpecies(Pokemon guess)
    {
        if (guess == null) return null;

        // If this exact entry is in the quiz, use it.
        if (cardById.ContainsKey(guess.id)) return guess;

        // Find by base species id (for forms) or by own id for base species.
        int baseId = guess.baseId != 0 ? guess.baseId : guess.id;

        // Any Pokémon in the current target list with same base species?
        // (covers Alola/Galar/Hisui etc. if you add them later)
        var mapped = targetList.FirstOrDefault(p =>
            (p.baseId != 0 ? p.baseId : p.id) == baseId);

        return mapped; // null if none found
    }

    public void OnBackToMenuClicked()
    {
        DefocusUI();
        if (!confirmDialog) { SceneManager.LoadScene("MainMenu"); return; }

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

    private void RebuildGrid()
    {
        if (!scrollRect || !scrollRect.viewport) { Debug.LogError("ScrollRect/Viewport missing"); return; }

        // Ensure ScrollView/Content has VLG + CSF
        var contentRt = content as RectTransform;
        var vlg = contentRt.GetComponent<VerticalLayoutGroup>() ?? contentRt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 24;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = contentRt.GetComponent<ContentSizeFitter>() ?? contentRt.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (Transform c in content) Destroy(c.gameObject);
        cardById.Clear(); solved.Clear(); hinted.Clear();

        var ordered = targetList.OrderBy(p => DexOrder.GetIndex(p)).ToList();

        var main = Instantiate(sectionGroupPrefab, content);
        main.EnsureLayout();
        main.SetTitle(Helpers.GetGenTitle(generation));

        SectionGroup megas = null;
        bool wantMegas = (generation == 6);
        if (wantMegas)
        {
            megas = Instantiate(sectionGroupPrefab, content);
            megas.EnsureLayout();
            megas.SetTitle("Mega Evolutions");
        }

        foreach (var p in ordered)
        {
            var targetGroup = (wantMegas && Helpers.IsMega(p)) ? megas : main;
            var card = Instantiate(cardPrefab, targetGroup.gridRoot);
            card.Bind(p);
            cardById[p.id] = card;
        }

        if (megas && megas.gridRoot.childCount == 0)
        {
            Destroy(megas.gameObject);
            megas = null;
        }

        FitSection(main);
        if (megas) FitSection(megas);

        UpdateScore();
    }

    private void FitSection(SectionGroup grp)
    {
        var grid = grp.gridRoot.GetComponent<GridLayoutGroup>();
        var fit = grp.gridRoot.GetComponent<GridAutoFit>() ?? grp.gridRoot.gameObject.AddComponent<GridAutoFit>();

        // pleasant defaults
        grid.spacing = new Vector2(16, 16);
        grid.childAlignment = TextAnchor.UpperLeft;

        fit.Viewport = scrollRect.viewport;
        fit.Header = grp.headerRect;
        fit.ItemCount = grp.CardCount;
        fit.OuterMarginX = 16;
        fit.OuterMarginY = 16;
        fit.Spacing = 16;
        fit.MinCols = 6;     // adjust to taste
        fit.MaxCols = 30;

        StartCoroutine(CoRecalc(fit));
    }

    private static IEnumerator CoRecalc(GridAutoFit fit)
    {
        yield return null;                       // wait 1 frame so header sizes are valid
        Canvas.ForceUpdateCanvases();
        fit.Recalculate();
    }


    private void RevealTypeHintForOne()
    {

        var pool = targetList.Where(p => !solved.Contains(p.id) && !hinted.Contains(p.id)).ToList();

        if (pool.Count == 0) return;

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
            // Base set for the generation
            var genSet = all.Where(p => p.generation == generation);

            // Extras by generation
            IEnumerable<Pokemon> extras = Enumerable.Empty<Pokemon>();

            if (generation == 6)
            {
                // Include Mega evolutions (introduced in Gen 6 no matter the base species' gen)
                extras = all.Where(Helpers.IsMega);
            }
            else if (generation == 8)
            {
                // Include Gigantamax + Hisui forms in Galar quiz
                extras = all.Where(p => Helpers.IsGmax(p) || Helpers.IsHisui(p));
            }
            else if (generation == 9)
            {
                // If you have DLC/expedition subsets marked via formKey/tags, add them here.
                // Example: formKey: "kitakami" / "blueberry"
                extras = all.Where(p => Helpers.HasForm(p, "kitakami") || Helpers.HasForm(p, "blueberry"));
            }

            all = genSet.Concat(extras).Distinct();
        }

        // Optional type filter
        if (GameSettings.TypeFilter != null && GameSettings.TypeFilter.Length > 0)
        {
            var allowed = new HashSet<string>(GameSettings.TypeFilter.Select(t => t.Trim().ToLowerInvariant()));
            all = all.Where(p => p.types != null && p.types.Any(t => allowed.Contains(t.ToLowerInvariant())));
        }

        DexOrder.LoadForGeneration(generation);

        // Use dex order for gens where we have a file
        if (generation == 7 || generation == 8)
            targetList = all.OrderBy(p => DexOrder.GetIndex(p)).ToList();
        else
            targetList = all.ToList();
    }

    private void ResetTimerOnly()
    {
        elapsed = 0f;
        if (timerText) timerText.text = "00:00:00";
    }

    private bool HasInQuizContinuation(string text)
    {
        var typed = KeyKeepDigits(text);
        if (string.IsNullOrEmpty(typed)) return false;

        foreach (var p in PokemonDatabase.Instance.All())
        {
            // name
            var nk = KeyKeepDigits(p.name);
            if (nk.Length > typed.Length && nk.StartsWith(typed) && MapToTargetSpecies(p) != null)
            {
                    return true;
            }
            // aliases
            if (p.aliases != null)
            {
                foreach (var a in p.aliases)
                {
                    var ak = KeyKeepDigits(a);
                    if (ak.Length > typed.Length && ak.StartsWith(typed) && MapToTargetSpecies(p) != null)
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
        if (scoreText) scoreText.text = $"{solved.Count} / {targetList.Count}";
    }


    private void OnGuessChanged(string currentText)
    {
        if (!running || IsDialogOpen()) return;
        if (string.IsNullOrWhiteSpace(currentText)) return;

        var trimmed = currentText.Trim().ToLowerInvariant();
        if (trimmed == SecretRevealAll)
        {
            RevealAll();
            // clear input & keep focus
            guessInput.SetTextWithoutNotify(string.Empty);
            guessInput.ActivateInputField();
            guessInput.Select();
            return;
        }

        var keyOnly = GuessNormalizer.Key(currentText.Trim());
        if (keyOnly == "nidoran")
        {
            // reveal both (♀ = 29, ♂ = 32)
            RevealByBaseIds(29, 32);
            return;
        }

        bool commit = char.IsWhiteSpace(currentText[currentText.Length - 1]);
        string raw = commit ? currentText.TrimEnd() : currentText;

        TryAcceptWithDisambiguation(raw, commit);
    }

    // Lowercase, strip spaces/quotes/dashes/accents — BUT KEEP DIGITS.
    private static string KeyKeepDigits(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        s = s.Trim().ToLowerInvariant();

        // basic ascii deaccent (add more if you like)
        s = s.Replace("é", "e");

        System.Text.StringBuilder sb = new();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); continue; }
            // allow nothing else (remove spaces, dashes, punctuation)
        }
        return sb.ToString();
    }

    // Strong exact match (name or alias) that PRESERVES digits (porygon2 stays porygon2)
    private static Pokemon FindByExactPreserveDigits(string text)
    {
        var k = KeyKeepDigits(text);
        if (string.IsNullOrEmpty(k)) return null;

        Pokemon best = null;
        int bestLen = -1;

        foreach (var p in PokemonDatabase.Instance.All())
        {
            var pk = KeyKeepDigits(p.name);
            if (pk == k && pk.Length > bestLen) { best = p; bestLen = pk.Length; }

            if (p.aliases != null)
                foreach (var a in p.aliases)
                {
                    var ak = KeyKeepDigits(a);
                    if (ak == k && ak.Length > bestLen) { best = p; bestLen = ak.Length; }
                }
        }
        return best;
    }

    private void TryAcceptWithDisambiguation(string text, bool commit)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // 1) EXACT (digit-preserving) first
        var exact = FindByExactPreserveDigits(text);

        if (exact != null)
        {
            var targetIfExact = MapToTargetSpecies(exact);

            if (targetIfExact != null)
            {
                // Exact name is in this quiz -> handle immediately
                HandleCandidate(exact, text, commit);
                return;
            }

            // Exact name is NOT in this quiz.
            if (!commit)
            {
                // If this token is a prefix of a longer name that *is* in this quiz (e.g. porygon -> porygon2),
                // hold off the toast and let the player continue typing.
                if (HasInQuizContinuation(text))
                    return;

                // Otherwise, show the toast immediately (force commit=true so HandleCandidate toasts now)
                HandleCandidate(exact, text, true);
                return;
            }

            // User committed (space/enter): toast via the usual path
            HandleCandidate(exact, text, true);
            return;
        }

        // 2) No exact match
        if (!commit) return; // don't fuzzy-match mid-typing

        // 3) Committed: fuzzy fallback
        var fuzzy = PokemonDatabase.Instance.FindByGuess(text);
        if (fuzzy == null) return;

        HandleCandidate(fuzzy, text, commit);
    }

    private void HandleCandidate(Pokemon guess, string originalText, bool commit)
    {
        // Map base name to a form that exists in THIS quiz
        var target = MapToTargetSpecies(guess);


        // Not in this quiz -> toast only when it's clearly intentional
        if (target == null)
        {
            // Only toast if the typed token is an exact name/alias (by your existing rules)
            // AND (the user committed OR token length is reasonably long).
            var key = GuessNormalizer.Key(originalText);
            bool exactTyped = IsExactNameOrAlias(originalText, guess);
            if ((commit && exactTyped) || (exactTyped && key.Length >= MIN_TOAST_LEN))
                ShowNotInQuiz(guess.name);
            return;
        }

        // Already solved?
        if (solved.Contains(target.id))
        {
            // If what's typed could lead to a longer, valid name in this quiz (e.g., "klink" -> "klinklang"),
            // do NOTHING so the user can continue typing.
            if (!commit && HasInQuizContinuation(originalText))
                return;

            // FAMILY FALLBACK (e.g., second "porygon2" -> try "porygon")
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
                        if (cardById.TryGetValue(altTarget.id, out var altCard)) altCard.Reveal();
                        UpdateScore();
                        guessInput?.SetTextWithoutNotify(string.Empty);
                        guessInput?.ActivateInputField();
                        guessInput?.Select();
                        if (solved.Count >= targetList.Count)
                        {
                            running = false;
                            if (guessInput) guessInput.interactable = false;
                            toast?.Show($"Finished in {TimeSpan.FromSeconds(elapsed):hh\\:mm\\:ss}", 2.5f);
                        }
                        return;
                    }
                    // alt already solved -> just highlight it
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

            // Default: highlight the already-solved target and clear
            if (cardById.TryGetValue(target.id, out var already))
            {
                already.FlashHighlight();
            }
            guessInput?.SetTextWithoutNotify(string.Empty);
            guessInput?.ActivateInputField();
            guessInput?.Select();
            return;
        }

        // If it's an exact name/alias for the target, accept immediately (don't block on ambiguity).
        bool isExactForTarget = IsExactNameOrAlias(originalText, target);

        if (!isExactForTarget)
        {
            var ambKey = GuessNormalizer.Key(originalText);
            bool ambiguous = IsAmbiguousPrefix(ambKey, target);
            if (ambiguous && !commit) return;
        }

        // Accept + reveal TARGET
        solved.Add(target.id);
        if (cardById.TryGetValue(target.id, out var card)) card.Reveal();
        UpdateScore();

        guessInput?.SetTextWithoutNotify(string.Empty);
        guessInput?.ActivateInputField();
        guessInput?.Select();

        if (solved.Count >= targetList.Count)
        {
            running = false;
            if (guessInput) guessInput.interactable = false;
            toast?.Show($"Finished in {TimeSpan.FromSeconds(elapsed):hh\\:mm\\:ss}", 2.5f);
        }
    }

    // remove all digits from a normalized key: "porygon2" -> "porygon"
    private static string StripDigits(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        System.Text.StringBuilder sb = new();
        foreach (var ch in key) if (!char.IsDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    // Find a Pokémon whose NAME or any ALIAS matches exactly the given normalized key.
    private static Pokemon FindByExactKey(string normalizedKey)
    {
        foreach (var p in PokemonDatabase.Instance.All())
        {
            if (GuessNormalizer.Key(p.name) == normalizedKey) return p;
            if (p.aliases != null)
                foreach (var a in p.aliases)
                    if (GuessNormalizer.Key(a) == normalizedKey) return p;
        }
        return null;
    }

    private static bool IsExactNameOrAlias(string text, Pokemon p)
    {
        var k = GuessNormalizer.Key(text);
        if (string.IsNullOrEmpty(k) || p == null) return false;

        if (GuessNormalizer.Key(p.name) == k) return true;
        if (p.aliases != null)
            foreach (var a in p.aliases)
                if (GuessNormalizer.Key(a) == k) return true;

        return false;
    }

    private bool IsAmbiguousPrefix(string key, Pokemon exact)
    {
        foreach (var p in targetList)
        {
            if (p.id == exact.id || solved.Contains(p.id)) continue;

            // main name
            if (GuessNormalizer.Key(p.name).StartsWith(key)) return true;

            // aliases
            if (p.aliases != null)
                foreach (var a in p.aliases)
                    if (GuessNormalizer.Key(a).StartsWith(key)) return true;
        }
        return false;
    }


    private void RevealAll()
    {
        foreach (var p in targetList)
        {
            if (!solved.Contains(p.id)) solved.Add(p.id);
            if (cardById.TryGetValue(p.id, out var card)) card.Reveal();
        }

        UpdateScore();

        running = false;
        if (guessInput) guessInput.interactable = false;
        toast?.Show($"Finished in {TimeSpan.FromSeconds(elapsed):hh\\:mm\\:ss}", 2.5f);
    }

}
