// MusicManager.cs
using UnityEngine;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Music")]
    [SerializeField]
    AudioClip musicClip; // assign your one background track

    [SerializeField, Range(0f, 1f)]
    float defaultVolume = 0.6f;

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

        src = gameObject.GetComponent<AudioSource>();
        if (!src)
            src = gameObject.AddComponent<AudioSource>();
        src.loop = true;
        src.playOnAwake = false;

        // Load prefs (comment these out if you DON'T want session persistence)
        isOn = PlayerPrefs.GetInt(KEY_ON, 1) == 1;
        volume = PlayerPrefs.GetFloat(KEY_VOL, defaultVolume);

        // Configure and maybe start playing
        src.clip = musicClip;
        src.volume = volume;
        if (isOn && src.clip)
            src.Play();
    }

    public bool IsOn => isOn;
    public float Volume => volume;

    public void SetEnabled(bool on)
    {
        isOn = on;
        PlayerPrefs.SetInt(KEY_ON, on ? 1 : 0);

        if (on)
        {
            if (src.clip && !src.isPlaying)
                src.Play();
            src.mute = false;
        }
        else
        {
            src.mute = true; // keeps time position in case you re-enable
            // Or: src.Pause(); if you prefer pausing
        }
    }

    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        src.volume = volume;
        PlayerPrefs.SetFloat(KEY_VOL, volume);
    }
}
