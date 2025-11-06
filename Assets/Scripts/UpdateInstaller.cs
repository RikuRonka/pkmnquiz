using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

public class UpdateInstaller : MonoBehaviour
{
    // Hook this to your “Update” button
    public void StartInstall(UpdateInfo info)
    {
        StartCoroutine(DownloadAndInstall(info));
    }

    IEnumerator DownloadAndInstall(UpdateInfo info)
    {
        string url = info.url; // from latest.json
        string zipPath = Path.Combine(Application.temporaryCachePath, "update.zip");
        string updaterPath = Path.Combine(Application.dataPath, "..", "Updater.exe"); // put Updater.exe next to your game exe
        string installDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);

        // 1) Download
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 60;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.LogError("Update download failed: " + req.error);
                yield break;
            }
            File.WriteAllBytes(zipPath, req.downloadHandler.data);
        }

        // 2) Optional: verify SHA256
        if (
            !string.IsNullOrEmpty(info.sha256)
            && info.sha256 != "optional-checksum-here"
            && !VerifySha256(zipPath, info.sha256)
        )
        {
            UnityEngine.Debug.LogError("Checksum mismatch. Aborting update.");
            yield break;
        }

        // 3) Launch updater and quit
        if (!File.Exists(updaterPath))
        {
            UnityEngine.Debug.LogError("Updater.exe not found at: " + updaterPath);
            yield break;
        }

        // Pass: zipPath, installDir, exeName
        var psi = new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments =
                $"--zip \"{zipPath}\" --target \"{installDir}\" --exe \"{exeName}\" --waitms 1000",
            UseShellExecute = false,
        };
        Process.Start(psi);

        // IMPORTANT: exit the game so files can be replaced
        Application.Quit();
    }

    bool VerifySha256(string filePath, string expectedHex)
    {
        expectedHex = expectedHex.Trim().ToLowerInvariant();
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var hash = sha.ComputeHash(fs);
        var got = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return got == expectedHex;
    }
}
