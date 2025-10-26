using System;
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
    public Transform gridContent;
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
    private float timeLeft;
    private bool running;
    public ScrollRect scrollRect;
    private const string SecretRevealAll = "revealall";
    private bool IsDialogOpen() => confirmDialog && confirmDialog.IsShowing;
    public Toast toast;

    private void Awake()
    {
        PokemonDatabase.Instance.LoadIfNeeded();
        SpriteLibrary.Instance.Preload();
        TypeIconLibrary.Instance.Preload();
        if (hintTypeBtn) hintTypeBtn.onClick.AddListener(RevealTypeHintForOne);

        if (resetBtn) resetBtn.onClick.AddListener(ResetGame);

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
    }

    private void Start()
    {

        if (GameSettings.Generation.HasValue)
            generation = GameSettings.Generation.Value;

        if (noTimerToggle) noTimerToggle.isOn = GameSettings.Minutes <= 0;
        if (minutesInput) minutesInput.text = GameSettings.Minutes > 0 ? GameSettings.Minutes.ToString() : "35";
        if (dexOrderToggle) dexOrderToggle.isOn = GameSettings.DexOrder;
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

        // --- Timer logic (unchanged) ---
        if (noTimerToggle == null || !noTimerToggle.isOn)
        {
            timeLeft = Mathf.Max(0f, timeLeft - Time.deltaTime);
            if (timerText) timerText.text = TimeSpan.FromSeconds(Mathf.CeilToInt(timeLeft)).ToString(@"hh\:mm\:ss");
            if (timeLeft <= 0.01f)
            {
                running = false;
                if (guessInput) guessInput.interactable = false;
            }
        }
        else
        {
            if (timerText) timerText.text = "∞";
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
        foreach (Transform c in gridContent) Destroy(c.gameObject);
        cardById.Clear();
        hinted.Clear();  // ← reset per run

        IEnumerable<Pokemon> list = targetList;
        if (dexOrderToggle != null && !dexOrderToggle.isOn)
            list = list.OrderBy(_ => UnityEngine.Random.value);

        foreach (var p in list)
        {
            var card = Instantiate(cardPrefab, gridContent);
            card.Bind(p);
            cardById[p.id] = card;
        }
        solved.Clear();
        UpdateScore();
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

        // Generation filter (if set)
        if (generation > 0)
            all = all.Where(p => p.generation == generation);

        if (GameSettings.TypeFilter != null && GameSettings.TypeFilter.Length > 0)
        {
            var allowed = new HashSet<string>(GameSettings.TypeFilter.Select(t => t.Trim().ToLowerInvariant()));
            all = all.Where(p => p.types != null && p.types.Any(t => allowed.Contains(t.ToLowerInvariant())));
        }

        targetList = all.OrderBy(p => p.id).ToList();
    }

    private void ResetTimerOnly()
    {
        if (noTimerToggle != null && noTimerToggle.isOn)
        {
            if (timerText) timerText.text = "∞";
            return;
        }
        int minutes = 35;
        if (minutesInput && int.TryParse(minutesInput.text, out var m)) minutes = Mathf.Max(1, m);
        timeLeft = minutes * 60f;
        if (timerText) timerText.text = TimeSpan.FromSeconds(timeLeft).ToString(@"hh\:mm\:ss");
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


    private void OnGuessSubmitted(string text) // <- NOT "OnSubmit"
    {
        if (!running) return;

        var p = PokemonDatabase.Instance.FindByGuess(text);
        if (p != null && p.generation == generation && !solved.Contains(p.id))
        {
            solved.Add(p.id);
            if (cardById.TryGetValue(p.id, out var card)) card.Reveal();
            UpdateScore();
            if (guessInput) guessInput.text = string.Empty;
        }

        if (guessInput)
        {
            guessInput.ActivateInputField();
            guessInput.Select();
        }

        if (solved.Count >= targetList.Count)
        {
            running = false;
            if (guessInput) guessInput.interactable = false;
        }
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

        bool commit = char.IsWhiteSpace(currentText[currentText.Length - 1]);
        string raw = commit ? currentText.TrimEnd() : currentText;

        TryAcceptWithDisambiguation(raw, commit);
    }


    private void TryAcceptWithDisambiguation(string text, bool commit)
    {
        var key = GuessNormalizer.Key(text);
        if (string.IsNullOrEmpty(key)) return;

        // Exact match (by our DB)
        var exact = PokemonDatabase.Instance.FindByGuess(text);
        if (exact == null) return;

        // Already solved? just highlight + refocus
        if (solved.Contains(exact.id))
        {
            if (cardById.TryGetValue(exact.id, out var already))
            {
                already.FlashHighlight();
                FocusCard(already.transform as RectTransform);
            }
            guessInput.SetTextWithoutNotify(string.Empty);
            guessInput.ActivateInputField();
            guessInput.Select();
            return;
        }

        bool inTarget = targetList.Any(p => p.id == exact.id);
        if (!inTarget)
        {
            ShowNotInQuiz(exact.name);
            return;
        }

        bool ambiguous = IsAmbiguousPrefix(key, exact);

        if (ambiguous && !commit) return;

        solved.Add(exact.id);
        if (cardById.TryGetValue(exact.id, out var card)) card.Reveal();
        UpdateScore();

        guessInput.SetTextWithoutNotify(string.Empty);
        guessInput.ActivateInputField();
        guessInput.Select();

        if (solved.Count >= targetList.Count)
        {
            running = false;
            if (guessInput) guessInput.interactable = false;
        }
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

        if (timerText) timerText.text = "✓";
    }

    private void FocusCard(RectTransform card)
    {
        if (!scrollRect || !card) return;

        var content = scrollRect.content;
        var viewport = scrollRect.viewport;

        Canvas.ForceUpdateCanvases();

        float contentH = content.rect.height;
        float viewH = viewport.rect.height;
        float y = Mathf.Abs(card.anchoredPosition.y);
        float target = 1f - Mathf.Clamp01((y - viewH * 0.5f) / Mathf.Max(1f, contentH - viewH));

        scrollRect.verticalNormalizedPosition = target;
    }
}
