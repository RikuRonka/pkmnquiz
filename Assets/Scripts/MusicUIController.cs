// MusicUIController.cs
using UnityEngine;
using UnityEngine.UI;

public class MusicUIController : MonoBehaviour
{
    [SerializeField]
    Toggle musicToggle;

    [SerializeField]
    Slider volumeSlider;

    void OnEnable()
    {
        var m = MusicManager.Instance;
        if (!m)
            return;

        if (musicToggle)
        {
            musicToggle.onValueChanged.RemoveAllListeners();
            musicToggle.isOn = m.IsOn;
            musicToggle.onValueChanged.AddListener(m.SetEnabled);
        }

        if (volumeSlider)
        {
            volumeSlider.onValueChanged.RemoveAllListeners();
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.value = m.Volume;
            volumeSlider.onValueChanged.AddListener(m.SetVolume);
        }
    }
}
