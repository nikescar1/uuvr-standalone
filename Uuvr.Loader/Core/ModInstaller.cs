using System.Text.Json;

namespace Uuvr.Loader.Core;

public class InstallManifest
{
    public string UuvrVersion { get; set; } = "";
    public string Flavor { get; set; } = "";
    public string BepInExRuntime { get; set; } = "";
    public List<string> Files { get; set; } = new();
}

public class InstallResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

// Installs the UUVR payload (BepInEx runtime + the right mod flavor) into a game folder,
// tracking every file it creates so installs are cleanly reversible.
public class ModInstaller
{
    public const string ManifestFileName = ".uuvr-install.json";

    private readonly Action<string> _log;

    public ModInstaller(Action<string> log)
    {
        _log = log;
    }

    public static string LoaderDir =>
        Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;

    public static string PayloadDir => Path.Combine(LoaderDir, "Payload");

    public static string GetModFlavor(UnityBackend backend, UnityGeneration generation)
    {
        var backendName = backend == UnityBackend.Il2Cpp ? "il2cpp" : "mono";

        // Only a legacy build exists for IL2CPP right now; it's also what gets
        // used (with mixed results) on modern IL2CPP games.
        var generationName = backend == UnityBackend.Il2Cpp
            ? "legacy"
            : generation == UnityGeneration.Legacy ? "legacy" : "modern";

        return $"uuvr-{backendName}-{generationName}";
    }

    public static string GetRuntimeName(UnityBackend backend, GameArchitecture architecture)
    {
        var backendName = backend == UnityBackend.Il2Cpp ? "il2cpp" : "mono";
        var architectureName = architecture == GameArchitecture.X86 ? "x86" : "x64";
        return $"{backendName}-{architectureName}";
    }

