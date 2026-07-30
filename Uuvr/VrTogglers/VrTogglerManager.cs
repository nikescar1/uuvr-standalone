using System;
using BepInEx.Configuration;
using UnityEngine;

namespace Uuvr.VrTogglers;

public class VrTogglerManager
{
    private VrToggler _toggler;

    public bool IsVrEnabled => _toggler is { IsVrEnabled: true };

    public VrTogglerManager()
    {
        SetUpToggler();
        _toggler?.SetVrEnabled(true);
    }

    private void SetUpToggler()
    {
        if (_toggler != null)
        {
            _toggler.SetVrEnabled(false);
        }
        
#if MODERN
        // TODO: should never pick OpenXR on x86, since it no worky.
        switch(ModConfiguration.Instance.PreferredVrApi.Value)
        {
            case ModConfiguration.VrApi.OpenVr:
            {
                _toggler = new XrPluginOpenVrToggler();
                return;
            }
            case ModConfiguration.VrApi.OpenXr:
            {
                _toggler = new XrPluginOpenXrToggler();
                return;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
#else
        // The legacy build also gets used for modern IL2CPP games, where Unity's built-in VR path
        // no longer exists. Those games need XR plugin management, which we can only reach by reflection.
        XrCapabilityReport.Log();

        switch (ModConfiguration.Instance.StartupMethod.Value)
        {
            case ModConfiguration.VrStartupMethod.XrPluginManagement:
                UuvrTrace.Log("VR startup method forced to XR Plugin Management");
                _toggler = new ReflectionXrPluginToggler();
                return;
            case ModConfiguration.VrStartupMethod.Subsystems:
                UuvrTrace.Log("VR startup method forced to XR subsystems");
                _toggler = new SubsystemXrToggler();
                return;
            case ModConfiguration.VrStartupMethod.Legacy:
                UuvrTrace.Log("VR startup method forced to legacy built-in VR");
                _toggler = new LegacyOpenVrToggler();
                return;
        }

        // Auto: most capable option the game actually supports.
        if (ReflectionXrPluginToggler.IsSupported())
        {
            UuvrTrace.Log("this game has XR Plugin Management, using it to start VR");
            _toggler = new ReflectionXrPluginToggler();
        }
        else if (SubsystemXrToggler.IsSupported())
        {
            UuvrTrace.Log("no XR Plugin Management, but the XR subsystem API is available; driving that directly");
            _toggler = new SubsystemXrToggler();
        }
        else
        {
            UuvrTrace.Log("no modern XR support found, falling back to the legacy VR path (only works on Unity 2019 and older)");
            _toggler = new LegacyOpenVrToggler();
        }
#endif
    }

    public void ToggleVr()
    {
        _toggler.SetVrEnabled(!_toggler.IsVrEnabled);
    }
}
