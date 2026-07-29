using System;
using UnityEngine;

namespace Uuvr.VrCamera;

// Decides which game cameras UUVR is allowed to turn into VR cameras,
// based on the user-configurable name filters. Helps with games where the
// automatic camera detection picks up minimaps, portraits, etc.
public static class CameraFilter
{
    public static bool ShouldUseCamera(Camera camera)
    {
        string cameraName;
        try
        {
            cameraName = camera.name;
        }
        catch (Exception)
        {
            return false;
        }

        if (MatchesFilter(cameraName, ModConfiguration.Instance.CameraNameBlockList.Value)) return false;

        var allowList = ModConfiguration.Instance.CameraNameAllowList.Value;
        if (!IsFilterEmpty(allowList) && !MatchesFilter(cameraName, allowList)) return false;

        return true;
    }

    private static bool IsFilterEmpty(string filter)
    {
        return filter == null || filter.Trim().Length == 0;
    }

    private static bool MatchesFilter(string cameraName, string filter)
    {
        if (IsFilterEmpty(filter)) return false;

        foreach (var part in filter.Split('/'))
        {
            var trimmedPart = part.Trim();
            if (trimmedPart.Length == 0) continue;
            if (cameraName.IndexOf(trimmedPart, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }
}
