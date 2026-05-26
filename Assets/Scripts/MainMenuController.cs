using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    public Button fullQuizBtn;

    void Awake()
    {
        MultiplayerMenuPanel.EnsureInScene();
        SingleplayerProgressResetPanel.EnsureInScene();

        bool fullscreen = PlayerPrefs.GetInt("fullscreen", 1) == 1;
        Screen.fullScreen = fullscreen;
        Screen.fullScreenMode = fullscreen
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;
        if (fullQuizBtn)
        {
            fullQuizBtn.onClick.RemoveAllListeners();
            fullQuizBtn.onClick.AddListener(() => PlayFullQuiz());
        }
    }

    public static async void PlayFullQuiz()
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(0))
            return;

        QuizNetworkRuntime.Shutdown();
        GameSettings.Generation = 0;
        GameSettings.TypeFilter = null;
        GameSettings.ArmQuizLaunch();
        SceneManager.LoadScene("Quiz");
    }

    public static void PlayGen(int gen)
    {
        PlayGenAsync(gen);
    }

    private static async void PlayGenAsync(int gen)
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(gen))
            return;

        QuizNetworkRuntime.Shutdown();
        GameSettings.Generation = gen;
        GameSettings.TypeFilter = null;
        GameSettings.ArmQuizLaunch();
        SceneManager.LoadScene("Quiz");
    }

    public static void PlayType(string typeName)
    {
        PlayTypeAsync(typeName);
    }

    private static async void PlayTypeAsync(string typeName)
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(0, typeName))
            return;

        QuizNetworkRuntime.Shutdown();
        GameSettings.Generation = null;
        GameSettings.TypeFilter = new[] { typeName };
        GameSettings.ArmQuizLaunch();
        SceneManager.LoadScene("Quiz");
    }

    public static void Quit()
    {
        Application.Quit();
    }
}
