using UnityEngine;
#if CPP
using System;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
#endif

namespace Uuvr.ModUi;

// Thin wrapper over Unity's immediate mode GUI (IMGUI), used by the in-game menu.
// Mono flavors call IMGUI directly. IL2CPP flavors don't have compile-time proxies
// for the IMGUI module, so they go through reflection into the interop assemblies
// that BepInEx generates at runtime. Everything is cached, and failures are soft:
// if IMGUI is stripped from the game, IsAvailable turns false and the menu is disabled.
public static class GuiBridge
{
#if MONO
    public static bool IsAvailable => true;

    public static void Label(string text) => GUILayout.Label(text);
    public static bool Button(string text) => GUILayout.Button(text);
    public static bool Toggle(bool value, string text) => GUILayout.Toggle(value, text);
    public static string TextField(string text) => GUILayout.TextField(text);
    public static float HorizontalSlider(float value, float min, float max) => GUILayout.HorizontalSlider(value, min, max);
    public static void Box(Rect rect, string text) => GUI.Box(rect, text);
    public static void BeginArea(Rect rect) => GUILayout.BeginArea(rect);
    public static void EndArea() => GUILayout.EndArea();
    public static Vector2 BeginScrollView(Vector2 scrollPosition) => GUILayout.BeginScrollView(scrollPosition);
    public static void EndScrollView() => GUILayout.EndScrollView();
    public static void BeginHorizontal() => GUILayout.BeginHorizontal();
    public static void EndHorizontal() => GUILayout.EndHorizontal();
    public static void Space(float pixels) => GUILayout.Space(pixels);
    public static void FlexibleSpace() => GUILayout.FlexibleSpace();
#elif CPP
    private static bool _initialized;
    private static bool _available;

    private static object? _emptyOptions;
    private static MethodInfo? _label;
    private static MethodInfo? _button;
    private static MethodInfo? _toggle;
    private static MethodInfo? _textField;
    private static MethodInfo? _horizontalSlider;
    private static MethodInfo? _box;
    private static MethodInfo? _beginArea;
    private static MethodInfo? _endArea;
    private static MethodInfo? _beginScrollView;
    private static MethodInfo? _endScrollView;
    private static MethodInfo? _beginHorizontal;
    private static MethodInfo? _endHorizontal;
    private static MethodInfo? _space;
    private static MethodInfo? _flexibleSpace;

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
            var guiLayoutType = FindType("UnityEngine.GUILayout");
            var guiType = FindType("UnityEngine.GUI");
            var optionType = FindType("UnityEngine.GUILayoutOption");
            if (guiLayoutType == null || guiType == null || optionType == null)
            {
                Debug.LogWarning("UUVR: IMGUI types not found in this game, in-game menu will be unavailable.");
                return;
            }

            var optionsArrayType = typeof(Il2CppReferenceArray<>).MakeGenericType(optionType);
            _emptyOptions = Activator.CreateInstance(optionsArrayType, 0L);

            _label = guiLayoutType.GetMethod("Label", new[] { typeof(string), optionsArrayType });
            _button = guiLayoutType.GetMethod("Button", new[] { typeof(string), optionsArrayType });
            _toggle = guiLayoutType.GetMethod("Toggle", new[] { typeof(bool), typeof(string), optionsArrayType });
            _textField = guiLayoutType.GetMethod("TextField", new[] { typeof(string), optionsArrayType });
            _horizontalSlider = guiLayoutType.GetMethod("HorizontalSlider", new[] { typeof(float), typeof(float), typeof(float), optionsArrayType });
            _box = guiType.GetMethod("Box", new[] { typeof(Rect), typeof(string) });
            _beginArea = guiLayoutType.GetMethod("BeginArea", new[] { typeof(Rect) });
            _endArea = guiLayoutType.GetMethod("EndArea", Type.EmptyTypes);
            _beginScrollView = guiLayoutType.GetMethod("BeginScrollView", new[] { typeof(Vector2), optionsArrayType });
            _endScrollView = guiLayoutType.GetMethod("EndScrollView", Type.EmptyTypes);
            _beginHorizontal = guiLayoutType.GetMethod("BeginHorizontal", new[] { optionsArrayType });
            _endHorizontal = guiLayoutType.GetMethod("EndHorizontal", Type.EmptyTypes);
            _space = guiLayoutType.GetMethod("Space", new[] { typeof(float) });
            _flexibleSpace = guiLayoutType.GetMethod("FlexibleSpace", Type.EmptyTypes);

            _available = _label != null && _button != null && _toggle != null && _textField != null &&
                         _horizontalSlider != null && _box != null && _beginArea != null && _endArea != null &&
                         _beginScrollView != null && _endScrollView != null && _beginHorizontal != null &&
                         _endHorizontal != null && _space != null && _flexibleSpace != null;

            if (!_available)
            {
                Debug.LogWarning("UUVR: some IMGUI methods are missing in this game, in-game menu will be unavailable.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"UUVR: failed to set up IMGUI bridge, in-game menu will be unavailable: {UuvrReflection.Describe(exception)}");
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

    public static void Label(string text) => _label!.Invoke(null, new[] { (object)text, _emptyOptions });
    public static bool Button(string text) => (bool)_button!.Invoke(null, new[] { (object)text, _emptyOptions });
    public static bool Toggle(bool value, string text) => (bool)_toggle!.Invoke(null, new[] { (object)value, text, _emptyOptions });
    public static string TextField(string text) => (string)_textField!.Invoke(null, new[] { (object)text, _emptyOptions });
    public static float HorizontalSlider(float value, float min, float max) => (float)_horizontalSlider!.Invoke(null, new[] { (object)value, min, max, _emptyOptions });
    public static void Box(Rect rect, string text) => _box!.Invoke(null, new[] { (object)rect, text });
    public static void BeginArea(Rect rect) => _beginArea!.Invoke(null, new[] { (object)rect });
    public static void EndArea() => _endArea!.Invoke(null, null);
    public static Vector2 BeginScrollView(Vector2 scrollPosition) => (Vector2)_beginScrollView!.Invoke(null, new[] { (object)scrollPosition, _emptyOptions });
    public static void EndScrollView() => _endScrollView!.Invoke(null, null);
    public static void BeginHorizontal() => _beginHorizontal!.Invoke(null, new[] { _emptyOptions });
    public static void EndHorizontal() => _endHorizontal!.Invoke(null, null);
    public static void Space(float pixels) => _space!.Invoke(null, new[] { (object)pixels });
    public static void FlexibleSpace() => _flexibleSpace!.Invoke(null, null);
#endif
}
