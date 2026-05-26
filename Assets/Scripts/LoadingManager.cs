using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
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
    public bool IsLoading => _loadCo != null;
    string _title = "Building…";
    Coroutine _fadeCo;
    Coroutine _loadCo;

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (transform.parent != null)
            transform.SetParent(null, false);

        DontDestroyOnLoad(gameObject);

        SetProgress(0f);
        SetVisible(false, immediate: true);
    }

    void SetVisible(bool on, bool immediate = false)
    {
        gameObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        gameObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler
            .ScaleMode
            .ScaleWithScreenSize;
        if (!cg)
            return;

        if (_fadeCo != null)
        {
            StopCoroutine(_fadeCo);
            _fadeCo = null;
        }

        if (immediate)
        {
            cg.alpha = on ? 1f : 0f;
            cg.blocksRaycasts = on;
            cg.interactable = on;
            return;
        }

        _fadeCo = StartCoroutine(Fade(on ? 1f : 0f, 0.15f));
        cg.blocksRaycasts = on;
        cg.interactable = on;
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
        if (_fadeCo != null)
        {
            StopCoroutine(_fadeCo);
            _fadeCo = null;
        }
        _fadeCo = StartCoroutine(Fade(0f));
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
        _fadeCo = null; // finished
    }

    public void LoadQuiz(int gen, string typeKey)
    {
        if (_loadCo != null) // <-- the guard
        {
            Debug.LogWarning("[Loader] Ignored duplicate LoadQuiz; already loading.");
            return;
        }

        PendingGen = gen;
        PendingType = typeKey;

        var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
        string title = !string.IsNullOrEmpty(typeKey)
            ? $"Loading {ti.ToTitleCase(typeKey)} type quiz…"
            : (gen == 0 ? "Loading Full Quiz…" : $"Loading Gen {gen} quiz…");

        Show(title, immediate: true);
        SetProgress(0f);

        _loadCo = StartCoroutine(CoLoadQuiz()); // <-- store handle
    }

    public void CancelLoad()
    {
        if (_loadCo != null)
        {
            StopCoroutine(_loadCo);
            _loadCo = null;
        }
        SetVisible(false, immediate: true);
        SetProgress(0f);
    }

    IEnumerator CoLoadQuiz()
    {
        try
        {
            SetProgress(0f);
            SetVisible(true);

            GameSettings.ArmQuizLaunch();
            var op = SceneManager.LoadSceneAsync("Quiz", LoadSceneMode.Single);
            op.allowSceneActivation = true;

            while (!op.isDone)
            {
                float mapped = Mathf.Clamp01(op.progress / 0.9f) * 0.7f;
                SetProgress(mapped);
                yield return null;
            }

            yield return null;

            var qm = FindFirstObjectByType<QuizManager>();
            if (qm != null)
            {
                if (!string.IsNullOrEmpty(PendingType))
                    qm.StartTypeQuiz(PendingType);
                else
                    qm.StartGenQuiz(PendingGen);

                if (qm.TryGetComponent<IQuizProgress>(out var progressApi))
                    yield return StartCoroutine(
                        progressApi.BuildWithExternalProgress(SetProgress, 0.7f, 1f)
                    );
                else
                {
                    float t = 0.7f;
                    while (t < 1f)
                    {
                        t += Time.unscaledDeltaTime * 0.6f;
                        SetProgress(t);
                        yield return null;
                    }
                }
            }
            else
            {
                SetProgress(1f);
            }

            yield return StartCoroutine(Fade(0f, 0.15f));
            cg.blocksRaycasts = false;
        }
        finally
        {
            _loadCo = null;
            PendingGen = 0;
            PendingType = null;
            SetProgress(0f);
        }
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (_loadCo != null && s.name == "Quiz")
            return;

        _loadCo = null;
        SetProgress(0f);
        SetVisible(false, immediate: true);
        if (cg)
            cg.blocksRaycasts = false;
    }
}

public interface IQuizProgress
{
    IEnumerator BuildWithExternalProgress(Action<float> report, float from, float to);
}
