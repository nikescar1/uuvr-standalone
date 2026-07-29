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
        if (ReflectionXrPluginToggler.IsSupported())
        {
            UuvrTrace.Log("this game has XR Plugin Management, using it to start VR");
            _toggler = new ReflectionXrPluginToggler();
        }
        else
        {
            UuvrTrace.Log("no XR Plugin Management found, using the legacy VR path");
            _toggler = new LegacyOpenVrToggler();
        }
#endif
    }

    public void ToggleVr()
    {
        _toggler.SetVrEnabled(!_toggler.IsVrEnabled);
    }
}
