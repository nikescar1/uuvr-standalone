using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Uuvr.Loader.Core;

public enum UnityBackend
{
    Unknown,
    Mono,
    Il2Cpp,
}

public enum UnityGeneration
{
    Unknown,
    // Unity 2019 and earlier.
    Legacy,
    // Unity 2020 and later.
    Modern,
}

public enum GameArchitecture
{
    Unknown,
    X86,
    X64,
}

// Everything UUVR needs to know about a Unity game to pick the right
// mod flavor and BepInEx runtime, detected from the game files alone.
public class UnityGameInfo
{
    public string ExePath { get; private init; } = "";
    public string GameDir { get; private init; } = "";
    public string DataDir { get; private init; } = "";
    public string Name { get; private init; } = "";
    public string UnityVersion { get; private set; } = "";
    public UnityBackend Backend { get; private set; }
    public UnityGeneration Generation { get; private set; }
    public GameArchitecture Architecture { get; private set; }

    private static readonly Regex UnityVersionRegex = new(@"(\d{1,4})\.(\d{1,2})\.(\d{1,3})[abfpx]\d+", RegexOptions.Compiled);

    public static bool LooksLikeUnityGame(string exePath)
    {
        if (!File.Exists(exePath)) return false;
        var gameDir = Path.GetDirectoryName(exePath);
        if (gameDir == null) return false;

        var dataDir = GetDataDir(exePath);
        if (dataDir != null) return true;

        return File.Exists(Path.Combine(gameDir, "UnityPlayer.dll"));
    }

    public static UnityGameInfo? Detect(string exePath)
    {
        if (!File.Exists(exePath)) return null;

        var gameDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
        if (gameDir == null) return null;

        var dataDir = GetDataDir(exePath);
        if (dataDir == null) return null;

        var info = new UnityGameInfo
        {
            ExePath = Path.GetFullPath(exePath),
            GameDir = gameDir,
            DataDir = dataDir,
            Name = Path.GetFileNameWithoutExtension(exePath),
        };

        info.Backend = DetectBackend(gameDir, dataDir);
        info.Architecture = DetectArchitecture(exePath);
        info.UnityVersion = DetectUnityVersion(gameDir, dataDir) ?? "";
        info.Generation = GenerationFromVersion(info.UnityVersion);

        return info;
    }

    private static string? GetDataDir(string exePath)
    {
        var gameDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
        if (gameDir == null) return null;

        var dataDir = Path.Combine(gameDir, Path.GetFileNameWithoutExtension(exePath) + "_Data");
        if (Directory.Exists(dataDir)) return dataDir;

        // Some games have a single Data folder not named after the exe.
        var genericDataDir = Path.Combine(gameDir, "Data");
        if (Directory.Exists(genericDataDir) && File.Exists(Path.Combine(genericDataDir, "globalgamemanagers")))
        {
            return genericDataDir;
        }

        return null;
    }

    private static UnityBackend DetectBackend(string gameDir, string dataDir)
    {
        if (File.Exists(Path.Combine(gameDir, "GameAssembly.dll")) ||
            Directory.Exists(Path.Combine(dataDir, "il2cpp_data")))
        {
            return UnityBackend.Il2Cpp;
        }

        if (Directory.Exists(Path.Combine(dataDir, "Managed")))
        {
            return UnityBackend.Mono;
        }

        return UnityBackend.Unknown;
    }

    private static GameArchitecture DetectArchitecture(string exePath)
    {
        try
        {
            using var stream = File.OpenRead(exePath);
            using var reader = new BinaryReader(stream);

            stream.Seek(0x3C, SeekOrigin.Begin);
            var peHeaderOffset = reader.ReadInt32();
            if (peHeaderOffset <= 0 || peHeaderOffset > stream.Length - 6) return GameArchitecture.Unknown;

            stream.Seek(peHeaderOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550) return GameArchitecture.Unknown; // "PE\0\0"

            return reader.ReadUInt16() switch
            {
                0x014C => GameArchitecture.X86,
                0x8664 => GameArchitecture.X64,
                _ => GameArchitecture.Unknown,
            };
        }
        catch (Exception)
        {
            return GameArchitecture.Unknown;
        }
    }

    private static string? DetectUnityVersion(string gameDir, string dataDir)
    {
        // File version info of the engine binaries is the cheapest reliable source.
        foreach (var candidate in new[] { Path.Combine(gameDir, "UnityPlayer.dll") })
        {
            var version = VersionFromFileInfo(candidate);
            if (version != null) return version;
        }

        // Fall back to scanning the headers of the serialized asset files,
        // which start with (or contain very early) the engine version string.
        foreach (var fileName in new[] { "globalgamemanagers", "mainData", "data.unity3d", "level0", "resources.assets" })
        {
            var version = VersionFromFileHeader(Path.Combine(dataDir, fileName));
            if (version != null) return version;
        }

        return null;
    }

    private static string? VersionFromFileInfo(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(filePath);
            foreach (var candidate in new[] { fileVersionInfo.ProductVersion, fileVersionInfo.FileVersion })
            {
                if (candidate == null) continue;
                var match = UnityVersionRegex.Match(candidate);
                if (match.Success) return match.Value;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static string? VersionFromFileHeader(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            using var stream = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(stream.Length, 65536)];
            var read = stream.Read(buffer, 0, buffer.Length);

            var text = Encoding.ASCII.GetString(buffer, 0, read);
            var match = UnityVersionRegex.Match(text);
            return match.Success ? match.Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static UnityGeneration GenerationFromVersion(string version)
    {
        var match = Regex.Match(version, @"^(\d{1,4})\.");
        if (!match.Success) return UnityGeneration.Unknown;

        var major = int.Parse(match.Groups[1].Value);

        // Unity 6 uses 6000.x version numbers.
        if (major >= 2020) return UnityGeneration.Modern;
        if (major >= 1000) return UnityGeneration.Modern;
        return UnityGeneration.Legacy;
    }
}
