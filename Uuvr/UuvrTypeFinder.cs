using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Uuvr;

// Finds types that may live in assemblies this mod can't reference at compile time.
//
// Under IL2CPP, BepInEx generates an interop assembly per game assembly into BepInEx/interop
// and loads them on demand, so a type can be perfectly available while its assembly isn't in
// the AppDomain yet. Plain Type.GetType then finds nothing. This probes the interop folder as
// a last resort before giving up.
public static class UuvrTypeFinder
{
    private static readonly Dictionary<string, Type?> Cache = new();
    private static string[]? _interopAssemblyPaths;

    // Assembly names worth loading on spec when looking for XR types.
    private static readonly string[] XrAssemblyNames =
    {
        "Unity.XR.Management",
        "Unity.XR.OpenXR",
        "Unity.XR.OpenVR",
        "UnityEngine.XRModule",
        "UnityEngine.SubsystemsModule",
        "UnityEngine.VRModule",
    };

    public static Type? FindType(string typeName)
    {
        if (Cache.TryGetValue(typeName, out var cached)) return cached;

        var type = FindTypeUncached(typeName);
        Cache[typeName] = type;
        return type;
    }

    private static Type? FindTypeUncached(string typeName)
    {
        foreach (var assemblyName in XrAssemblyNames)
        {
            var type = Type.GetType($"{typeName}, {assemblyName}");
            if (type != null) return type;
        }

        var plainType = Type.GetType(typeName);
        if (plainType != null) return plainType;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(typeName);
                if (type != null) return type;
            }
            catch (Exception)
            {
                // Some assemblies throw on reflection; keep looking.
            }
        }

        return FindTypeInInteropAssemblies(typeName);
    }

    private static Type? FindTypeInInteropAssemblies(string typeName)
    {
        foreach (var assemblyPath in GetInteropAssemblyPaths())
        {
            var fileName = Path.GetFileNameWithoutExtension(assemblyPath);

            // Only load assemblies we have a reason to want; loading everything
            // would be slow and could have side effects.
            var isInteresting = false;
            foreach (var candidate in XrAssemblyNames)
            {
                if (string.Equals(fileName, candidate, StringComparison.OrdinalIgnoreCase)) isInteresting = true;
            }
            if (!isInteresting) continue;

            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    UuvrTrace.Log($"found {typeName} by loading {fileName} from the interop folder");
                    return type;
                }
            }
            catch (Exception exception)
            {
                UuvrTrace.LogWarning($"couldn't load interop assembly {fileName}: {exception.Message}");
            }
        }

        return null;
    }

    public static string[] GetInteropAssemblyPaths()
    {
        if (_interopAssemblyPaths != null) return _interopAssemblyPaths;

        _interopAssemblyPaths = new string[0];

        foreach (var directory in GetCandidateInteropDirectories())
        {
            try
            {
                if (!Directory.Exists(directory)) continue;
                _interopAssemblyPaths = Directory.GetFiles(directory, "*.dll");
                if (_interopAssemblyPaths.Length > 0) return _interopAssemblyPaths;
            }
            catch (Exception)
            {
            }
        }

        return _interopAssemblyPaths;
    }

    private static IEnumerable<string> GetCandidateInteropDirectories()
    {
        var modFolder = UuvrPlugin.ModFolderPath;
        if (string.IsNullOrEmpty(modFolder)) yield break;

        // ModFolderPath is BepInEx/plugins/UUVR, so BepInEx is two levels up.
        string? bepInExFolder = null;
        try
        {
            bepInExFolder = Path.GetFullPath(Path.Combine(Path.Combine(modFolder, ".."), ".."));
        }
        catch (Exception)
        {
        }

        if (bepInExFolder == null) yield break;

        yield return Path.Combine(bepInExFolder, "interop");
        yield return Path.Combine(bepInExFolder, "unhollowed");
    }
}
