using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class MenuRouter : MonoBehaviour
{
    [SerializeField]
    LoadingManager loadingPrefab;
    public static MenuRouter Instance { get; private set; }

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void EnsureLoader()
    {
        if (LoadingManager.Instance)
            return;

        var lm = Instantiate(loadingPrefab);
        lm.name = "LoadingOverlay (Singleton)";
        lm.transform.SetParent(null, false);
    }

    public async void PlayFullQuiz()
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(0))
            return;

        QuizNetworkRuntime.Shutdown();
        EnsureLoader();
        GameSettings.Generation = 0;
        GameSettings.TypeFilter = null;
        LoadingManager.Instance.LoadQuiz(0, null);
    }

    public async void PlayTypeQuiz(string typeKey)
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(0, typeKey))
            return;

        QuizNetworkRuntime.Shutdown();
        GameSettings.TypeBgColor = GameObject
            .Find(typeKey.FirstCharacterToUpper())
            .GetComponent<Button>()
            .colors.normalColor;
        GameSettings.Generation = null;
        GameSettings.TypeFilter = new[] { typeKey };
        EnsureLoader();
        LoadingManager.Instance.LoadQuiz(0, typeKey);
    }

    public async void PlayGenQuiz(int gen)
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(gen))
            return;

        QuizNetworkRuntime.Shutdown();
        GameSettings.Generation = gen;
        GameSettings.TypeFilter = null;
        EnsureLoader();
        LoadingManager.Instance.LoadQuiz(gen, null);
    }

    public async void PlayMegaEvolutionsQuiz()
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(10))
            return;

        QuizNetworkRuntime.Shutdown();
        GameSettings.Generation = 10;
        GameSettings.TypeFilter = null;
        EnsureLoader();
        LoadingManager.Instance.LoadQuiz(10, null);
    }
}
