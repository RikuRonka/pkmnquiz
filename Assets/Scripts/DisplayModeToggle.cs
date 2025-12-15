using UnityEngine;
using UnityEngine.UI;

public class DisplayModeToggle : MonoBehaviour
{
    const string KEY_FULLSCREEN = "fullscreen";

    [SerializeField]
    Toggle fullscreenToggle;

    void Start()
    {
        bool fullscreen = PlayerPrefs.GetInt(KEY_FULLSCREEN, 1) == 1;

        if (fullscreenToggle)
        {
            fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
            fullscreenToggle.onValueChanged.RemoveAllListeners();
            fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        }

        Apply(fullscreen);
    }

    void SetFullscreen(bool fullscreen)
    {
        PlayerPrefs.SetInt(KEY_FULLSCREEN, fullscreen ? 1 : 0);
        PlayerPrefs.Save();
        Apply(fullscreen);
    }

    static void Apply(bool fullscreen)
    {
        if (!fullscreen)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.fullScreen = false;
            return;
        }

        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        Screen.fullScreen = true;
    }
}
