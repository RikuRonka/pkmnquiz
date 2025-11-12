using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MusicUIController : MonoBehaviour
{
    [SerializeField]
    Toggle musicToggle;

    [SerializeField]
    Slider volumeSlider;

    bool suppress;
    bool bound;

    void OnEnable()
    {
        TryBindOrWait();
    }

    void OnDisable()
    {
        Unbind();
    }

    void TryBindOrWait()
    {
        var m = MusicManager.Instance;
        if (m)
        {
            Bind(m);
        }
        else
        {
            StartCoroutine(BindNextFrame());
        }
    }

    IEnumerator BindNextFrame()
    {
        yield return null;
        var m = MusicManager.Instance;
        if (m)
            Bind(m);
    }

    void Bind(MusicManager m)
    {
        if (bound)
            return;

        suppress = true;
        if (musicToggle)
            musicToggle.isOn = m.IsOn;
        if (volumeSlider)
            volumeSlider.value = m.Volume;
        suppress = false;

        if (musicToggle)
            musicToggle.onValueChanged.AddListener(OnToggleChanged);
        if (volumeSlider)
            volumeSlider.onValueChanged.AddListener(OnSliderChanged);

        m.EnabledChanged += ReflectEnabled;
        m.VolumeChanged += ReflectVolume;

        bound = true;
    }

    void Unbind()
    {
        if (!bound)
            return;

        var m = MusicManager.Instance;

        if (musicToggle)
            musicToggle.onValueChanged.RemoveListener(OnToggleChanged);
        if (volumeSlider)
            volumeSlider.onValueChanged.RemoveListener(OnSliderChanged);

        if (m)
        {
            m.EnabledChanged -= ReflectEnabled;
            m.VolumeChanged -= ReflectVolume;
        }

        bound = false;
    }

    void OnToggleChanged(bool on)
    {
        if (!suppress)
            MusicManager.Instance?.SetEnabled(on);
    }

    void OnSliderChanged(float v)
    {
        if (!suppress)
            MusicManager.Instance?.SetVolume(v);
    }

    void ReflectEnabled(bool on)
    {
        if (!musicToggle)
            return;
        suppress = true;
        musicToggle.isOn = on;
        suppress = false;
    }

    void ReflectVolume(float v)
    {
        if (!volumeSlider)
            return;
        suppress = true;
        volumeSlider.value = v;
        suppress = false;
    }
}
