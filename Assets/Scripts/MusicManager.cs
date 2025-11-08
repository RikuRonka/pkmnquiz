using System;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [SerializeField]
    AudioClip musicClip;

    [SerializeField, Range(0f, 1f)]
    float defaultVolume = 0.6f;

    public event Action<bool> EnabledChanged;
    public event Action<float> VolumeChanged;

    AudioSource src;
    bool isOn;
    float volume;

    const string KEY_ON = "music_on";
    const string KEY_VOL = "music_vol";

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        src = GetComponent<AudioSource>();
        src.loop = true;
        src.playOnAwake = false; // don't auto-play; we control Play/Stop
        src.spatialBlend = 0f; // 2D

        // Load prefs
        isOn = PlayerPrefs.GetInt(KEY_ON, 1) == 1;
        volume = PlayerPrefs.GetFloat(KEY_VOL, defaultVolume);

        // Apply
        src.clip = musicClip;
        src.volume = volume;
        src.mute = !isOn;
        if (isOn && src.clip)
            src.Play();
    }

    public bool IsOn => isOn;
    public float Volume => volume;

    public void SetEnabled(bool on)
    {
        if (isOn == on)
        { // still reflect/mute state & notify UIs
            src.mute = !on;
            EnabledChanged?.Invoke(isOn);
            return;
        }
        isOn = on;
        PlayerPrefs.SetInt(KEY_ON, on ? 1 : 0);

        if (on)
        {
            src.mute = false;
            if (src.clip && !src.isPlaying)
                src.Play();
        }
        else
        {
            src.mute = true; /* or src.Pause(); */
        }

        EnabledChanged?.Invoke(isOn);
    }

    public void SetVolume(float v)
    {
        v = Mathf.Clamp01(v);
        if (!Mathf.Approximately(volume, v))
        {
            volume = v;
            PlayerPrefs.SetFloat(KEY_VOL, volume);
        }
        src.volume = volume; // always apply
        VolumeChanged?.Invoke(volume); // always notify
    }

    // Handy when a UI appears and wants the current state immediately
    public void SyncUI()
    {
        EnabledChanged?.Invoke(isOn);
        VolumeChanged?.Invoke(volume);
    }
}
