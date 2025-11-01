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

    private readonly Dictionary<int, List<Pokemon>> megaFormsByBase = new();
    private readonly Dictionary<int, Pokemon> megaSlotPickByBase = new();
    private readonly Dictionary<int, PokemonCard> megaCardByBase = new();
    private readonly Dictionary<string, List<int>> megaByBaseName = new();

    private readonly Dictionary<string, int> expeditionByBaseName = new();
    private readonly Dictionary<int, Pokemon> expeditionPickByBase = new();
    private readonly Dictionary<int, PokemonCard> expeditionCardByBase = new();

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

    private static void MoveBefore(
        List<Pokemon> list,
        Predicate<Pokemon> what,
        Predicate<Pokemon> before
    )
    {
        var item = list.FirstOrDefault(x => what(x));
        if (item == null)
            return;

        var idxBefore = list.FindIndex(x => before(x));
        if (idxBefore < 0)
            return;

        list.Remove(item);
        // re-find anchor because indices changed after Remove
        idxBefore = list.FindIndex(x => before(x));
        list.Insert(Math.Max(0, idxBefore), item);
    }

    private static void MoveAfter(
        List<Pokemon> list,
        Predicate<Pokemon> what,
        Predicate<Pokemon> after
    )
    {
        var item = list.FirstOrDefault(x => what(x));
        if (item == null)
            return;

        var idxAfter = list.FindIndex(x => after(x));
        if (idxAfter < 0)
            return;

        list.Remove(item);
        // re-find anchor because indices changed after Remove
        idxAfter = list.FindIndex(x => after(x));
        list.Insert(Math.Min(list.Count, idxAfter + 1), item);
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

    private void MoveIdBefore(int idToMove, int anchorId)
    {
        int i = targetList.FindIndex(p => p.id == idToMove);
        int j = targetList.FindIndex(p => p.id == anchorId);
        if (i < 0 || j < 0)
            return;

        var item = targetList[i];
        targetList.RemoveAt(i);
        j = targetList.FindIndex(p => p.id == anchorId); // recompute after removal
        targetList.Insert(Math.Max(0, j), item);
    }

    private void MoveIdAfter(int idToMove, int anchorId)
    {
        int i = targetList.FindIndex(p => p.id == idToMove);
        int j = targetList.FindIndex(p => p.id == anchorId);
        if (i < 0 || j < 0)
            return;

        var item = targetList[i];
        targetList.RemoveAt(i);
        j = targetList.FindIndex(p => p.id == anchorId); // recompute after removal
        targetList.Insert(Math.Min(targetList.Count, j + 1), item);
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

        // If the exact entry is present in THIS quiz, use it.
        if (cardById.ContainsKey(guess.id))
            return guess;

        int baseId = guess.baseId != 0 ? guess.baseId : guess.id;

        // Is a non-mega entry for this base species present in the quiz?
        bool baseInMain = targetList.Any(p =>
            !Helpers.IsMega(p) && (p.baseId != 0 ? p.baseId : p.id) == baseId
        );

        if (baseInMain)
        {
            // Prefer the non-mega (main section) entry
            var baseEntry = targetList.FirstOrDefault(p =>
                !Helpers.IsMega(p) && (p.baseId != 0 ? p.baseId : p.id) == baseId
            );
            if (baseEntry != null)
                return baseEntry;
        }
        else
        {
            // No base in main section (e.g., Charizard/Mewtwo in Kalos) -> prefer the mega slot
            if (megaSlotPickByBase.TryGetValue(baseId, out var megaPick))
                return megaPick;
        }
        if (
            generation == 9
            && !baseInMain
            && expeditionPickByBase.TryGetValue(baseId, out var expPick)
        )
            return expPick;
        // Fallback: anything with same base id
        return targetList.FirstOrDefault(p => (p.baseId != 0 ? p.baseId : p.id) == baseId);
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

    private bool TryAcceptExpeditionByBaseName(string text, bool commit)
    {
        if (generation != 9)
            return false;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var k = GuessNormalizer.Key(text);
        var hit = expeditionByBaseName.TryGetValue(k, out var baseId);
        Debug.Log(
            $"[Expeditions] TryAccept key='{k}' hit={hit} baseId={(hit ? baseId.ToString() : "-")}"
        );

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

        // Content layout
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
        expeditionByBaseName.Clear();
        expeditionPickByBase.Clear();
        expeditionCardByBase.Clear();
        // IMPORTANT: keep the order you built in BuildTargetList (so Paldean Wooper stays before Clodsire)
        var ordered = targetList;

        // Sections
        var main = Instantiate(sectionGroupPrefab, content);
        main.EnsureLayout();
        main.SetTitle(Helpers.GetGenTitle(generation));

        SectionGroup megas = null;
        SectionGroup paldeaExpeditions = null;

        if (generation == 6)
        {
            megas = Instantiate(sectionGroupPrefab, content);
            megas.EnsureLayout();
            megas.SetTitle("Mega Evolutions");
        }
        if (generation == 9)
        {
            paldeaExpeditions = Instantiate(sectionGroupPrefab, content);
            paldeaExpeditions.EnsureLayout();
            paldeaExpeditions.SetTitle("Paldea Expeditions");
        }

        // Collect expeditions; place others immediately
        var expeditionPool = new List<Pokemon>();

        foreach (var p in ordered)
        {
            // Gen 6: collect megas by base, don't place yet
            if (generation == 6 && Helpers.IsMega(p))
            {
                int baseKey = BaseIdOf(p);
                if (!megaFormsByBase.TryGetValue(baseKey, out var list))
                    megaFormsByBase[baseKey] = list = new List<Pokemon>();
                list.Add(p);
                continue;
            }

            // Gen 9: collect expeditions; place later
            if (generation == 9 && Helpers.IsPaldeaExpedition(p))
            {
                expeditionPool.Add(p);
                continue;
            }

            // Main section card
            var card = Instantiate(cardPrefab, main.gridRoot);
            card.Bind(p);
            cardById[p.id] = card;
        }

        if (generation == 6 && megas != null)
        {
            var rng = new System.Random();
            foreach (var kv in megaFormsByBase)
            {
                var pick = kv.Value[rng.Next(kv.Value.Count)];
                var card = Instantiate(cardPrefab, megas.gridRoot);
                card.Bind(pick);

                megaSlotPickByBase[kv.Key] = pick;
                megaCardByBase[kv.Key] = card;
                cardById[pick.id] = card;
            }
        }

        if (generation == 9 && paldeaExpeditions != null)
        {
            var expOrdered = expeditionPool.OrderBy(p => DexOrder.GetIndex(p)).ToList();

            // Ensure Bloodmoon comes right after Sinistcha
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
                var card = Instantiate(cardPrefab, paldeaExpeditions.gridRoot);
                card.Bind(p);
                cardById[p.id] = card;

                int baseKey = p.baseId != 0 ? p.baseId : p.id;
                expeditionPickByBase[baseKey] = p;
                expeditionCardByBase[baseKey] = card;

                // ---- build name keys WITHOUT requiring the base species to exist ----
                void AddKey(string s)
                {
                    var k = GuessNormalizer.Key(s);
                    if (!string.IsNullOrEmpty(k))
                        expeditionByBaseName[k] = baseKey;
                }

                // 1) Full form name (e.g., "Ursaluna (Bloodmoon)")
                AddKey(p.name);

                // 2) Aliases on the form itself (includes things like "Ursaluna Bloodmoon")
                if (p.aliases != null)
                    foreach (var a in p.aliases)
                        AddKey(a);

                // 3) Base name stripped from parentheses (e.g., "Ursaluna")
                var idx = p.name.IndexOf('(');
                if (idx > 0)
                    AddKey(p.name.Substring(0, idx).Trim());

                // 4) If you *do* have the base mon in DB, this adds extra keys,
                // but it’s optional and safe to keep:
                var baseMon = PokemonDatabase.Instance.All().FirstOrDefault(x => x.id == baseKey);
                if (baseMon != null)
                {
                    AddKey(baseMon.name);
                    if (baseMon.aliases != null)
                        foreach (var a in baseMon.aliases)
                            AddKey(a);
                }
            }
        }

        // Let the fitter know how many cards per section
        main.SetCardCount(main.gridRoot.childCount);
        megas?.SetCardCount(megas.gridRoot.childCount);
        paldeaExpeditions?.SetCardCount(paldeaExpeditions.gridRoot.childCount);

        // Fit
        FitSection(main);
        if (megas != null)
            FitSection(megas);
        if (paldeaExpeditions != null)
            FitSection(paldeaExpeditions);

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

        if (grp.headerLabel && grp.headerLabel.text == "Paldea Expeditions")
        {
            fit.MinCols = grp.CardCount;
            fit.MaxCols = grp.CardCount;
            fit.MaxCell = 140f;
        }

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
                var megasDistinctByBase = all.Where(Helpers.IsMega)
                    .GroupBy(p => p.baseId != 0 ? p.baseId : p.id)
                    .Select(g => g.First());
                extras = megasDistinctByBase;
            }
            else if (generation == 8)
            {
                extras = all.Where(p => Helpers.IsGmax(p) || Helpers.IsHisui(p));
            }
            else if (generation == 9)
            {
                extras = all.Where(p => Helpers.IsPaldeaExpedition(p));
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

        // Work on a LIST we control
        var ordered = all.OrderBy(p => DexOrder.GetIndex(p)).ToList();

        if (generation == 9)
        {
            // ---- collapse Paldean Tauros to EXACTLY ONE entry ----
            var taurosForms = ordered.Where(Helpers.IsPaldeaTauros).ToList();
            var taurosOne = taurosForms.FirstOrDefault(); // keep the first by dex order
            if (taurosForms.Count > 0)
            {
                ordered.RemoveAll(Helpers.IsPaldeaTauros);
                ordered.Add(taurosOne); // add back one; we'll position it precisely next
            }

            // ---- Wooper (Paldea) immediately BEFORE Clodsire ----
            int iWoo = ordered.FindIndex(p => p.id == 980); // Wooper (Paldea)
            int iClod = ordered.FindIndex(p => GuessNormalizer.Key(p.name) == "clodsire");

            if (iWoo >= 0 && iClod >= 0 && iWoo != iClod - 1)
            {
                var w = ordered[iWoo];
                ordered.RemoveAt(iWoo);
                iClod = ordered.FindIndex(p => GuessNormalizer.Key(p.name) == "clodsire");
                ordered.Insert(Math.Max(0, iClod), w);
            }

            int iTau = ordered.FindIndex(p => p.baseId == 128 && p.formKey == "paldea"); // the one we kept
            int iGra = ordered.FindIndex(p => GuessNormalizer.Key(p.name) == "grafaiai");

            if (iTau >= 0 && iGra >= 0 && iTau != iGra + 1)
            {
                var t = ordered[iTau];
                ordered.RemoveAt(iTau);
                iGra = ordered.FindIndex(p => GuessNormalizer.Key(p.name) == "grafaiai");
                ordered.Insert(Math.Min(ordered.Count, iGra + 1), t);
            }
        }

        // Finalize
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

        if (generation == 9 && TryAcceptExpeditionByBaseName(currentText.Trim(), commit: true))
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

        if (generation == 6)
        {
            var k = GuessNormalizer.Key(currentText.Trim());
            if (
                !string.IsNullOrEmpty(k)
                && megaByBaseName.TryGetValue(k, out var ids)
                && ids.Count > 0
            )
            {
                // Find the base species by name/alias
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

                // Only auto-map to a mega if the base is NOT in the main section (Charizard/Mewtwo case)
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
                // else: let normal flow continue so the base card is revealed
            }
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

        if (generation == 6)
        {
            int baseKey = target.baseId != 0 ? target.baseId : target.id;

            bool baseExistsInMain = targetList.Any(p =>
                !Helpers.IsMega(p) && (p.baseId != 0 ? p.baseId : p.id) == baseKey
            );

            if (baseExistsInMain && megaSlotPickByBase.TryGetValue(baseKey, out var megaPick))
            {
                if (!solved.Contains(megaPick.id))
                {
                    solved.Add(megaPick.id);
                    if (megaCardByBase.TryGetValue(baseKey, out var megaCard) && megaCard != null)
                        megaCard.Reveal();
                }
            }
        }
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
