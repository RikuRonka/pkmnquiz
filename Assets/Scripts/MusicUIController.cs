using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MusicUIController : MonoBehaviour
{
    [SerializeField]
    private Toggle musicToggle;

    [SerializeField]
    private Slider volumeSlider;

    [Header("New")]
    [SerializeField]
    private TMP_Dropdown songDropdown;

    private bool suppress;
    private bool bound;

    void OnEnable() => TryBindOrWait();

    void OnDisable() => Unbind();

    void Awake()
    {
        if (!songDropdown)
            songDropdown = GameObject.Find("musicDropDown")?.GetComponent<TMP_Dropdown>();
    }

    void TryBindOrWait()
    {
        var m = MusicManager.Instance;
        if (m)
            Bind(m);
        else
            StartCoroutine(BindNextFrame());
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

        if (songDropdown)
        {
            PopulateDropdown(m);
            songDropdown.value = m.TrackIndex;
            songDropdown.RefreshShownValue();
        }

        suppress = false;

        if (musicToggle)
            musicToggle.onValueChanged.AddListener(OnToggleChanged);
        if (volumeSlider)
            volumeSlider.onValueChanged.AddListener(OnSliderChanged);
        if (songDropdown)
            songDropdown.onValueChanged.AddListener(OnSongChanged);

        m.EnabledChanged += ReflectEnabled;
        m.VolumeChanged += ReflectVolume;
        m.TrackChanged += ReflectTrack;

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
        if (songDropdown)
            songDropdown.onValueChanged.RemoveListener(OnSongChanged);

        if (m)
        {
            m.EnabledChanged -= ReflectEnabled;
            m.VolumeChanged -= ReflectVolume;
            m.TrackChanged -= ReflectTrack;
        }

        bound = false;
    }

    void PopulateDropdown(MusicManager m)
    {
        songDropdown.ClearOptions();

        var opts = new List<string>();
        int count = m.TrackCount;

        if (count <= 0)
        {
            opts.Add("No tracks");
            songDropdown.AddOptions(opts);
            songDropdown.interactable = false;
            return;
        }

        for (int i = 0; i < count; i++)
            opts.Add(m.GetTrackName(i));

        songDropdown.AddOptions(opts);
        songDropdown.interactable = true;
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

    void OnSongChanged(int index)
    {
        if (!suppress)
            MusicManager.Instance?.SetTrack(index, restart: true);
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

    void ReflectTrack(int index)
    {
        if (!songDropdown)
            return;
        suppress = true;

        // If playlist changed at runtime, ensure dropdown matches.
        var m = MusicManager.Instance;
        if (m)
            PopulateDropdown(m);

        songDropdown.value = Mathf.Clamp(index, 0, Mathf.Max(0, songDropdown.options.Count - 1));
        songDropdown.RefreshShownValue();

        suppress = false;
    }
}
