using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    public Button fullQuizBtn;

    void Awake()
    {
        bool fullscreen = PlayerPrefs.GetInt("fullscreen", 1) == 1;
        Screen.fullScreen = fullscreen;
        Screen.fullScreenMode = fullscreen
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;
        if (fullQuizBtn)
        {
            fullQuizBtn.onClick.RemoveAllListeners();
            fullQuizBtn.onClick.AddListener(() =>
            {
                GameSettings.Generation = 0;
                GameSettings.TypeFilter = null;
                SceneManager.LoadScene("Quiz");
            });
        }
    }

    public static void PlayGen(int gen)
    {
        GameSettings.Generation = gen;
        GameSettings.TypeFilter = null;
        SceneManager.LoadScene("Quiz");
    }

    public static void PlayType(string typeName)
    {
        GameSettings.Generation = null;
        GameSettings.TypeFilter = new[] { typeName };
        SceneManager.LoadScene("Quiz");
    }

    public static void Quit()
    {
        Application.Quit();
    }
}
