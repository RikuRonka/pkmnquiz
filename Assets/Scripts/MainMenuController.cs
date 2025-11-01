using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    public Button fullQuizBtn;

    void Awake()
    {
        // ... existing wiring
        if (fullQuizBtn)
        {
            fullQuizBtn.onClick.RemoveAllListeners();
            fullQuizBtn.onClick.AddListener(() =>
            {
                GameSettings.Generation = 0; // FULL (1–9)
                GameSettings.TypeFilter = null; // reset type filter if you use it
                SceneManager.LoadScene("Quiz");
            });
        }
    }

    public void PlayGen(int gen)
    {
        GameSettings.Generation = gen;
        GameSettings.TypeFilter = null;
        SceneManager.LoadScene("Quiz");
    }

    public void PlayType(string typeName)
    {
        GameSettings.Generation = null;
        GameSettings.TypeFilter = new[] { typeName };
        SceneManager.LoadScene("Quiz");
    }
}
