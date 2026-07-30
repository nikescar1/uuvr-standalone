using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.SceneManagement;

namespace Uuvr.VrCamera;

// Remembers which camera the user picked for VR, per scene.
//
// Automatic detection takes over every camera that looks usable, which is right most of the
// time and wrong in a way that's impossible to guess at when a game renders through several.
// Cycling through the cameras by hand is the escape hatch, and since the right answer differs
// between a game's menu and its gameplay scenes, the choice is stored per scene name.
//
// Serialized into one config string as "scene=camera" pairs separated by /, matching the
// other list settings. Camera or scene names containing / or = can't be stored this way;
// those games need the camera name filters instead.
public static class ForcedCameraMemory
{
    public const string AutomaticName = "";

    private static string? _parsedFrom;
    private static Dictionary<string, string> _byScene = new();

    public static string GetCurrentSceneName()
    {
        try
        {
            return SceneManager.GetActiveScene().name ?? "";
        }
        catch (Exception)
        {
            // Not worth failing over: an empty name just means every scene shares one choice.
            return "";
        }
    }

    // The camera name forced for the current scene, or null when detection is automatic.
    public static string? GetForcedCameraName()
    {
        var map = GetMap();
        return map.TryGetValue(GetCurrentSceneName(), out var cameraName) && cameraName.Length > 0
            ? cameraName
            : null;
    }

    public static void SetForcedCameraName(string? cameraName)
    {
        var map = GetMap();
        var scene = GetCurrentSceneName();

        if (string.IsNullOrEmpty(cameraName)) map.Remove(scene);
        else map[scene] = cameraName!;

        var serialized = new StringBuilder();
        foreach (var pair in map)
        {
            if (serialized.Length > 0) serialized.Append('/');
            serialized.Append(pair.Key).Append('=').Append(pair.Value);
        }

        var value = serialized.ToString();
        _parsedFrom = value;
        ModConfiguration.Instance.ForcedCameraByScene.Value = value;
    }

    private static Dictionary<string, string> GetMap()
    {
        var raw = ModConfiguration.Instance.ForcedCameraByScene.Value ?? "";
        if (raw == _parsedFrom) return _byScene;

        var map = new Dictionary<string, string>();
        foreach (var entry in raw.Split('/'))
        {
            var separator = entry.IndexOf('=');
            if (separator < 0) continue;

            var scene = entry.Substring(0, separator).Trim();
            var cameraName = entry.Substring(separator + 1).Trim();
            if (cameraName.Length > 0) map[scene] = cameraName;
        }

        _parsedFrom = raw;
        _byScene = map;
        return map;
    }
}
