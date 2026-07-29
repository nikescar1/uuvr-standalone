using System;
using System.Reflection;
using UnityEngine;

namespace Uuvr.VrTogglers;

// Drives Unity's XR Plugin Management entirely through reflection.
//
// Unity 2020+ removed the built-in VR path that LegacyOpenVrToggler relies on, so modern games
// need XR plugin management instead. The modern builds of UUVR reference those types directly,
// but there's no modern IL2CPP build, so modern IL2CPP games fall back to the legacy build and
// would otherwise never start VR. Reflection lets that build talk to whatever XR management the
// game itself ships.
//
// Every step logs what it found, so a game that fails here says exactly where it stopped.
public class ReflectionXrPluginToggler : VrToggler
{
    private object? _managerSettings;
    private MethodInfo? _startSubsystems;
    private MethodInfo? _stopSubsystems;
    private MethodInfo? _deinitializeLoader;

    public static bool IsSupported()
    {
        return FindType("UnityEngine.XR.Management.XRManagerSettings") != null;
    }

    protected override bool SetUp()
    {
        var managerType = FindType("UnityEngine.XR.Management.XRManagerSettings");
        if (managerType == null)
        {
            Debug.LogWarning("UUVR: this game doesn't include Unity's XR Plugin Management, so VR can't be started this way.");
            return false;
        }

        _managerSettings = GetGameManagerSettings(managerType) ?? CreateManagerSettings(managerType);
        if (_managerSettings == null)
        {
            Debug.LogError("UUVR: failed to obtain an XRManagerSettings instance.");
            return false;
        }

        _startSubsystems = managerType.GetMethod("StartSubsystems");
        _stopSubsystems = managerType.GetMethod("StopSubsystems");
        _deinitializeLoader = managerType.GetMethod("DeinitializeLoader");

        var initializeLoaderSync = managerType.GetMethod("InitializeLoaderSync");
        if (initializeLoaderSync == null)
        {
            Debug.LogError("UUVR: XRManagerSettings.InitializeLoaderSync not found.");
            return false;
        }

        Debug.Log("UUVR: initializing XR loader...");
        initializeLoaderSync.Invoke(_managerSettings, null);

        var activeLoader = managerType.GetProperty("activeLoader")?.GetValue(_managerSettings, null);
        if (activeLoader == null)
        {
            Debug.LogError(
                "UUVR: XR loader failed to initialize. Make sure SteamVR (or your OpenXR runtime) is running before starting the game.");
            return false;
        }

        Debug.Log($"UUVR: XR loader initialized ({activeLoader.GetType().Name}).");
        return true;
    }

    // Preferred path: the game already ships a configured XRGeneralSettings asset,
    // which knows which loaders were built into it.
    private static object? GetGameManagerSettings(Type managerType)
    {
        try
        {
            var generalSettingsType = FindType("UnityEngine.XR.Management.XRGeneralSettings");
            var instance = generalSettingsType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null, null);
            if (instance == null) return null;

            var manager = generalSettingsType?.GetProperty("Manager")?.GetValue(instance, null);
            if (manager == null) return null;

            if (CountLoaders(managerType, manager) == 0)
            {
                Debug.Log("UUVR: the game's XR settings have no loaders configured, setting up our own.");
                return null;
            }

