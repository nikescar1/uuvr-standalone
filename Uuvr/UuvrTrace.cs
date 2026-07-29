using System;
using System.IO;
using UnityEngine;

namespace Uuvr;

// Writes breadcrumbs to BepInEx/uuvr-trace.log, one file operation per line so the
// text is on disk before the next line runs.
//
// Starting VR can take the game down at the native level, which produces no managed
// exception and leaves BepInEx's own log buffered and truncated. This file survives that,
// so the last line written says exactly which step killed the game.
public static class UuvrTrace
{
    private static string? _path;
    private static bool _disabled;

    public static void Log(string message)
    {
        Debug.Log($"UUVR: {message}");
        WriteLine(message);
    }

    public static void LogWarning(string message)
    {
        Debug.LogWarning($"UUVR: {message}");
        WriteLine($"WARNING {message}");
    }

    public static void LogError(string message)
    {
        Debug.LogError($"UUVR: {message}");
        WriteLine($"ERROR {message}");
    }

    private static void WriteLine(string message)
    {
        if (_disabled) return;

        try
        {
            if (_path == null)
            {
                var folder = UuvrPlugin.ModFolderPath;
                if (string.IsNullOrEmpty(folder)) return;

                _path = Path.Combine(folder, "uuvr-trace.log");

                // Start fresh every launch, so the file always describes the current run.
                File.WriteAllText(_path, $"UUVR {UuvrPlugin.PluginVersion} trace\n");
            }

            File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff}  {message}\n");
        }
        catch (Exception)
        {
            // Never let tracing itself break the mod.
            _disabled = true;
        }
    }
}