    public static InstallManifest? ReadManifest(UnityGameInfo game)
    {
        try
        {
            var manifestPath = Path.Combine(game.GameDir, ManifestFileName);
            if (!File.Exists(manifestPath)) return null;
            return JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool HasForeignBepInEx(UnityGameInfo game)
    {
        if (ReadManifest(game) != null) return false;
        return Directory.Exists(Path.Combine(game.GameDir, "BepInEx", "core"));
    }

    private static string DoorstopConfigPath(UnityGameInfo game) => Path.Combine(game.GameDir, "doorstop_config.ini");

    // Whether the mod loader runs at all on game launch. Flipping this off gives a
    // "play flat tonight" switch without touching the install.
    public static bool? GetVrEnabledState(UnityGameInfo game)
    {
        try
        {
            var configPath = DoorstopConfigPath(game);
            if (!File.Exists(configPath)) return null;

            var match = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(configPath), @"(?m)^\s*enabled\s*=\s*(true|false)");
            if (!match.Success) return null;

            return match.Groups[1].Value == "true";
        }
        catch (Exception)
        {
            return null;
        }
    }

    public InstallResult SetVrEnabled(UnityGameInfo game, bool enabled)
    {
        try
        {
            var configPath = DoorstopConfigPath(game);
            if (!File.Exists(configPath))
            {
                return Fail("No doorstop_config.ini found; is UUVR (or BepInEx) installed for this game?");
            }

            var configText = File.ReadAllText(configPath);
            var newText = System.Text.RegularExpressions.Regex.Replace(
                configText, @"(?m)^(\s*enabled\s*=\s*)(true|false)", $"${{1}}{(enabled ? "true" : "false")}");
            File.WriteAllText(configPath, newText);

            if (!enabled)
            {
                // Legacy games get their global settings file patched at launch to enable VR devices.
                // Restore the backup so a flat launch is truly stock; the patcher re-patches
                // automatically on the next VR-enabled launch.
                RestoreGlobalSettingsBackups(game);
                _log("VR disabled. The game will now launch flat. Use 'Enable VR' to switch back.");
            }
            else
            {
                _log("VR enabled. Start SteamVR (or your OpenXR runtime) before launching the game.");
            }

            return new InstallResult { Success = true, Message = enabled ? "VR enabled." : "VR disabled (flat mode)." };
        }
        catch (Exception exception)
        {
            return Fail($"Failed to switch VR state: {exception.Message}");
        }
    }

    private void RestoreGlobalSettingsBackups(UnityGameInfo game)
    {
        foreach (var fileName in new[] { "globalgamemanagers", "mainData", "data.unity3d" })
        {
            var originalPath = Path.Combine(game.DataDir, fileName);
            var backupPath = originalPath + ".bak";

            try
            {
                if (!File.Exists(backupPath)) continue;
                File.Copy(backupPath, originalPath, true);
                File.Delete(backupPath);
                _log($"Restored original {fileName} from backup.");
            }
            catch (Exception exception)
            {
                _log($"Could not restore {fileName} backup: {exception.Message}");
            }
        }
    }

    public InstallResult Install(UnityGameInfo game, UnityGeneration? generationOverride = null)
    {
        try
        {
            if (game.Backend == UnityBackend.Unknown)
            {
                return Fail("Could not detect the game's scripting backend (Mono or IL2CPP), so UUVR can't be installed automatically.");
            }

            if (game.Architecture == GameArchitecture.Unknown)
            {
                return Fail("Could not detect whether the game is 32 or 64 bit, so UUVR can't be installed automatically.");
            }

            var generation = generationOverride ?? game.Generation;
            if (game.Backend == UnityBackend.Mono && generation == UnityGeneration.Unknown)
            {
                return Fail("Could not detect the Unity version. Pick 'Force legacy' (Unity 2019 or older) or 'Force modern' (Unity 2020+) and try again.");
            }

            var flavor = GetModFlavor(game.Backend, generation);
            var runtimeName = GetRuntimeName(game.Backend, game.Architecture);

            var modSourceDir = Path.Combine(PayloadDir, "Mods", flavor);
            var runtimeSourceDir = Path.Combine(PayloadDir, "BepInEx", runtimeName);

            if (!Directory.Exists(modSourceDir))
            {
                return Fail($"Payload folder is missing '{modSourceDir}'. Re-download the UUVR release zip and extract ALL of it, keeping the folder structure.");
            }

            // Remove any previous UUVR install first so updates never leave stale files behind.
            var previousManifest = ReadManifest(game);
            if (previousManifest != null)
            {
                _log($"Removing previous UUVR {previousManifest.UuvrVersion} install...");
                Uninstall(game);
            }

            var manifest = new InstallManifest
            {
                UuvrVersion = LoaderVersion.Value,
                Flavor = flavor,
                BepInExRuntime = runtimeName,
            };

            var adoptExistingBepInEx = HasForeignBepInEx(game);
            if (adoptExistingBepInEx)
            {
                _log("Existing BepInEx install detected: leaving it in place and only adding the UUVR mod to it.");
            }
            else
            {
                if (!Directory.Exists(runtimeSourceDir))
                {
                    return Fail($"Payload folder is missing '{runtimeSourceDir}'. Re-download the UUVR release zip and extract ALL of it, keeping the folder structure.");
                }

                _log($"Installing BepInEx runtime ({runtimeName})...");
                CopyTree(runtimeSourceDir, game.GameDir, game.GameDir, manifest.Files);
            }

            _log($"Installing UUVR ({flavor})...");
            var pluginsSourceDir = Path.Combine(modSourceDir, "plugins");
            var patchersSourceDir = Path.Combine(modSourceDir, "patchers");

            if (Directory.Exists(pluginsSourceDir))
            {
                CopyTree(pluginsSourceDir, Path.Combine(game.GameDir, "BepInEx", "plugins", "UUVR"), game.GameDir, manifest.Files);
            }

            if (Directory.Exists(patchersSourceDir))
            {
                CopyTree(patchersSourceDir, Path.Combine(game.GameDir, "BepInEx", "patchers", "UUVR"), game.GameDir, manifest.Files);
            }

            var manifestPath = Path.Combine(game.GameDir, ManifestFileName);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            _log($"Installed UUVR {LoaderVersion.Value} ({flavor}, {manifest.Files.Count} files).");
            if (game.Backend == UnityBackend.Il2Cpp)
            {
                _log("Note: the first launch of an IL2CPP game takes a few minutes while BepInEx generates interop assemblies.");
            }
            if (game.Backend == UnityBackend.Il2Cpp && generation == UnityGeneration.Modern)
            {
                _log("Note: this is a modern (Unity 2020+) IL2CPP game, which UUVR supports only experimentally.");
                _log("It will try to start VR through the game's own XR Plugin Management a few seconds after launch. Check BepInEx/plugins/UUVR/uuvr-trace.log to see how far it got.");
                _log("If the game crashes or hangs on startup, raise 'VR Start Delay' or set 'Start VR Automatically = false' in BepInEx/config/raicuparta.uuvr-legacy.cfg, then use the toggle VR key (F3) in-game.");
            }

            return new InstallResult { Success = true, Message = $"UUVR installed ({flavor})." };
        }
        catch (UnauthorizedAccessException exception)
        {
            return Fail($"No permission to write into the game folder ({exception.Message}). Try running the loader as administrator, or move the game out of a protected folder.");
        }
        catch (Exception exception)
        {
            return Fail($"Install failed: {exception.Message}");
        }
    }

    public InstallResult Uninstall(UnityGameInfo game)
    {
        try
        {
            var manifest = ReadManifest(game);
            if (manifest == null)
            {
                return Fail("No UUVR install manifest found for this game; nothing to uninstall.");
            }

            var deleted = 0;
            foreach (var relativePath in manifest.Files)
            {
                var fullPath = Path.GetFullPath(Path.Combine(game.GameDir, relativePath));

                // Never delete anything outside the game folder, no matter what the manifest says.
                if (!fullPath.StartsWith(Path.GetFullPath(game.GameDir), StringComparison.OrdinalIgnoreCase)) continue;

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    deleted++;
                }
            }

            PruneEmptyDirectories(Path.Combine(game.GameDir, "BepInEx"));
            PruneEmptyDirectories(Path.Combine(game.GameDir, "dotnet"));
            File.Delete(Path.Combine(game.GameDir, ManifestFileName));

            // Undo the VR devices patch that the legacy patcher applies to the game's global settings.
            RestoreGlobalSettingsBackups(game);

            _log($"Uninstalled UUVR ({deleted} files removed). Config files created by the game were left in place.");
            return new InstallResult { Success = true, Message = "UUVR uninstalled." };
        }
        catch (Exception exception)
        {
            return Fail($"Uninstall failed: {exception.Message}");
        }
    }

    private static void CopyTree(string sourceDir, string destinationDir, string gameDir, List<string> trackedFiles)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var sourcePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, sourcePath);
            var destinationPath = Path.Combine(destinationDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, true);

            // Manifest paths are stored relative to the game folder.
            trackedFiles.Add(Path.GetRelativePath(gameDir, destinationPath));
        }
    }

    private static void PruneEmptyDirectories(string rootDir)
    {
        if (!Directory.Exists(rootDir)) return;

        foreach (var dir in Directory.GetDirectories(rootDir))
        {
            PruneEmptyDirectories(dir);
        }

        if (Directory.GetFileSystemEntries(rootDir).Length == 0)
        {
            Directory.Delete(rootDir);
        }
    }

    private InstallResult Fail(string message)
    {
        _log(message);
        return new InstallResult { Success = false, Message = message };
    }
}

public static class LoaderVersion
{
    public const string Value = "0.5.7";
}
