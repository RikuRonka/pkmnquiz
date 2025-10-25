using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.UI;

public class QuizManager : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField guessInput;
    public TMP_Text scoreText;
    public TMP_Text timerText;
    public Toggle dexOrderToggle;   // on = dex order, off = chaos
    public Toggle noTimerToggle;    // on = infinite
    public TMP_InputField minutesInput;
    public Button resetBtn;

    [Header("Grid")]
    public Transform gridContent;   // parent with GridLayoutGroup
    public PokemonCard cardPrefab;

    [Header("Config")]
    public int generation = 1;      // start with Kanto

    private List<Pokemon> targetList = new();
    private Dictionary<int, PokemonCard> cardById = new();
    private HashSet<int> solved = new();

    public Button hintTypeBtn;          // assign in Inspector
    private HashSet<int> hinted = new(); // track which mons already got a hint

    private float timeLeft; // seconds
    private bool running;
    public ScrollRect scrollRect;
    private const string SecretRevealAll = "revealall";
    public TMP_Text heardText;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    public Button micToggleBtn;
    private VoiceInput voice;
    private bool micOn;
    private readonly Dictionary<string, Pokemon> voiceMap = new(); // exact phrase → Pokemon
    private string lastHeard = null;
    private float lastHeardTime = -999f;
#endif

    // In Awake(), replace/add listeners:
    private void Awake()
    {

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (micToggleBtn) micToggleBtn.onClick.AddListener(ToggleMic);
#endif
        PokemonDatabase.Instance.LoadIfNeeded();
        SpriteLibrary.Instance.Preload();
        TypeIconLibrary.Instance.Preload();
        if (hintTypeBtn) hintTypeBtn.onClick.AddListener(RevealTypeHintForOne);

        if (resetBtn) resetBtn.onClick.AddListener(ResetGame);

        // REPLACE this:
        // if (guessInput) guessInput.onSubmit.AddListener(OnGuessSubmitted);

        // WITH this (reveals while typing):
        if (guessInput) guessInput.onValueChanged.AddListener(OnGuessChanged);

        if (noTimerToggle) noTimerToggle.onValueChanged.AddListener(_ => ResetTimerOnly());
        if (dexOrderToggle) dexOrderToggle.onValueChanged.AddListener(_ => RebuildGrid());
    }

    private void Start()
    {
        BuildTargetList();
        RebuildGrid();
        ResetTimerOnly();
        running = true;
        if (guessInput) guessInput.ActivateInputField();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        voiceMap.Clear();
        foreach (var p in targetList)
        {
            void add(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                var k = s.Trim().ToLowerInvariant();
                if (!voiceMap.ContainsKey(k)) voiceMap[k] = p;
            }
            add(p.name);
            if (p.aliases != null) foreach (var al in p.aliases) add(al);
        }
        voice = gameObject.GetComponent<VoiceInput>() ?? gameObject.AddComponent<VoiceInput>();
        voice.OnHeard = OnVoiceHeard;
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private void ToggleMic()
    {
        if (voice == null) return;

        if (micOn)
        {
            voice.StopListening();
            micOn = false; SetMicLabel("Speak"); SetHeard("");
        }
        else
        {
            // phrases from voiceMap.Keys or targetList
            voice.StartListening(voiceMap.Keys);
            micOn = true; SetMicLabel("Listening…"); SetHeard("—");
        }
    }
    private void SetHeard(string s)
    {
        if (!heardText) return;
        heardText.text = string.IsNullOrEmpty(s) ? "" : $"Heard: {s}";
    }

    private void SetMicLabel(string text)
    {
        var t = micToggleBtn?.GetComponentInChildren<TMPro.TMP_Text>();
        if (t) t.text = text;
    }

    private void OnVoiceHeard(string heardRaw)
    {
        if (!micOn) return;
        SetHeard(heardRaw);  // <-- show exactly what Windows thought you said

        var heard = heardRaw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(heard)) return;

        if (heard == lastHeard && Time.time - lastHeardTime < 0.4f) return;
        lastHeard = heard; lastHeardTime = Time.time;

        if (!voiceMap.TryGetValue(heard, out var p))
        {
            // If you want, also try fuzzy: TryAcceptGuess(heard);
            return;
        }

        if (solved.Contains(p.id))
        {
            if (cardById.TryGetValue(p.id, out var card)) { card.FlashHighlight(); FocusCard(card.transform as RectTransform); }
            return;
        }

        solved.Add(p.id);
        if (cardById.TryGetValue(p.id, out var hit)) hit.Reveal();
        UpdateScore();

        if (solved.Count >= targetList.Count)
        {
            running = false;
            if (guessInput) guessInput.interactable = false;
            // Optional: keep showing 'Listening…' or auto-stop mic:
            // ToggleMic();
        }
    }
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private void RebuildVoiceMapAndRestartMic()
    {
        if (voice == null) voice = gameObject.GetComponent<VoiceInput>() ?? gameObject.AddComponent<VoiceInput>();

        voiceMap.Clear();
        var phrases = new System.Collections.Generic.List<string>();

        foreach (var p in targetList)
        {
            void add(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                var key = s.Trim();
                if (!voiceMap.ContainsKey(key.ToLowerInvariant()))
                    voiceMap[key.ToLowerInvariant()] = p;
                phrases.Add(key); // keep original casing for recognizer
            }
            add(p.name);
            if (p.aliases != null) foreach (var a in p.aliases) add(a);
        }

        // DEBUG: confirm Pikachu is included
        bool hasPikachu = phrases.Exists(s => s.Equals("Pikachu", System.StringComparison.InvariantCultureIgnoreCase));
        UnityEngine.Debug.Log($"[Voice] Phrases={phrases.Count}, has 'Pikachu'={hasPikachu}");

        if (micOn)
        {
            voice.StartListening(phrases); // restarts recognizer with fresh list
            SetMicLabel("Listening…");
        }
    }
