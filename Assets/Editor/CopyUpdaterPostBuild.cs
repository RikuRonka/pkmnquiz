#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class CopyUpdaterPostBuild
{
    // Runs after BuildPipeline.BuildPlayer finishes
    [PostProcessBuild(1000)]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        // Only for Windows builds
        if (target != BuildTarget.StandaloneWindows && target != BuildTarget.StandaloneWindows64)
            return;

        // <projectRoot>/Updater.exe   (you have it at project root)
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        string src = Path.Combine(projectRoot, "Updater.exe");
        if (!File.Exists(src))
        {
            Debug.LogWarning($"[Updater] Not found at: {src}");
            return;
        }

        // Destination: directory of the built game
        string buildDir = Path.GetDirectoryName(pathToBuiltProject)!;
        string dst = Path.Combine(buildDir, "Updater.exe");

        try
        {
            File.Copy(src, dst, overwrite: true);
            Debug.Log($"[Updater] Copied -> {dst}");
        }
        catch (IOException ex)
        {
            Debug.LogError($"[Updater] Copy failed: {ex.Message}");
        }
    }
}
#endif
