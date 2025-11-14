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

    public void PlayFullQuiz()
    {
        EnsureLoader();
        GameSettings.Generation = 0;
        GameSettings.TypeFilter = null;
        LoadingManager.Instance.LoadQuiz(0, null);
    }

    public void PlayTypeQuiz(string typeKey)
    {
        GameSettings.TypeBgColor = GameObject
            .Find(typeKey.FirstCharacterToUpper())
            .GetComponent<Button>()
            .colors.normalColor;
        GameSettings.Generation = null;
        GameSettings.TypeFilter = new[] { typeKey };
        EnsureLoader();
        LoadingManager.Instance.LoadQuiz(0, typeKey);
    }

    public void PlayGenQuiz(int gen)
    {
        GameSettings.Generation = gen;
        GameSettings.TypeFilter = null;
        EnsureLoader();
        LoadingManager.Instance.LoadQuiz(gen, null);
    }
}
