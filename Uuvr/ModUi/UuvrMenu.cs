using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Uuvr.ModUi;

// In-game settings menu, inspired by UEVR's overlay UI.
// Lets you tweak every UUVR setting live without leaving the game or editing config files.
// Rendered with IMGUI so it also shows up inside the headset when the VR UI is in Mirror mode.
public class UuvrMenu : UuvrBehaviour
{
#if CPP
    public UuvrMenu(IntPtr pointer) : base(pointer)
    {
    }
#endif

    public static bool IsOpen { get; private set; }

    // Enum value rows get unwieldy beyond this count; larger enums use a cycle widget instead.
    private const int MaxEnumButtons = 5;

    private Vector2 _scrollPosition;
    private string _activeSection = "";
    private readonly Dictionary<string, string> _textBuffers = new();
    private bool _guiFailed;
    private CursorLockMode _previousCursorLockState;
    private bool _previousCursorVisible;
    private bool _cursorStateSaved;

    public void ToggleOpen()
    {
        if (IsOpen) Close();
        else Open();
    }

    private void Open()
    {
        IsOpen = true;
        TrySaveCursorState();
    }

    private void Close()
    {
        IsOpen = false;
        _textBuffers.Clear();
        TryRestoreCursorState();
    }

    private void Update()
    {
        // Games love re-locking the cursor every frame, so we have to keep freeing it.
        if (IsOpen) TryForceCursorVisible();
    }

    private void OnGUI()
    {
        if (!IsOpen || _guiFailed) return;

        if (!GuiBridge.IsAvailable)
        {
            _guiFailed = true;
            ReportMenuUnavailable("this game has no usable IMGUI");
            return;
        }

        try
        {
            DrawMenu();
        }
        catch (Exception exception)
        {
            _guiFailed = true;
            ReportMenuUnavailable(UuvrReflection.Describe(exception));
        }
    }

    // Goes through the trace file, not just the Unity log: a game that strips IMGUI leaves the
    // menu key doing nothing at all, and the reason needs to be somewhere findable.
    private static void ReportMenuUnavailable(string reason)
    {
        UuvrTrace.LogWarning($"the in-game menu can't be drawn ({reason}).");
        UuvrTrace.Log(
            "Change settings in BepInEx/config/raicuparta.uuvr-*.cfg instead, or use the hotkeys: " +
            $"{ModConfiguration.Instance.CycleCameraTrackingKey.Value} cycles camera tracking mode, " +
            $"{ModConfiguration.Instance.CycleUiPatchModeKey.Value} cycles UI patch mode, " +
            $"{ModConfiguration.Instance.ToggleOverrideDepthKey.Value} toggles depth override.");
    }

    private void DrawMenu()
    {
        var menuWidth = Mathf.Min(560f, Screen.width * 0.6f);
        var menuHeight = Mathf.Min(760f, Screen.height * 0.92f);
        var menuRect = new Rect(30f, (Screen.height - menuHeight) * 0.5f, menuWidth, menuHeight);

        // Boxes stack, so drawing a couple makes the background more opaque and readable.
        GuiBridge.Box(menuRect, "");
        GuiBridge.Box(menuRect, "");
        GuiBridge.Box(menuRect, "");

        GuiBridge.BeginArea(new Rect(menuRect.x + 12f, menuRect.y + 10f, menuRect.width - 24f, menuRect.height - 20f));

        var vrEnabled = UuvrCore.Instance != null && UuvrCore.Instance.IsVrEnabled;
        GuiBridge.Label($"UUVR {UuvrPlugin.PluginVersion}  |  VR: {(vrEnabled ? "ON" : "OFF")}  |  Menu key: {ModConfiguration.Instance.ToggleMenuKey.Value}");
        GuiBridge.Space(6f);

        GuiBridge.BeginHorizontal();
        if (GuiBridge.Button($"Toggle VR ({ModConfiguration.Instance.ToggleVrKey.Value})")) UuvrCore.Instance?.ToggleVr();
        if (GuiBridge.Button($"Recenter ({ModConfiguration.Instance.RecenterKey.Value})")) VrRecenter.Recenter();
        if (GuiBridge.Button("Save config")) ModConfiguration.Instance.Config.Save();
        if (GuiBridge.Button("Close")) Close();
        GuiBridge.EndHorizontal();
        GuiBridge.Space(10f);

        var sections = GetSections();
        if (_activeSection.Length == 0 && sections.Count > 0) _activeSection = sections[0];

        const int tabsPerRow = 4;
        for (var rowStart = 0; rowStart < sections.Count; rowStart += tabsPerRow)
        {
            GuiBridge.BeginHorizontal();
            for (var index = rowStart; index < Mathf.Min(rowStart + tabsPerRow, sections.Count); index++)
            {
                var section = sections[index];
                var label = section == _activeSection ? $"[ {section} ]" : section;
                if (GuiBridge.Button(label)) _activeSection = section;
            }
            GuiBridge.EndHorizontal();
        }

        GuiBridge.Space(8f);
        _scrollPosition = GuiBridge.BeginScrollView(_scrollPosition);
        foreach (var entry in GetAllEntries())
        {
            if (entry.Definition.Section != _activeSection) continue;
            DrawEntry(entry);
            GuiBridge.Space(10f);
        }
        GuiBridge.EndScrollView();
        GuiBridge.EndArea();
    }

