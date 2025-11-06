using System.Diagnostics;
using System.IO.Compression;

class Program
{
    static int Main(string[] args)
    {
        string zip = GetArg(args, "--zip");
        string target = GetArg(args, "--target");
        string exeName = GetArg(args, "--exe");
        int waitMs = int.TryParse(GetArg(args, "--waitms") ?? "0", out var w) ? w : 0;

        if (
            string.IsNullOrWhiteSpace(zip)
            || string.IsNullOrWhiteSpace(target)
            || string.IsNullOrWhiteSpace(exeName)
        )
        {
            Console.Error.WriteLine(
                "Usage: Updater.exe --zip <path> --target <dir> --exe <Game.exe> [--waitms 1000]"
            );
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

        Thread.Sleep(waitMs);
        for (int i = 0; i < 50; i++)
        {
            if (!IsProcessRunning(exeName))
                break;
            Thread.Sleep(200);
        }

        string staging = Path.Combine(
            Path.GetTempPath(),
            "pkmnquiz_update_" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(staging);

        try
        {
            ZipFile.ExtractToDirectory(zip, staging, true);

            string root = staging;
            var entries = Directory.GetFileSystemEntries(staging);
            var realEntries = entries
                .Where(e =>
                {
                    var name = Path.GetFileName(e);
                    return !name.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith(".", StringComparison.Ordinal);
                })
                .ToArray();

            if (realEntries.Length == 1 && Directory.Exists(realEntries[0]))
            {
                root = realEntries[0];
                Console.WriteLine("Detected single-root folder in zip. Using: " + root);
            }

            CopyAll(new DirectoryInfo(root), new DirectoryInfo(target));

            string exePath = Directory
                .GetFiles(target, exeName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(exePath))
            {
                Console.Error.WriteLine("Cannot find game exe in target: " + exeName);
                return 5;
            }

            var p = new Process
            {
                StartInfo =
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath)!,
                    UseShellExecute = true,
                },
            };
            p.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Update failed: " + ex);
            return 6;
        }
        finally
        {
            try
            {
                Directory.Delete(staging, true);
            }
            catch { }
            try
            {
                File.Delete(zip);
            }
            catch { }
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
                if (name == exeName)
                    return true;
            }
            catch { }
        }
        return false;
    }

    static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (var dir in source.GetDirectories())
        {
            var destDir = new DirectoryInfo(Path.Combine(target.FullName, dir.Name));
            if (!destDir.Exists)
                destDir.Create();
            CopyAll(dir, destDir);
        }

        foreach (var file in source.GetFiles())
        {
            if (string.Equals(file.Name, "Updater.exe", StringComparison.OrdinalIgnoreCase))
                continue;

            string dest = Path.Combine(target.FullName, file.Name);
            try
            {
                if (File.Exists(dest))
                {
                    var attrs = File.GetAttributes(dest);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(dest, attrs & ~FileAttributes.ReadOnly);
                }
                file.CopyTo(dest, true);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
                file.CopyTo(dest, true);
            }
        }
    }
}
