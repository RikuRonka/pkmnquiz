using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class UpdateInfo
{
    public string version;
    public string notes;
    public string url;
    public string sha256;

    public static UpdateInfo Instance;

    public UpdateInfo()
    {
        Instance = this;
    }
}

public class UpdateChecker : MonoBehaviour
{
    private const string manifestUrl =
        "https://raw.githubusercontent.com/RikuRonka/pkmnquiz/refs/heads/main/latest.json";

    public event Action OnNoUpdate;
    public event Action<UpdateInfo> OnUpdateFound;

    public void CheckNow() => StartCoroutine(CheckForUpdate());

    IEnumerator CheckForUpdate()
    {
        // Add a cache-buster so you never see a stale 404/old file
        var url = $"{manifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        using var req = UnityWebRequest.Get(url);
        req.timeout = 15;
        req.SetRequestHeader("User-Agent", "pkmnquiz-updater");
        req.SetRequestHeader("Accept", "application/json");
        req.SetRequestHeader("Cache-Control", "no-cache");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning(
                $"Update check failed [{req.responseCode}] {req.error}\nURL: {url}\nBody: {req.downloadHandler.text}"
            );
            yield break;
        }

        var json = req.downloadHandler.text.Trim();

        UpdateInfo info = null;
        try
        {
            info = JsonUtility.FromJson<UpdateInfo>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to parse manifest: {ex.Message}");
            yield break;
        }

        if (info == null || string.IsNullOrEmpty(info.version))
        {
            Debug.LogWarning("Manifest missing version.");
            yield break;
        }

        var current = new Version(Application.version);
        var latest = new Version(info.version);

        if (latest > current)
            OnUpdateFound?.Invoke(info);
        else
            OnNoUpdate?.Invoke();
    }
}