    private static List<ConfigEntryBase> GetAllEntries()
    {
        var entries = new List<ConfigEntryBase>();
        var config = ModConfiguration.Instance.Config;
        foreach (var definition in config.Keys)
        {
            entries.Add(config[definition]);
        }
        return entries;
    }

    private static List<string> GetSections()
    {
        var sections = new List<string>();
        foreach (var entry in GetAllEntries())
        {
            if (!sections.Contains(entry.Definition.Section)) sections.Add(entry.Definition.Section);
        }
        return sections;
    }

    private void DrawEntry(ConfigEntryBase entry)
    {
        var settingType = entry.SettingType;

        if (settingType == typeof(bool))
        {
            DrawBool(entry);
        }
        else if (settingType.IsEnum)
        {
            DrawEnum(entry);
        }
        else if ((settingType == typeof(float) || settingType == typeof(int)) && TryGetRange(entry, out var min, out var max))
        {
            DrawSlider(entry, min, max);
        }
        else
        {
            DrawSerializedText(entry);
        }
    }

    private static void DrawBool(ConfigEntryBase entry)
    {
        var value = (bool)entry.BoxedValue;
        var newValue = GuiBridge.Toggle(value, " " + entry.Definition.Key);
        if (newValue != value) entry.BoxedValue = newValue;
    }

    private static void DrawEnum(ConfigEntryBase entry)
    {
        var settingType = entry.SettingType;
        var currentValue = entry.BoxedValue;
        var values = Enum.GetValues(settingType);

        GuiBridge.Label($"{entry.Definition.Key}: {currentValue}");

        if (values.Length <= MaxEnumButtons)
        {
            GuiBridge.BeginHorizontal();
            foreach (var value in values)
            {
                var isCurrent = value.Equals(currentValue);
                var label = isCurrent ? $"* {value}" : value.ToString();
                if (GuiBridge.Button(label) && !isCurrent) entry.BoxedValue = value;
            }
            GuiBridge.EndHorizontal();
        }
        else
        {
            var currentIndex = Array.IndexOf(values, currentValue);
            GuiBridge.BeginHorizontal();
            if (GuiBridge.Button("<"))
            {
                var previousIndex = (currentIndex - 1 + values.Length) % values.Length;
                entry.BoxedValue = values.GetValue(previousIndex);
            }
            if (GuiBridge.Button(">"))
            {
                var nextIndex = (currentIndex + 1) % values.Length;
                entry.BoxedValue = values.GetValue(nextIndex);
            }
            GuiBridge.EndHorizontal();
        }
    }

    private static void DrawSlider(ConfigEntryBase entry, float min, float max)
    {
        var isInt = entry.SettingType == typeof(int);
        var value = Convert.ToSingle(entry.BoxedValue);

        GuiBridge.Label($"{entry.Definition.Key}: {(isInt ? value.ToString("0") : value.ToString("0.###"))}");
        var newValue = GuiBridge.HorizontalSlider(value, min, max);

        if (isInt)
        {
            var newIntValue = (int)Math.Round(newValue);
            if (newIntValue != (int)entry.BoxedValue) entry.BoxedValue = newIntValue;
        }
        else if (Math.Abs(newValue - value) > 0.0001f)
        {
            entry.BoxedValue = newValue;
        }
    }

    // Fallback editor for strings, vectors and unbounded numbers:
    // edits the serialized (config file) form of the value, applied on demand.
    private void DrawSerializedText(ConfigEntryBase entry)
    {
        GuiBridge.Label(entry.Definition.Key);

        var bufferKey = entry.Definition.Section + "/" + entry.Definition.Key;
        if (!_textBuffers.TryGetValue(bufferKey, out var buffer))
        {
            buffer = entry.GetSerializedValue();
        }

        GuiBridge.BeginHorizontal();
        _textBuffers[bufferKey] = GuiBridge.TextField(buffer);
        if (GuiBridge.Button("Apply"))
        {
            try
            {
                entry.SetSerializedValue(_textBuffers[bufferKey]);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"UUVR: failed to apply value for {entry.Definition.Key}: {exception.Message}");
            }
            _textBuffers.Remove(bufferKey);
        }
        GuiBridge.EndHorizontal();
    }

    private static bool TryGetRange(ConfigEntryBase entry, out float min, out float max)
    {
        min = 0f;
        max = 0f;

        var acceptableValues = entry.Description?.AcceptableValues;
        if (acceptableValues == null) return false;

        var minProperty = acceptableValues.GetType().GetProperty("MinValue");
        var maxProperty = acceptableValues.GetType().GetProperty("MaxValue");
        if (minProperty == null || maxProperty == null) return false;

        try
        {
            min = Convert.ToSingle(minProperty.GetValue(acceptableValues, null));
            max = Convert.ToSingle(maxProperty.GetValue(acceptableValues, null));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void TrySaveCursorState()
    {
        try
        {
            _previousCursorLockState = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            _cursorStateSaved = true;
        }
        catch (Exception)
        {
            _cursorStateSaved = false;
        }
    }

    private static void TryForceCursorVisible()
    {
        try
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        catch (Exception)
        {
            // Some very old Unity versions might not have this API; nothing we can do.
        }
    }

    private void TryRestoreCursorState()
    {
        if (!_cursorStateSaved) return;

        try
        {
            Cursor.lockState = _previousCursorLockState;
            Cursor.visible = _previousCursorVisible;
        }
        catch (Exception)
        {
        }
    }
}
