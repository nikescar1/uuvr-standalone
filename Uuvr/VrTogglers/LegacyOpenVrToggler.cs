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
        return SetVrEnabledProperty(true);
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
