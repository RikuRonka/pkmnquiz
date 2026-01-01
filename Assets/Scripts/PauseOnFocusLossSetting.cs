using UnityEngine;
using UnityEngine.UI;

public class PauseOnFocusLossSetting : MonoBehaviour
{
    const string KEY = "pause_on_focus_loss";

    [SerializeField]
    Toggle toggle;

    void Awake()
    {
        if (!toggle)
            toggle = GetComponent<Toggle>();

        bool enabled = PlayerPrefs.GetInt(KEY, 1) == 1;
        toggle.SetIsOnWithoutNotify(enabled);

        toggle.onValueChanged.AddListener(OnToggleChanged);
    }

    void OnToggleChanged(bool on)
    {
        PlayerPrefs.SetInt(KEY, on ? 1 : 0);
        PlayerPrefs.Save();
    }
}
