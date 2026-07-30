using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Uuvr.ModUi;

// In-game settings menu, inspired by UEVR's overlay UI.
// Lets you tweak every UUVR setting live without leaving the game or editing config files.
// Rendered with IMGUI so it also shows up inside the headset when the VR UI is in Mirror mode.
//
// Laid out by hand rather than with GUILayout: see GuiBridge for why. That also means no
// scroll view, so long sections are paged instead.
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

    private const float RowHeight = 24f;
    private const float Gap = 4f;
    private const float Padding = 12f;
    private const int TabsPerRow = 4;

    private string _activeSection = "";
    private int _firstEntryIndex;
    private readonly Dictionary<string, string> _textBuffers = new();
    private bool _guiFailed;
    private CursorLockMode _previousCursorLockState;
    private bool _previousCursorVisible;
    private bool _cursorStateSaved;

    // Layout cursor, in screen space.
    private Rect _content;
    private float _cursorY;

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
            $"{ModConfiguration.Instance.ToggleOverrideDepthKey.Value} toggles depth override, " +
            $"{ModConfiguration.Instance.CycleVrCameraKey.Value} cycles which camera VR uses, " +
            $"{ModConfiguration.Instance.CameraReportKey.Value} writes a camera report.");
    }

    private Rect NextRow(float height)
    {
        var rect = new Rect(_content.x, _content.y + _cursorY, _content.width, height);
        _cursorY += height + Gap;
        return rect;
    }

    private static Rect Column(Rect row, int index, int count)
    {
        var width = (row.width - Gap * (count - 1)) / count;
        return new Rect(row.x + (width + Gap) * index, row.y, width, row.height);
    }

    private float RemainingHeight => _content.height - _cursorY;

    private void DrawMenu()
    {
        var menuWidth = Mathf.Min(560f, Screen.width * 0.6f);
        var menuHeight = Mathf.Min(760f, Screen.height * 0.92f);
        var menuRect = new Rect(30f, (Screen.height - menuHeight) * 0.5f, menuWidth, menuHeight);

        // Boxes stack, so drawing a couple makes the background more opaque and readable.
        GuiBridge.Box(menuRect, "");
        GuiBridge.Box(menuRect, "");
        GuiBridge.Box(menuRect, "");

        _content = new Rect(
            menuRect.x + Padding,
            menuRect.y + Padding,
            menuRect.width - Padding * 2f,
            menuRect.height - Padding * 2f);
        _cursorY = 0f;

        var vrEnabled = UuvrCore.Instance != null && UuvrCore.Instance.IsVrEnabled;
        GuiBridge.Label(
            NextRow(RowHeight),
            $"UUVR {UuvrPlugin.PluginVersion}  |  VR: {(vrEnabled ? "ON" : "OFF")}  |  Menu key: {ModConfiguration.Instance.ToggleMenuKey.Value}");

        var actions = NextRow(RowHeight);
        if (GuiBridge.Button(Column(actions, 0, 4), $"Toggle VR ({ModConfiguration.Instance.ToggleVrKey.Value})"))
        {
            UuvrCore.Instance?.ToggleVr();
        }
        if (GuiBridge.Button(Column(actions, 1, 4), $"Recenter ({ModConfiguration.Instance.RecenterKey.Value})"))
        {
            VrRecenter.Recenter();
        }
        if (GuiBridge.Button(Column(actions, 2, 4), "Save config")) ModConfiguration.Instance.Config.Save();
        if (GuiBridge.Button(Column(actions, 3, 4), "Close")) Close();

        _cursorY += Gap;

        var sections = GetSections();
        if (_activeSection.Length == 0 && sections.Count > 0) _activeSection = sections[0];

        for (var rowStart = 0; rowStart < sections.Count; rowStart += TabsPerRow)
        {
            var row = NextRow(RowHeight);
            var inRow = Mathf.Min(TabsPerRow, sections.Count - rowStart);
            for (var index = 0; index < inRow; index++)
            {
                var section = sections[rowStart + index];
                var label = section == _activeSection ? $"[ {section} ]" : section;
                if (GuiBridge.Button(Column(row, index, TabsPerRow), label))
                {
                    _activeSection = section;
                    _firstEntryIndex = 0;
                }
            }
        }

        _cursorY += Gap;
        DrawSectionEntries();
    }

    private void DrawSectionEntries()
    {
        var entries = new List<ConfigEntryBase>();
        foreach (var entry in GetAllEntries())
        {
            if (entry.Definition.Section == _activeSection) entries.Add(entry);
        }

        if (_firstEntryIndex >= entries.Count) _firstEntryIndex = 0;

        // Leave room for the pager, so it can't get pushed off the bottom of the menu.
        var pagerHeight = RowHeight + Gap;
        var index = _firstEntryIndex;
        while (index < entries.Count)
        {
            var entry = entries[index];
            if (RemainingHeight - pagerHeight < GetEntryHeight(entry)) break;

            DrawEntry(entry);
            _cursorY += Gap;
            index++;
        }

        var shown = index - _firstEntryIndex;
        if (shown >= entries.Count) return;

        _cursorY = _content.height - RowHeight;
        var pager = NextRow(RowHeight);
        if (GuiBridge.Button(Column(pager, 0, 3), "< Previous"))
        {
            // Paging back by however many fitted keeps the pages stable in both directions.
            _firstEntryIndex = Mathf.Max(0, _firstEntryIndex - Mathf.Max(1, shown));
        }
        GuiBridge.Label(
            Column(pager, 1, 3), $"  {_firstEntryIndex + 1}-{_firstEntryIndex + shown} of {entries.Count}");
        if (GuiBridge.Button(Column(pager, 2, 3), "Next >"))
        {
            _firstEntryIndex = index < entries.Count ? index : 0;
        }
    }

    private static float GetEntryHeight(ConfigEntryBase entry)
    {
        // Bools are a single toggle; everything else is a label with a control under it.
        return entry.SettingType == typeof(bool) ? RowHeight : RowHeight * 2f + Gap;
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

    private void DrawBool(ConfigEntryBase entry)
    {
        var value = (bool)entry.BoxedValue;
        var newValue = GuiBridge.Toggle(NextRow(RowHeight), value, " " + entry.Definition.Key);
        if (newValue != value) entry.BoxedValue = newValue;
    }

    private void DrawEnum(ConfigEntryBase entry)
    {
        var settingType = entry.SettingType;
        var currentValue = entry.BoxedValue;
        var values = Enum.GetValues(settingType);

        GuiBridge.Label(NextRow(RowHeight), $"{entry.Definition.Key}: {currentValue}");
        var row = NextRow(RowHeight);

        if (values.Length <= MaxEnumButtons)
        {
            for (var index = 0; index < values.Length; index++)
            {
                var value = values.GetValue(index);
                var isCurrent = value.Equals(currentValue);
                var label = isCurrent ? $"* {value}" : value.ToString();
                if (GuiBridge.Button(Column(row, index, values.Length), label) && !isCurrent)
                {
                    entry.BoxedValue = value;
                }
            }

            return;
        }

        var currentIndex = Array.IndexOf(values, currentValue);
        if (GuiBridge.Button(Column(row, 0, 2), "<"))
        {
            entry.BoxedValue = values.GetValue((currentIndex - 1 + values.Length) % values.Length);
        }
        if (GuiBridge.Button(Column(row, 1, 2), ">"))
        {
            entry.BoxedValue = values.GetValue((currentIndex + 1) % values.Length);
        }
    }

    private void DrawSlider(ConfigEntryBase entry, float min, float max)
    {
        var isInt = entry.SettingType == typeof(int);
        var value = Convert.ToSingle(entry.BoxedValue);

        GuiBridge.Label(
            NextRow(RowHeight), $"{entry.Definition.Key}: {(isInt ? value.ToString("0") : value.ToString("0.###"))}");

        // Sliders are drawn thinner than a row so they sit on the text baseline properly.
        var row = NextRow(RowHeight);
        var newValue = GuiBridge.HorizontalSlider(
            new Rect(row.x, row.y + RowHeight * 0.25f, row.width, RowHeight * 0.5f), value, min, max);

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
        GuiBridge.Label(NextRow(RowHeight), entry.Definition.Key);

        var bufferKey = entry.Definition.Section + "/" + entry.Definition.Key;
        if (!_textBuffers.TryGetValue(bufferKey, out var buffer))
        {
            buffer = entry.GetSerializedValue();
        }

        var row = NextRow(RowHeight);
        var applyWidth = 80f;
        _textBuffers[bufferKey] = GuiBridge.TextField(
            new Rect(row.x, row.y, row.width - applyWidth - Gap, row.height), buffer);

        if (GuiBridge.Button(new Rect(row.xMax - applyWidth, row.y, applyWidth, row.height), "Apply"))
        {
            try
            {
                entry.SetSerializedValue(_textBuffers[bufferKey]);
            }
            catch (Exception exception)
            {
                UuvrTrace.LogWarning($"failed to apply value for {entry.Definition.Key}: {exception.Message}");
            }
            _textBuffers.Remove(bufferKey);
        }
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
