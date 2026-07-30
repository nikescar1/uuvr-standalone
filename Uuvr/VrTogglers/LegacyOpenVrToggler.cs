using System;
using System.Reflection;
using UnityEngine;

namespace Uuvr.VrTogglers;

public class LegacyOpenVrToggler: VrToggler
{
    private static Type? _xrSettingsType;
    private static PropertyInfo? _xrEnabledProperty;

    protected override bool SetUp()
    {
        _xrSettingsType =
            Type.GetType("UnityEngine.XR.XRSettings, UnityEngine.XRModule") ??
            Type.GetType("UnityEngine.XR.XRSettings, UnityEngine.VRModule") ??
            Type.GetType("UnityEngine.VR.VRSettings, UnityEngine");

        _xrEnabledProperty = _xrSettingsType?.GetProperty("enabled");

        if (_xrEnabledProperty == null)
        {
            Debug.LogError("UUVR: failed to find XRSettings.enabled, can't toggle VR in this game.");
            return false;
        }

        return true;
    }

    protected override bool EnableVr()
    {
        if (!SetVrEnabledProperty(true)) return false;

        // On Unity 2020 and newer this property can be set without anything happening,
        // because built-in VR was removed in favour of XR plugin management. Read the
        // result back rather than reporting success we haven't verified.
        var enabled = _xrEnabledProperty?.GetValue(null, null) as bool?;
        var deviceActive = _xrSettingsType?.GetProperty("isDeviceActive")?.GetValue(null, null) as bool?;

        if (enabled == true || deviceActive == true)
        {
            UuvrTrace.Log($"legacy VR enabled (XRSettings.enabled={enabled}, isDeviceActive={deviceActive})");
            return true;
        }

        UuvrTrace.LogError(
            $"legacy VR did not turn on (XRSettings.enabled={enabled}, isDeviceActive={deviceActive}). " +
            "This is expected on Unity 2020 and newer, where built-in VR was removed and VR can only be started through XR plugin management.");
        return false;
    }

    protected override bool DisableVr()
    {
        return SetVrEnabledProperty(false);
    }

    private static bool SetVrEnabledProperty(bool enabled)
    {
        if (_xrEnabledProperty == null) return false;

        try
        {
            _xrEnabledProperty.SetValue(null, enabled, null);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"UUVR: failed to set XRSettings.enabled to {enabled}: {exception.Message}");
            return false;
        }
    }
}