#endif

    // Extract your “accept guess if correct” logic into a method used by both typing + voice:
    private void TryAcceptGuess(string text)
    {
        var p = PokemonDatabase.Instance.FindByGuess(text);
        if (p == null) return;

        if (solved.Contains(p.id))
        {
            if (cardById.TryGetValue(p.id, out var already)) { already.FlashHighlight(); FocusCard(already.transform as RectTransform); }
            return;
        }
        if (p.generation != generation) return;

        solved.Add(p.id);
        if (cardById.TryGetValue(p.id, out var card)) card.Reveal();
        UpdateScore();

        if (solved.Count >= targetList.Count)
        {
            running = false;
            if (guessInput) guessInput.interactable = false;
            ToggleMic(); // stop listening when completed (optional)
        }
    }
#endif

    private void Update()
    {
        if (!running) return;

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

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        RebuildVoiceMapAndRestartMic();
#endif
    }

    private void RevealTypeHintForOne()
    {
        // Sanity: did icons load?
        var testGrass = TypeIconLibrary.Instance.Get("Grass");
        Debug.Log($"[Hint] Click. IconsLoadedTest(grass)={(testGrass ? "yes" : "no")}, solved={solved.Count}, target={targetList.Count}");

        // pool of unguessed, not-yet-hinted
        var pool = targetList.Where(p => !solved.Contains(p.id) && !hinted.Contains(p.id)).ToList();
        Debug.Log($"[Hint] pool={pool.Count}, hinted={hinted.Count}");

        if (pool.Count == 0) return;

        // pick first to be deterministic during debugging
        var pick = pool[0];
        hinted.Add(pick.id);

        Debug.Log($"[Hint] pick: #{pick.id} {pick.name} types={(pick.types == null ? "null" : string.Join("/", pick.types))}");

        if (!cardById.TryGetValue(pick.id, out var card) || card == null)
        {
            Debug.LogWarning($"[Hint] No card for id {pick.id}.");
            return;
        }

        card.ShowTypeHint(pick.types);
    }


    private void BuildTargetList()
    {
        targetList = PokemonDatabase.Instance.All()
                   .Where(p => p.generation == generation)
                   .OrderBy(p => p.id)
                   .ToList();
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

    // Instantly check current input; if it's a full match, reveal and clear
    private void OnGuessChanged(string currentText)
    {
        if (!running) return;
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

        var p = PokemonDatabase.Instance.FindByGuess(currentText);
        if (p == null) return;

    }

    // 2) Add this helper (instant reveal only when unambiguous)
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

        // If this exact name is also a prefix of another still-unguessed mon's name/alias,
        // then it's AMBIGUOUS (e.g., "pidgeot" vs "pidgeotto", "mew" vs "mewtwo")
        bool ambiguous = IsAmbiguousPrefix(key, exact);

        // If ambiguous and the user hasn't committed (no trailing space), wait for more typing
        if (ambiguous && !commit) return;

        // Accept the guess
        solved.Add(exact.id);
        if (cardById.TryGetValue(exact.id, out var card)) card.Reveal();
        UpdateScore();

        // Clear and refocus
        guessInput.SetTextWithoutNotify(string.Empty);
        guessInput.ActivateInputField();
        guessInput.Select();

        if (solved.Count >= targetList.Count)
        {
            running = false;
            if (guessInput) guessInput.interactable = false;
        }
    }

    // 3) Add this disambiguation check
    private bool IsAmbiguousPrefix(string key, Pokemon exact)
    {
        // Scan remaining mons; if any has a name/alias whose normalized form starts with `key`,
        // and it's a DIFFERENT mon, then typing `key` is ambiguous.
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
        // mark all as solved
        foreach (var p in targetList)
        {
            if (!solved.Contains(p.id)) solved.Add(p.id);
            if (cardById.TryGetValue(p.id, out var card)) card.Reveal();
        }

        UpdateScore();

        // stop the run (optional)
        running = false;
        if (guessInput) guessInput.interactable = false;

        // optional: freeze timer display to final value
        if (timerText) timerText.text = "✓";
    }

    private void FocusCard(RectTransform card)
    {
        if (!scrollRect || !card) return;

        var content = (RectTransform)scrollRect.content;
        var viewport = (RectTransform)scrollRect.viewport;

        Canvas.ForceUpdateCanvases();

        // Assuming Content pivot Y = 1 (top). For default ScrollView it is.
        float contentH = content.rect.height;
        float viewH = viewport.rect.height;
        float y = Mathf.Abs(card.anchoredPosition.y); // distance from top
        float target = 1f - Mathf.Clamp01((y - viewH * 0.5f) / Mathf.Max(1f, contentH - viewH));

        scrollRect.verticalNormalizedPosition = target;
    }
}