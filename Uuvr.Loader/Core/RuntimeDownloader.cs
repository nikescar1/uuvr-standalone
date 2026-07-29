using System.IO.Compression;

namespace Uuvr.Loader.Core;

// Fetches BepInEx runtimes from their official GitHub releases when the Payload folder
// doesn't already bundle them (release zips of UUVR come with everything included,
// so this mostly matters for source builds).
public static class RuntimeDownloader
{
    private class RuntimeSource
    {
        public string Name = "";
        public string Url = "";
    }

    private static readonly RuntimeSource[] Sources =
    {
        new() { Name = "mono-x64", Url = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x64_5.4.23.4.zip" },
        new() { Name = "mono-x86", Url = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x86_5.4.23.4.zip" },
        new() { Name = "il2cpp-x64", Url = "https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.2/BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip" },
        new() { Name = "il2cpp-x86", Url = "https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.2/BepInEx-Unity.IL2CPP-win-x86-6.0.0-pre.2.zip" },
    };

    public static bool IsRuntimeAvailable(string runtimeName)
    {
        return Directory.Exists(Path.Combine(ModInstaller.PayloadDir, "BepInEx", runtimeName));
    }

    public static async Task<bool> EnsureRuntimeAsync(string runtimeName, Action<string> log)
    {
        if (IsRuntimeAvailable(runtimeName)) return true;

        var source = Sources.FirstOrDefault(candidate => candidate.Name == runtimeName);
        if (source == null)
        {
            log($"Unknown BepInEx runtime '{runtimeName}'.");
            return false;
        }

        var targetDir = Path.Combine(ModInstaller.PayloadDir, "BepInEx", runtimeName);

        try
        {
            log($"Downloading BepInEx runtime ({runtimeName})...");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UuvrLoader");
            var zipBytes = await httpClient.GetByteArrayAsync(source.Url);

            var tempZipPath = Path.Combine(Path.GetTempPath(), $"uuvr-{runtimeName}.zip");
            await File.WriteAllBytesAsync(tempZipPath, zipBytes);

            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(tempZipPath, targetDir, true);
            File.Delete(tempZipPath);

            log($"BepInEx runtime ready ({runtimeName}).");
            return true;
        }
        catch (Exception exception)
        {
            log($"Failed to download BepInEx runtime: {exception.Message}");
            try
            {
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
            }
            catch (Exception)
            {
            }
            return false;
        }
    }
}
