using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class UpdateInfo
{
    public string version, url, sha256;
    public string[] notes;

    public string NotesText
    {
        get
        {
            if (notes == null || notes.Length == 0)
                return string.Empty;

            var cleaned = new List<string>(notes.Length);
            foreach (var note in notes)
            {
                if (!string.IsNullOrWhiteSpace(note))
                    cleaned.Add(note.Trim());
            }

            return string.Join("\n", cleaned);
        }
    }

    public void Normalize()
    {
        notes ??= Array.Empty<string>();
    }
}

[Serializable]
internal sealed class LegacyUpdateInfo
{
    public string version = string.Empty;
    public string notes = string.Empty;
    public string url = string.Empty;
    public string sha256 = string.Empty;
}

public class UpdaterRunner : MonoBehaviour
{
    [SerializeField]
    string manifestUrl =
        "https://raw.githubusercontent.com/RikuRonka/pkmnquiz/refs/heads/main/latest.json";

    [SerializeField]
    string gameExeName = "pkmnquiz.exe";

    [SerializeField]
    string updaterExeName = "Updater.exe";

    [SerializeField]
    int updaterWaitMs = 800;

    public event Action OnNoUpdate;
    public event Action<UpdateInfo> OnUpdateFound;
    public event Action<float> OnDownloadProgress;
    public event Action<string> OnStatus;
    public event Action<string> OnCheckFailed;
    UpdateInfo _pending;

    public void CheckForUpdate() => StartCoroutine(CheckCo());

    IEnumerator CheckCo()
    {
#if UNITY_EDITOR
        string local = Path.Combine(
            Directory.GetParent(Application.dataPath)!.FullName,
            "latest.json"
        );
        if (File.Exists(local))
        {
            string json = File.ReadAllText(local);
            var info = ParseUpdateInfo(json);
            if (info != null && !string.IsNullOrEmpty(info.version))
            {
                FinishUpdateCheck(info);
                yield break;
            }
        }
#endif
        string url = $"{manifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        using var req = UnityWebRequest.Get(url);
        req.timeout = 15;
        req.SetRequestHeader("User-Agent", "pkmnquiz-updater");
        req.SetRequestHeader("Accept", "application/json");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            UnityEngine.Debug.LogWarning(
                $"Update check failed [{req.responseCode}] {req.error} URL: {url}"
            );
            OnCheckFailed?.Invoke("Update check failed");
            yield break;
        }

        var remoteInfo = ParseUpdateInfo(req.downloadHandler.text);
        if (remoteInfo == null || string.IsNullOrEmpty(remoteInfo.version))
        {
            OnCheckFailed?.Invoke("Invalid update info");
            yield break;
        }

        FinishUpdateCheck(remoteInfo);
    }

    static UpdateInfo ParseUpdateInfo(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var info = JsonUtility.FromJson<UpdateInfo>(json);
            if (info == null)
                return null;

            if (info.notes == null || info.notes.Length == 0)
            {
                var legacy = JsonUtility.FromJson<LegacyUpdateInfo>(json);
                if (legacy != null && !string.IsNullOrWhiteSpace(legacy.notes))
                    info.notes = SplitLegacyNotes(legacy.notes);
            }

            info.Normalize();
            return info;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"Update info parse failed: {ex.Message}");
            return null;
        }
    }

    static string[] SplitLegacyNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return Array.Empty<string>();

        notes = notes.Replace("\r", "").Trim();
        var parts = Regex.Split(notes, @"(?m)^\s*[-\u2022]\s+|\s+-\s+|\n+");
        var cleaned = new List<string>();
        foreach (var part in parts)
        {
            var item = Regex.Replace(part.Trim(), @"\s+", " ");
            if (!string.IsNullOrWhiteSpace(item))
                cleaned.Add(item);
        }

        return cleaned.ToArray();
    }

    void FinishUpdateCheck(UpdateInfo info)
    {
        _pending = info;

        if (!TryIsNewerVersion(info.version, out bool newer))
        {
            OnCheckFailed?.Invoke("Version check failed");
            return;
        }

        if (newer)
            OnUpdateFound?.Invoke(info);
        else
            OnNoUpdate?.Invoke();
    }

    static bool TryIsNewerVersion(string latestVersion, out bool newer)
    {
        newer = false;

        try
        {
            var current = new Version(Application.version);
            var latest = new Version(latestVersion);
            newer = latest > current;
            return true;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"Update version comparison failed: {ex.Message}");
            return false;
        }
    }

    public void StartUpdate()
    {
        if (_pending == null)
        {
            UnityEngine.Debug.LogWarning("No pending update.");
            return;
        }

        StartCoroutine(DownloadAndRun(_pending));
    }

    IEnumerator DownloadAndRun(UpdateInfo info)
    {
        OnStatus?.Invoke("Downloading update...");

        string tempZip = Path.Combine(
            Path.GetTempPath(),
            $"pkmnquiz_update_{Guid.NewGuid():N}.zip"
        );

        using (var req = UnityWebRequest.Get(info.url))
        {
            req.downloadHandler = new DownloadHandlerFile(tempZip);
            req.timeout = 300;
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                OnDownloadProgress?.Invoke(req.downloadProgress);
                yield return null;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.LogError($"Download failed: {req.error}");
                yield break;
            }
        }

        string gameDir = Path.GetDirectoryName(Application.dataPath)!;
        if (Application.platform == RuntimePlatform.WindowsPlayer)
        {
            var maybeExeDir = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(maybeExeDir))
                gameDir = maybeExeDir;
        }

        string updaterPath = Path.Combine(gameDir, updaterExeName);
        if (!File.Exists(updaterPath))
        {
            UnityEngine.Debug.LogError($"Updater not found at: {updaterPath}");
            yield break;
        }

        var args =
            $"--zip \"{tempZip}\" --target \"{gameDir}\" --exe \"{gameExeName}\" --waitms {updaterWaitMs}";

        OnStatus?.Invoke("Applying update...");

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = args,
                    WorkingDirectory = gameDir,
                    UseShellExecute = false,
                }
            );
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to start updater: {e.Message}");
            yield break;
        }

        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
