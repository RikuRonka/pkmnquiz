using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class UpdateInfo
{
    public string version,
        notes,
        url,
        sha256;
}

public class UpdaterRunner : MonoBehaviour
{
    [SerializeField]
    string manifestUrl =
        "https://raw.githubusercontent.com/RikuRonka/pkmnquiz/refs/heads/main/latest.json";

    [SerializeField]
    string gameExeName = "pkmnquiz.exe"; // Your exe filename

    [SerializeField]
    string updaterExeName = "Updater.exe"; // Must be next to game exe

    [SerializeField]
    int updaterWaitMs = 800; // small delay before file ops

    public event Action OnNoUpdate;
    public event Action<UpdateInfo> OnUpdateFound;
    public event Action<float> OnDownloadProgress; // 0..1
    public event Action<string> OnStatus; // optional logs for UI
    UpdateInfo _pending;

    public void CheckForUpdate() => StartCoroutine(CheckCo());

    IEnumerator CheckCo()
    {
#if UNITY_EDITOR
        // Project root (one level up from Assets)
        string local = Path.Combine(
            Directory.GetParent(Application.dataPath)!.FullName,
            "latest.json"
        );
        if (File.Exists(local))
        {
            string json = File.ReadAllText(local);
            var info = JsonUtility.FromJson<UpdateInfo>(json);
            if (info != null && !string.IsNullOrEmpty(info.version))
            {
                _pending = info;

                var current = new Version(Application.version);
                var latest = new Version(info.version);

                if (latest > current)
                    OnUpdateFound?.Invoke(info);
                else
                    OnNoUpdate?.Invoke();

                yield break; // don’t hit the network while in Editor
            }
            // fall through to web check if parse failed
        }
#endif
        {
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
                yield break;
            }

            var info = JsonUtility.FromJson<UpdateInfo>(req.downloadHandler.text);
            if (info == null || string.IsNullOrEmpty(info.version))
                yield break;
            _pending = info;

            var current = new Version(Application.version);
            var latest = new Version(info.version);

            if (latest > current)
                OnUpdateFound?.Invoke(info);
            else
                OnNoUpdate?.Invoke();
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
        OnStatus?.Invoke("Downloading update…");

        string tempZip = Path.Combine(
            Path.GetTempPath(),
            $"pkmnquiz_update_{Guid.NewGuid():N}.zip"
        );

        using (var req = UnityWebRequest.Get(info.url))
        {
            req.downloadHandler = new DownloadHandlerFile(tempZip);
            req.timeout = 300; // big file
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

        string gameDir = Path.GetDirectoryName(Application.dataPath)!; // for IL2CPP it’s …/_Data/.., but the exe dir is one up
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

        OnStatus?.Invoke("Applying update…");

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
