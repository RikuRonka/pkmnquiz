using System;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Playlist")]
    [SerializeField]
    private AudioClip[] tracks;

    [SerializeField]
    private int defaultTrackIndex = 0;

    [Header("Defaults")]
    [SerializeField, Range(0f, 1f)]
    private float defaultVolume = 0.6f;

    public event Action<bool> EnabledChanged;
    public event Action<float> VolumeChanged;
    public event Action<int> TrackChanged;

    private AudioSource src;
    private bool isOn;
    private float volume;
    private int trackIndex;

    private const string KEY_ON = "music_on";
    private const string KEY_VOL = "music_vol";
    private const string KEY_TRACK = "music_track";

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
        src.playOnAwake = false;
        src.spatialBlend = 0f;

        isOn = PlayerPrefs.GetInt(KEY_ON, 1) == 1;
        volume = PlayerPrefs.GetFloat(KEY_VOL, defaultVolume);

        trackIndex = PlayerPrefs.GetInt(KEY_TRACK, defaultTrackIndex);
        trackIndex = ClampTrackIndex(trackIndex);

        src.volume = volume;
        src.mute = !isOn;

        ApplyTrack(trackIndex, restartIfPlaying: false);

        if (isOn && src.clip)
            src.Play();
    }

    public bool IsOn => isOn;
    public float Volume => volume;
    public int TrackIndex => trackIndex;

    public int TrackCount => tracks != null ? tracks.Length : 0;

    public string GetTrackName(int index)
    {
        if (tracks == null || index < 0 || index >= tracks.Length || tracks[index] == null)
            return "None";
        return tracks[index].name;
    }

    public void SetEnabled(bool on)
    {
        if (isOn == on)
        {
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
            src.mute = true;
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

        src.volume = volume;
        VolumeChanged?.Invoke(volume);
    }

    public void SetTrack(int index, bool restart = true)
    {
        index = ClampTrackIndex(index);
        if (trackIndex == index)
        {
            // Still notify if you want UI consistency:
            TrackChanged?.Invoke(trackIndex);
            return;
        }

        trackIndex = index;
        PlayerPrefs.SetInt(KEY_TRACK, trackIndex);

        ApplyTrack(trackIndex, restartIfPlaying: restart);

        TrackChanged?.Invoke(trackIndex);
    }

    private void ApplyTrack(int index, bool restartIfPlaying)
    {
        AudioClip next = null;
        if (tracks != null && index >= 0 && index < tracks.Length)
            next = tracks[index];

        bool wasPlaying = src.isPlaying;
        src.clip = next;

        if (isOn && src.clip)
        {
            if (restartIfPlaying || !wasPlaying)
            {
                src.Stop();
                src.Play();
            }
        }
        else
        {
            // If music is off, keep it muted/idle.
            src.Stop();
        }
    }

    private int ClampTrackIndex(int index)
    {
        int count = TrackCount;
        if (count <= 0)
            return 0;
        return Mathf.Clamp(index, 0, count - 1);
    }

    public void SyncUI()
    {
        EnabledChanged?.Invoke(isOn);
        VolumeChanged?.Invoke(volume);
        TrackChanged?.Invoke(trackIndex);
    }
}
