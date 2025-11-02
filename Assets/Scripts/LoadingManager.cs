// LoadingManager.cs
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LoadingManager : MonoBehaviour
{
    public static LoadingManager Instance { get; private set; }

    [Header("Overlay UI")]
    [SerializeField]
    CanvasGroup cg;

    [SerializeField]
    Image barFill; // radial or horizontal fill image

    [SerializeField]
    TMP_Text percentLabel;

    public int PendingGen { get; private set; } = 0;
    public string PendingType { get; private set; } = null;
    string _title = "Building…";

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Make sure this GO is root before DontDestroyOnLoad
        if (transform.parent != null)
            transform.SetParent(null, false);

        DontDestroyOnLoad(gameObject);

        SetProgress(0f);
        SetVisible(false, immediate: true);
    }

    void SetVisible(bool on, bool immediate = false)
    {
        if (!cg)
            return;
        StopAllCoroutines();
        if (immediate)
        {
            cg.alpha = on ? 1f : 0f;
            cg.blocksRaycasts = on;
            return;
        }
        StartCoroutine(Fade(on ? 1f : 0f, 0.15f));
        cg.blocksRaycasts = on;
    }

    public void Show(string title, bool immediate = false)
    {
        _title = string.IsNullOrEmpty(title) ? "Building…" : title;

        gameObject.SetActive(true);

        if (barFill)
            barFill.fillAmount = 0f;

        if (percentLabel)
            percentLabel.text = $"{_title} 0%"; // title + percent

        if (cg)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
    }

    public void SetProgress(float t)
    {
        t = Mathf.Clamp01(t);

        if (barFill)
            barFill.fillAmount = t;

        if (percentLabel)
            percentLabel.text = $"{_title} {Mathf.RoundToInt(t * 100f)}%"; // keep title visible
    }

    public void Hide()
    {
        if (!cg)
            cg = GetComponent<CanvasGroup>();
        StopAllCoroutines();
        StartCoroutine(Fade(0f));
    }

    IEnumerator Fade(float target, float d = 0.15f)
    {
        if (!cg)
            yield break;
        float start = cg.alpha,
            t = 0f;
        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(start, target, t / d);
            yield return null;
        }
        cg.alpha = target;
        if (Mathf.Approximately(target, 0f))
            gameObject.SetActive(false);
    }

    // Call this from menu buttons
    public void LoadQuiz(int gen, string typeKey)
    {
        PendingGen = gen; // 0 = full quiz
        PendingType = typeKey; // null for non-type quiz

        // ---- Title formatting ----
        string title;
        if (!string.IsNullOrEmpty(typeKey))
        {
            // Type quiz
            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            title = $"Loading {ti.ToTitleCase(typeKey)} type quiz…";
        }
        else if (gen == 0)
        {
            // Full quiz (Gen 1–9)
            title = "Loading Full Quiz…";
        }
        else
        {
            // Gen quiz -> "Loading Gen X – Region quiz…"
            // lightweight region map to avoid cross-class deps
            string[] regions =
            {
                "",
                "Kanto",
                "Johto",
                "Hoenn",
                "Sinnoh",
                "Unova",
                "Kalos",
                "Alola",
                "Galar",
                "Paldea",
            };
            string region = (gen >= 1 && gen < regions.Length) ? regions[gen] : $"Gen {gen}";
            title = $"Loading Gen {gen} – {region} quiz…";
        }

        Show(title, immediate: true); // ensure overlay shows the title now
        StartCoroutine(CoLoadQuiz());
    }

    IEnumerator CoLoadQuiz()
    {
        SetProgress(0f);
        SetVisible(true, immediate: false);

        // Phase 1: load the scene
        var op = SceneManager.LoadSceneAsync("Quiz", LoadSceneMode.Single);
        op.allowSceneActivation = true; // we’ll let it activate asap
        while (!op.isDone)
        {
            // Unity reports up to 0.9 while loading; map that to 0..0.7
            SetProgress(Mathf.Clamp01(op.progress / 0.9f) * 0.7f);
            yield return null;
        }

        // Phase 2: wait a frame so Quiz scene is fully awake
        yield return null;

        // Find the new QuizManager and let it do its heavy lifting with progress
        var qm = FindFirstObjectByType<QuizManager>();
        if (qm != null)
        {
            if (!string.IsNullOrEmpty(PendingType))
                qm.StartTypeQuiz(PendingType);
            else
                qm.StartGenQuiz(PendingGen);

            // call directly – QuizManager implements IQuizProgress
            yield return StartCoroutine(qm.BuildWithExternalProgress(SetProgress, 0.7f, 1f));
        }
        else
        {
            SetProgress(1f);
        }

        // Phase 3: hide
        yield return StartCoroutine(Fade(0f, 0.15f));
        cg.blocksRaycasts = false;
    }
}

// simple interface the quiz scene can optionally implement
public interface IQuizProgress
{
    // report maps progress into [from..to] segment (e.g., 0.7..1.0)
    IEnumerator BuildWithExternalProgress(Action<float> report, float from, float to);
}
