using UnityEngine;
#if CPP
using System;
using System.Reflection;
#endif

namespace Uuvr.ModUi;

// Thin wrapper over Unity's immediate mode GUI (IMGUI), used by the in-game menu.
// Mono flavors call IMGUI directly. IL2CPP flavors don't have compile-time proxies
// for the IMGUI module, so they go through reflection into the interop assemblies
// that BepInEx generates at runtime. Everything is cached, and failures are soft:
// if IMGUI is unusable in a game, IsAvailable turns false and the menu is disabled.
//
// Everything here is GUI rather than GUILayout, and every widget takes an explicit Rect.
// GUILayout is a managed layout engine sitting on top of GUI, so it's the first thing to
// disappear from an IL2CPP game that never draws IMGUI itself — and when it goes, the menu
// goes with it. Positioning by hand costs a bit of arithmetic and keeps the menu working in
// games where the layout engine is gone.
public static class GuiBridge
{
#if MONO
    public static bool IsAvailable => true;

    public static void Label(Rect rect, string text) => GUI.Label(rect, text);
    public static bool Button(Rect rect, string text) => GUI.Button(rect, text);
    public static bool Toggle(Rect rect, bool value, string text) => GUI.Toggle(rect, value, text);
    public static string TextField(Rect rect, string text) => GUI.TextField(rect, text);
    public static float HorizontalSlider(Rect rect, float value, float min, float max) =>
        GUI.HorizontalSlider(rect, value, min, max);
    public static void Box(Rect rect, string text) => GUI.Box(rect, text);
    public static void BeginGroup(Rect rect) => GUI.BeginGroup(rect);
    public static void EndGroup() => GUI.EndGroup();
#elif CPP
    private static bool _initialized;
    private static bool _available;

    private static MethodInfo? _label;
    private static MethodInfo? _button;
    private static MethodInfo? _toggle;
    private static MethodInfo? _textField;
    private static MethodInfo? _horizontalSlider;
    private static MethodInfo? _box;
    private static MethodInfo? _beginGroup;
    private static MethodInfo? _endGroup;

    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return _available;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var guiType = FindType("UnityEngine.GUI");
            var styleType = FindType("UnityEngine.GUIStyle");
            if (guiType == null || styleType == null)
            {
                UuvrTrace.LogWarning("this game has no IMGUI types, so the in-game menu can't be drawn.");
                return;
            }

            _label = guiType.GetMethod("Label", new[] { typeof(Rect), typeof(string) });
            _button = guiType.GetMethod("Button", new[] { typeof(Rect), typeof(string) });
            _toggle = guiType.GetMethod("Toggle", new[] { typeof(Rect), typeof(bool), typeof(string) });
            _textField = guiType.GetMethod("TextField", new[] { typeof(Rect), typeof(string) });
            _horizontalSlider = guiType.GetMethod(
                "HorizontalSlider", new[] { typeof(Rect), typeof(float), typeof(float), typeof(float) });
            _box = guiType.GetMethod("Box", new[] { typeof(Rect), typeof(string) });
            _beginGroup = guiType.GetMethod("BeginGroup", new[] { typeof(Rect) });
            _endGroup = guiType.GetMethod("EndGroup", Type.EmptyTypes);

            _available = _label != null && _button != null && _toggle != null && _textField != null &&
                         _horizontalSlider != null && _box != null && _beginGroup != null && _endGroup != null;

            if (!_available)
            {
                UuvrTrace.LogWarning("some IMGUI methods are missing in this game, so the in-game menu can't be drawn.");
            }
        }
        catch (Exception exception)
        {
            UuvrTrace.LogWarning(
                $"couldn't set up IMGUI, so the in-game menu can't be drawn: {UuvrReflection.Describe(exception)}");
            _available = false;
        }
    }

    private static Type? FindType(string typeName)
    {
        var type = Type.GetType($"{typeName}, UnityEngine.IMGUIModule") ?? Type.GetType($"{typeName}, UnityEngine");
        if (type != null) return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type != null) return type;
        }

        return null;
    }

    public static void Label(Rect rect, string text) => _label!.Invoke(null, new[] { (object)rect, text });
    public static bool Button(Rect rect, string text) => (bool)_button!.Invoke(null, new[] { (object)rect, text });
    public static bool Toggle(Rect rect, bool value, string text) =>
        (bool)_toggle!.Invoke(null, new[] { (object)rect, value, text });
    public static string TextField(Rect rect, string text) =>
        (string)_textField!.Invoke(null, new[] { (object)rect, text });
    public static float HorizontalSlider(Rect rect, float value, float min, float max) =>
        (float)_horizontalSlider!.Invoke(null, new[] { (object)rect, value, min, max });
    public static void Box(Rect rect, string text) => _box!.Invoke(null, new[] { (object)rect, text });
    public static void BeginGroup(Rect rect) => _beginGroup!.Invoke(null, new[] { (object)rect });
    public static void EndGroup() => _endGroup!.Invoke(null, null);
#endif
}
