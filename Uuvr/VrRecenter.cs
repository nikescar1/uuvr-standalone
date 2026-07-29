using System;
using UnityEngine;
#if MODERN && MONO
using System.Collections.Generic;
using UnityEngine.XR;
#endif

namespace Uuvr;

// Recenters the VR view so that the current headset pose becomes the neutral "forward" pose.
// Different Unity versions expose this in different places, so we try a few.
public static class VrRecenter
{
    public static void Recenter()
    {
#if MODERN && MONO
        if (RecenterXrSubsystems()) return;
#endif
        if (RecenterInputTracking()) return;

        Debug.LogWarning("UUVR: found no way to recenter the VR view in this game.");
    }

#if MODERN && MONO
    private static bool RecenterXrSubsystems()
    {
        try
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetInstances(subsystems);

            var recentered = false;
            foreach (var subsystem in subsystems)
            {
                if (subsystem.TryRecenter()) recentered = true;
            }

            return recentered;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"UUVR: XR subsystem recenter failed: {exception.Message}");
            return false;
        }
    }
#endif

    private static bool RecenterInputTracking()
    {
        try
        {
            var inputTrackingType =
                Type.GetType("UnityEngine.XR.InputTracking, UnityEngine.XRModule") ??
                Type.GetType("UnityEngine.XR.InputTracking, UnityEngine.VRModule") ??
                Type.GetType("UnityEngine.VR.InputTracking, UnityEngine.VRModule") ??
                Type.GetType("UnityEngine.VR.InputTracking, UnityEngine");

            var recenterMethod = inputTrackingType?.GetMethod("Recenter");
            if (recenterMethod == null) return false;

            recenterMethod.Invoke(null, null);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"UUVR: InputTracking recenter failed: {exception.Message}");
            return false;
        }
    }
}
