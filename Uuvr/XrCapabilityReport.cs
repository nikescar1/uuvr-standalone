using System;
using System.IO;
using System.Text;

namespace Uuvr;

// Writes a one-time summary of the VR machinery this game actually contains.
//
// Modern Unity can only start VR through managed XR code, and a game built without the XR
// packages simply doesn't have any. When UUVR can't start VR, this is the difference between
// "something is broken" and "this game has nothing to drive", so it belongs in the log
// rather than in a debugging session.
public static class XrCapabilityReport
{
    private static bool _logged;

    // Types that each represent a possible route to starting VR, or a useful hint.
    private static readonly string[] TypesToProbe =
    {
        // XR Plugin Management: what ReflectionXrPluginToggler drives.
        "UnityEngine.XR.Management.XRManagerSettings",
        "UnityEngine.XR.Management.XRGeneralSettings",
        // Loaders that XR Plugin Management would start.
        "UnityEngine.XR.OpenXR.OpenXRLoader",
        "Unity.XR.OpenVR.OpenVRLoader",
        // The lower-level subsystem API, which lives in the engine itself rather than
        // in a package, and may be usable when the packages are absent.
        "UnityEngine.SubsystemManager",
        "UnityEngine.XR.XRDisplaySubsystem",
        "UnityEngine.XR.XRDisplaySubsystemDescriptor",
        "UnityEngine.XR.XRInputSubsystem",
        // Legacy built-in VR, which only does anything on Unity 2019 and older.
        "UnityEngine.XR.XRSettings",
        "UnityEngine.XR.InputTracking",
    };

    public static void Log()
    {
        if (_logged) return;
        _logged = true;

        try
        {
            UuvrTrace.Log("--- VR capability report ---");

            foreach (var typeName in TypesToProbe)
            {
                var type = UuvrTypeFinder.FindType(typeName);
                UuvrTrace.Log($"  {(type != null ? "present" : "MISSING")}  {typeName}");
            }

            LogXrSettings();
            LogInteropAssemblies();

            UuvrTrace.Log("--- end of capability report ---");
        }
        catch (Exception exception)
        {
            UuvrTrace.LogWarning($"capability report failed: {exception.Message}");
        }
    }

    private static void LogXrSettings()
    {
        try
        {
            var xrSettingsType = UuvrTypeFinder.FindType("UnityEngine.XR.XRSettings");
            if (xrSettingsType == null) return;

            var enabled = xrSettingsType.GetProperty("enabled")?.GetValue(null, null);
            var deviceActive = xrSettingsType.GetProperty("isDeviceActive")?.GetValue(null, null);
            var loadedDevice = xrSettingsType.GetProperty("loadedDeviceName")?.GetValue(null, null);
            UuvrTrace.Log($"  XRSettings: enabled={enabled}, isDeviceActive={deviceActive}, loadedDeviceName='{loadedDevice}'");

            if (xrSettingsType.GetProperty("supportedDevices")?.GetValue(null, null) is string[] supportedDevices)
            {
                UuvrTrace.Log($"  XRSettings.supportedDevices: [{string.Join(", ", supportedDevices)}]");
            }
        }
        catch (Exception exception)
        {
            UuvrTrace.Log($"  XRSettings: couldn't read ({exception.Message})");
        }
    }

    // Under IL2CPP these file names are the definitive list of what managed code the game
    // contains, whether or not the assemblies have been loaded yet.
    private static void LogInteropAssemblies()
    {
        var assemblyPaths = UuvrTypeFinder.GetInteropAssemblyPaths();
        if (assemblyPaths.Length == 0)
        {
            UuvrTrace.Log("  no interop assembly folder found (expected for Mono games)");
            return;
        }

        UuvrTrace.Log($"  interop assemblies: {assemblyPaths.Length} total");

        var matches = new StringBuilder();
        foreach (var assemblyPath in assemblyPaths)
        {
            var fileName = Path.GetFileNameWithoutExtension(assemblyPath);
            if (fileName.IndexOf("XR", StringComparison.OrdinalIgnoreCase) < 0 &&
                fileName.IndexOf("VR", StringComparison.OrdinalIgnoreCase) < 0 &&
                fileName.IndexOf("Subsystem", StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (matches.Length > 0) matches.Append(", ");
            matches.Append(fileName);
        }

        UuvrTrace.Log(matches.Length > 0
            ? $"  XR/VR related interop assemblies: {matches}"
            : "  no XR/VR related interop assemblies at all — this game contains no managed XR code");
    }
}