            Debug.Log("UUVR: using the XR settings that the game already ships.");
            return manager;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"UUVR: couldn't read the game's XR settings ({exception.Message}), setting up our own.");
            return null;
        }
    }

    // Fallback: build our own manager and register whichever XR loader the game has available.
    private static object? CreateManagerSettings(Type managerType)
    {
        try
        {
            var manager = CreateScriptableObject(managerType);
            if (manager == null) return null;

            var loader = CreateLoader();
            if (loader == null)
            {
                Debug.LogError(
                    "UUVR: no usable XR loader found in this game (looked for OpenXR and OpenVR). This game most likely can't run modern VR through UUVR yet.");
                return null;
            }

            if (!AddLoader(managerType, manager, loader))
            {
                Debug.LogError("UUVR: failed to register the XR loader with XRManagerSettings.");
                return null;
            }

            return manager;
        }
        catch (Exception exception)
        {
            Debug.LogError($"UUVR: failed to create XR manager settings: {exception}");
            return null;
        }
    }

    private static object? CreateLoader()
    {
        // Preference order matches the modern builds: OpenXR first, then OpenVR.
        string[] loaderTypeNames =
        {
            "UnityEngine.XR.OpenXR.OpenXRLoader",
            "Unity.XR.OpenVR.OpenVRLoader",
            "Unity.XR.OpenVR.OpenVRLoaderXR",
        };

        foreach (var loaderTypeName in loaderTypeNames)
        {
            var loaderType = FindType(loaderTypeName);
            if (loaderType == null) continue;

            var loader = CreateScriptableObject(loaderType);
            if (loader == null) continue;

            Debug.Log($"UUVR: using XR loader {loaderTypeName}.");
            return loader;
        }

        return null;
    }

    private static int CountLoaders(Type managerType, object manager)
    {
        var loaders = GetLoaderList(managerType, manager);
        if (loaders == null) return 0;

        var count = loaders.GetType().GetProperty("Count")?.GetValue(loaders, null);
        return count is int intCount ? intCount : 0;
    }

    private static object? GetLoaderList(Type managerType, object manager)
    {
        // "loaders" is deprecated in newer versions but still present, and it's the only
        // way to read the list; activeLoaders is read-only in some versions.
        return managerType.GetProperty("loaders")?.GetValue(manager, null) ??
               managerType.GetProperty("activeLoaders")?.GetValue(manager, null);
    }

    private static bool AddLoader(Type managerType, object manager, object loader)
    {
        // Newer XR Management prefers TryAddLoader over mutating the deprecated list.
        var tryAddLoader = managerType.GetMethod("TryAddLoader");
        if (tryAddLoader != null)
        {
            try
            {
                var parameters = tryAddLoader.GetParameters().Length == 2
                    ? new[] { loader, (object)(-1) }
                    : new[] { loader };
                if (tryAddLoader.Invoke(manager, parameters) is true) return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"UUVR: TryAddLoader failed ({exception.Message}), falling back to the loaders list.");
            }
        }

        var loaders = GetLoaderList(managerType, manager);
        var addMethod = loaders?.GetType().GetMethod("Add");
        if (loaders == null || addMethod == null) return false;

        addMethod.Invoke(loaders, new[] { loader });
        return true;
    }

    private static object? CreateScriptableObject(Type type)
    {
#if CPP
        return ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.From(type));
#else
        return ScriptableObject.CreateInstance(type);
#endif
    }

    // Searches every loaded assembly, since the XR assemblies are named differently
    // depending on Unity version and scripting backend.
    private static Type? FindType(string typeName)
    {
        foreach (var assemblySuffix in new[]
                 {
                     ", Unity.XR.Management", ", Unity.XR.OpenXR", ", Unity.XR.OpenVR", "",
                 })
        {
            var type = Type.GetType(typeName + assemblySuffix);
            if (type != null) return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(typeName);
                if (type != null) return type;
            }
            catch (Exception)
            {
                // Some assemblies throw on reflection; just keep looking.
            }
        }

        return null;
    }

    protected override bool EnableVr()
    {
        if (_managerSettings == null) return false;

        try
        {
            _startSubsystems?.Invoke(_managerSettings, null);
            Debug.Log("UUVR: XR subsystems started.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"UUVR: failed to start XR subsystems: {exception}");
            return false;
        }
    }

    protected override bool DisableVr()
    {
        if (_managerSettings == null) return false;

        try
        {
            _stopSubsystems?.Invoke(_managerSettings, null);
            _deinitializeLoader?.Invoke(_managerSettings, null);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"UUVR: failed to stop XR subsystems: {exception}");
            return false;
        }
    }
}
