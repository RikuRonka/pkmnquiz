// Updater.csproj -> <TargetFramework>net6.0</TargetFramework>
// Add <UseWindowsForms>false</UseWindowsForms> etc. Keep it minimal.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

class Program
{
    static int Main(string[] args)
    {
        string zip = GetArg(args, "--zip");
        string target = GetArg(args, "--target");
        string exeName = GetArg(args, "--exe");
        int waitMs = int.TryParse(GetArg(args, "--waitms") ?? "0", out var w) ? w : 0;

        if (string.IsNullOrWhiteSpace(zip) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(exeName))
        {
            Console.Error.WriteLine("Usage: Updater.exe --zip <path> --target <dir> --exe <Game.exe> [--waitms 1000]");
            return 2;
        }

        if (!File.Exists(zip))
        {
            Console.Error.WriteLine("Zip not found: " + zip);
            return 3;
        }

        if (!Directory.Exists(target))
        {
            Console.Error.WriteLine("Target dir not found: " + target);
            return 4;
        }

        // 1) Wait a little for the main app to exit
        Thread.Sleep(waitMs);

        // Also try to ensure no process with exeName is running
        for (int i = 0; i < 50; i++)
        {
            if (!IsProcessRunning(exeName)) break;
            Thread.Sleep(200);
        }

        // 2) Extract to a staging folder to avoid partial writes
        string staging = Path.Combine(Path.GetTempPath(), "pkmnquiz_update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            ZipFile.ExtractToDirectory(zip, staging, true);
            string root = staging;
            var entries = Directory.GetFileSystemEntries(staging);
            if (entries.Length == 1 && Directory.Exists(entries[0]))
            {
                root = entries[0]; // use inner folder as root
                Console.WriteLine("Detected single-root folder in zip. Using: " + root);
            }

            // 3) Copy all files from staging over target
            CopyAll(new DirectoryInfo(root), new DirectoryInfo(target));

            // 4) Relaunch game
            string exePath = Directory.GetFiles(target, exeName, SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (string.IsNullOrEmpty(exePath))
            {
                Console.Error.WriteLine("Cannot find game exe in target: " + exeName);
                return 5;
            }

            var p = new Process();
            p.StartInfo.FileName = exePath;
            p.StartInfo.WorkingDirectory = target;
            p.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Update failed: " + ex);
            return 6;
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
            // optionally delete the zip:
            try { File.Delete(zip); } catch { }
        }

        return 0;
    }

    static string GetArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    static bool IsProcessRunning(string exeName)
    {
        exeName = exeName.Trim().ToLowerInvariant();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = Path.GetFileName(p.MainModule?.FileName ?? "").ToLowerInvariant();
                if (name == exeName) return true;
            }
            catch { /* access denied */ }
        }
        return false;
    }

    static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (var dir in source.GetDirectories())
        {
            var destDir = new DirectoryInfo(Path.Combine(target.FullName, dir.Name));
            if (!destDir.Exists) destDir.Create();
            CopyAll(dir, destDir);
        }
        foreach (var file in source.GetFiles())
        {
            string dest = Path.Combine(target.FullName, file.Name);
            file.CopyTo(dest, true);
        }
    }
}
