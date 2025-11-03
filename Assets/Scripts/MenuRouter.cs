using UnityEngine;

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

    public void PlayFullQuiz()
    {
        EnsureLoader();
        LoadingManager.Instance.LoadQuiz(0, null);
    }

    public void PlayTypeQuiz(string typeKey)
    {
        EnsureLoader();
        LoadingManager.Instance.LoadQuiz(0, typeKey);
    }

    public void PlayGenQuiz(int gen)
    {
        EnsureLoader();
        LoadingManager.Instance.LoadQuiz(gen, null);
    }
}
