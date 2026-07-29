using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Uuvr.Loader.Core;

public class ScannedGame
{
    public string ExePath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Source { get; init; } = "";
}

// Finds installed Unity games by walking Steam libraries.
// Games from other stores can always be added manually.
public static class GameScanner
{
    private static readonly Regex VdfPathRegex = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
    private static readonly Regex AcfNameRegex = new("\"name\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
    private static readonly Regex AcfInstallDirRegex = new("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

    // Executables that live next to games but are never the game itself.
    private static readonly string[] IgnoredExeNames =
    {
        "UnityCrashHandler", "UnityCrashHandler32", "UnityCrashHandler64", "vcredist", "vc_redist",
        "dxsetup", "dotnetfx", "oalinst", "uninstall", "unins000", "crashpad_handler",
    };

    public static List<ScannedGame> ScanSteamGames()
    {
        var games = new List<ScannedGame>();

        foreach (var libraryPath in FindSteamLibraryPaths())
        {
            var steamAppsDir = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamAppsDir)) continue;

            string[] manifests;
            try
            {
                manifests = Directory.GetFiles(steamAppsDir, "appmanifest_*.acf");
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var manifestPath in manifests)
            {
                try
                {
                    var manifestText = File.ReadAllText(manifestPath);
                    var installDirMatch = AcfInstallDirRegex.Match(manifestText);
                    if (!installDirMatch.Success) continue;

                    var gameDir = Path.Combine(steamAppsDir, "common", installDirMatch.Groups[1].Value);
                    if (!Directory.Exists(gameDir)) continue;

                    var exePath = FindUnityExe(gameDir);
                    if (exePath == null) continue;

                    var nameMatch = AcfNameRegex.Match(manifestText);
                    games.Add(new ScannedGame
                    {
                        ExePath = exePath,
                        DisplayName = nameMatch.Success ? nameMatch.Groups[1].Value : Path.GetFileNameWithoutExtension(exePath),
                        Source = "Steam",
                    });
                }
                catch (Exception)
                {
                    // Broken manifest or inaccessible folder; skip it.
                }
            }
        }

        return games
            .GroupBy(game => game.ExePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> FindSteamLibraryPaths()
    {
        var libraries = new List<string>();

        var steamPath =
            ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath") ??
            ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") ??
            ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        if (steamPath == null) return libraries;

        steamPath = steamPath.Replace('/', Path.DirectorySeparatorChar);
        libraries.Add(steamPath);

        var libraryFoldersVdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(libraryFoldersVdf))
            {
                var vdfText = File.ReadAllText(libraryFoldersVdf);
                foreach (Match match in VdfPathRegex.Matches(vdfText))
                {
                    var libraryPath = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(libraryPath) && !libraries.Contains(libraryPath, StringComparer.OrdinalIgnoreCase))
                    {
                        libraries.Add(libraryPath);
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        return libraries;
    }

    private static string? ReadRegistryString(RegistryKey root, string subKeyPath, string valueName)
    {
        try
        {
            using var subKey = root.OpenSubKey(subKeyPath);
            return subKey?.GetValue(valueName) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? FindUnityExe(string gameDir)
    {
        try
        {
            // Check the game root first, then one level of subfolders (e.g. "Binaries", "x64").
            var candidateDirs = new List<string> { gameDir };
            candidateDirs.AddRange(Directory.GetDirectories(gameDir));

            foreach (var dir in candidateDirs)
            {
                foreach (var exePath in Directory.GetFiles(dir, "*.exe"))
                {
                    var exeName = Path.GetFileNameWithoutExtension(exePath);
                    if (IgnoredExeNames.Any(ignored => exeName.StartsWith(ignored, StringComparison.OrdinalIgnoreCase))) continue;
                    if (!Directory.Exists(Path.Combine(dir, exeName + "_Data"))) continue;

                    return exePath;
                }
            }
        }
        catch (Exception)
        {
        }

        return null;
    }
}
