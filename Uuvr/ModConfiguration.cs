using System;
using System.ComponentModel;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace Uuvr;

public class ModConfiguration
{
    public static ModConfiguration Instance;

    public enum CameraTrackingMode
    {
        [Description("Absolute")] Absolute,
        [Description("Relative matrix")] RelativeMatrix,
#if MODERN
        // TODO: could add this for legacy too.
        [Description("Relative Transform")] RelativeTransform,
#endif
        [Description("Child")] Child,
    }

#if MODERN
    public enum VrApi
    {
        [Description("OpenVR")] OpenVr,
        [Description("OpenXR")] OpenXr,
    }
#endif

    public enum ScreenSpaceCanvasType
    {
        [Description("None")] None,

        [Description("Not rendering to texture")]
        NotToTexture,
        [Description("All")] All,
    }

    public enum UiRenderMode
    {
        [Description("Overlay camera (draws on top of everything)")]
        OverlayCamera,

        [Description("In world (can be occluded)")]
        InWorld,
    }

    public enum UiPatchMode
    {
        [Description("Don't touch UI")] None,

        [Description("Mirror flat screen (game not mirrored)")]
        Mirror,
        [Description("Patch Canvas objects")] CanvasRedirect,
    }

    public enum VrStartupMethod
    {
        [Description("Auto (pick whatever the game supports)")]
        Auto,

        [Description("XR Plugin Management (needs the game to ship it)")]
        XrPluginManagement,

        [Description("XR subsystems (modern games without XR packages)")]
        Subsystems,

        [Description("Legacy built-in VR (Unity 2019 and older)")]
        Legacy,
    }

    public readonly ConfigFile Config;
    public readonly ConfigEntry<VrStartupMethod> StartupMethod;
    public readonly ConfigEntry<bool> AutoStartVr;
    public readonly ConfigEntry<float> VrStartDelay;
    public readonly ConfigEntry<KeyboardKey.KeyCode> ToggleVrKey;
    public readonly ConfigEntry<KeyboardKey.KeyCode> ToggleMenuKey;
    public readonly ConfigEntry<KeyboardKey.KeyCode> RecenterKey;
    public readonly ConfigEntry<KeyboardKey.KeyCode> CycleCameraTrackingKey;
    public readonly ConfigEntry<KeyboardKey.KeyCode> CycleUiPatchModeKey;
    public readonly ConfigEntry<KeyboardKey.KeyCode> ToggleOverrideDepthKey;
    public readonly ConfigEntry<float> WorldScale;
    public readonly ConfigEntry<bool> OverrideNearClip;
    public readonly ConfigEntry<float> NearClipValue;
    public readonly ConfigEntry<string> CameraNameAllowList;
    public readonly ConfigEntry<string> CameraNameBlockList;
    public readonly ConfigEntry<CameraTrackingMode> CameraTracking;
    public readonly ConfigEntry<bool> RelativeCameraSetStereoView;
    public readonly ConfigEntry<int> VrCameraDepth;
    public readonly ConfigEntry<int> VrUiLayerOverride;
    public readonly ConfigEntry<Vector3> VrUiPosition;
    public readonly ConfigEntry<float> VrUiScale;
    public readonly ConfigEntry<string> VrUiShader;
    public readonly ConfigEntry<int> VrUiRenderQueue;
    public readonly ConfigEntry<bool> AlignCameraToHorizon;
    public readonly ConfigEntry<float> CameraPositionOffsetX;
    public readonly ConfigEntry<float> CameraPositionOffsetY;
    public readonly ConfigEntry<float> CameraPositionOffsetZ;
    public readonly ConfigEntry<bool> OverrideDepth;
    public readonly ConfigEntry<bool> PhysicsMatchHeadsetRefreshRate;
    public readonly ConfigEntry<UiPatchMode> PreferredUiPatchMode;
    public readonly ConfigEntry<UiRenderMode> PreferredUiRenderMode;
    public readonly ConfigEntry<ScreenSpaceCanvasType> ScreenSpaceCanvasTypesToPatch;
    public readonly ConfigEntry<string> ObjectsToDeactivateByComponent;
    public readonly ConfigEntry<string> ComponentsToDisable;
    public readonly ConfigEntry<float> ComponentSearchInterval;

#if MODERN
    public readonly ConfigEntry<VrApi> PreferredVrApi;
#endif

    private static CameraTrackingMode GetDefaultCameraTrackingMode()
    {
#if MODERN
        return CameraTrackingMode.RelativeTransform;
#else
        // The legacy build is also what modern IL2CPP games get, since there's no modern
        // IL2CPP build. RelativeMatrix overrides worldToCameraMatrix, which breaks the
        // camera's culling on modern Unity and leaves the scene empty apart from UI, so
        // those games start on Child — it renders through a real second camera instead.
        return IsModernUnity() ? CameraTrackingMode.Child : CameraTrackingMode.RelativeMatrix;
#endif
    }

    private static bool IsModernUnity()
    {
        try
        {
            var version = Application.unityVersion;
            var separator = version.IndexOf('.');
            if (separator > 0 && int.TryParse(version.Substring(0, separator), out var major))
            {
                // 2020+ dropped built-in VR; Unity 6 reports as 6000.x.
                return major >= 2020;
            }
        }
        catch (Exception)
        {
        }

        return false;
    }

    // BepInEx only knows how to serialize primitives, so Vector3 settings need a converter
    // registered before anything binds them, or the whole plugin fails to load.
    private static void RegisterVector3Converter()
    {
        try
        {
            if (TomlTypeConverter.CanConvert(typeof(Vector3))) return;

            TomlTypeConverter.AddConverter(typeof(Vector3), new BepInEx.Configuration.TypeConverter
            {
                ConvertToString = (value, type) =>
                {
                    var vector = (Vector3)value;
                    return string.Format(
                        CultureInfo.InvariantCulture, "{0}, {1}, {2}", vector.x, vector.y, vector.z);
                },
                ConvertToObject = (value, type) => ParseVector3(value),
            });
        }
        catch (Exception exception)
        {
            Debug.LogError($"UUVR: failed to register the Vector3 config converter: {exception}");
        }
    }

    // Accepts "x, y, z" and Unity's own "(x, y, z)" formatting.
    private static Vector3 ParseVector3(string value)
    {
        var parts = value.Trim().Trim('(', ')').Split(',');
        if (parts.Length != 3) throw new FormatException($"Expected 3 comma-separated numbers, got '{value}'");

        return new Vector3(
            float.Parse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
            float.Parse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
            float.Parse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    public ModConfiguration(ConfigFile config)
    {
        Instance = this;

        RegisterVector3Converter();

        Config = config;

#if MODERN
        PreferredVrApi = config.Bind(
            "General",
            "Preferred VR APi",
            VrApi.OpenXr,
            "VR API to use. Depending on the game, some APIs might be unavailable, so UUVR will fall back to one that works.");
#endif

        StartupMethod = config.Bind(
            "General",
            "VR Startup Method",
            VrStartupMethod.Auto,
            "How to turn VR on. Auto picks whichever method the game supports, which is almost always right; the others are for troubleshooting a game where Auto picks badly.");

        AutoStartVr = config.Bind(
            "General",
            "Start VR Automatically",
            true,
            "Turn VR on by itself shortly after the game starts. If a game crashes or hangs on startup with UUVR installed, set this to false; you can then start VR yourself with the toggle VR key once the game is running.");

        VrStartDelay = config.Bind(
            "General",
            "VR Start Delay",
            5f,
            new ConfigDescription(
                "Seconds to wait after the game starts before turning VR on. Starting VR before the game has finished loading can crash it, so raise this if the game dies on startup.",
                new AcceptableValueRange<float>(0f, 120f)));

        ToggleMenuKey = config.Bind(
            "Hotkeys",
            "Toggle Menu Key",
            KeyboardKey.KeyCode.F2,
            "Keyboard key used to open and close the in-game UUVR menu.");

        ToggleVrKey = config.Bind(
            "Hotkeys",
            "Toggle VR Key",
            KeyboardKey.KeyCode.F3,
            "Keyboard key used to turn VR mode on and off.");

        RecenterKey = config.Bind(
            "Hotkeys",
            "Recenter Key",
            KeyboardKey.KeyCode.F4,
            "Keyboard key used to recenter the VR view.");

        // These exist because the in-game menu needs Unity's IMGUI, which some IL2CPP games
        // strip out entirely. Hotkeys reach the settings that actually decide whether a game
        // renders at all, without depending on any UI.
        CycleCameraTrackingKey = config.Bind(
            "Hotkeys",
            "Cycle Camera Tracking Mode Key",
            KeyboardKey.KeyCode.F5,
            "Steps through the camera tracking modes. Use this if the game renders nothing in VR; the chosen mode is written to the log and the trace file.");

        CycleUiPatchModeKey = config.Bind(
            "Hotkeys",
            "Cycle UI Patch Mode Key",
            KeyboardKey.KeyCode.F6,
            "Steps through the UI patch modes.");

        ToggleOverrideDepthKey = config.Bind(
            "Hotkeys",
            "Toggle Override Depth Key",
            KeyboardKey.KeyCode.F7,
            "Turns 'Override Depth' on and off, which some games need before the VR camera shows anything.");

        WorldScale = config.Bind(
            "Camera",
            "World Scale",
            1f,
            new ConfigDescription(
                "Scales the world relative to the player. Higher values make the world bigger. Only affects relative and child camera tracking modes.",
                new AcceptableValueRange<float>(0.1f, 10f)));

        OverrideNearClip = config.Bind(
            "Camera",
            "Override Near Clip",
            false,
            "Overrides the camera near clipping plane. Useful when objects close to your face (like cockpits) get cut off.");

        NearClipValue = config.Bind(
            "Camera",
            "Near Clip Value",
            0.03f,
            new ConfigDescription(
                "Near clipping plane distance to use when 'Override Near Clip' is enabled.",
                new AcceptableValueRange<float>(0.001f, 2f)));

        CameraNameAllowList = config.Bind(
            "Camera Filters",
            "Only Use Cameras Named",
            "",
            "If not empty, only cameras whose name contains one of these values get used for VR. List of values separated by /. Example: 'MainCamera/PlayerCam'");

        CameraNameBlockList = config.Bind(
            "Camera Filters",
            "Never Use Cameras Named",
            "",
            "Cameras whose name contains one of these values never get used for VR. List of values separated by /. Example: 'Minimap/Portrait'");

        CameraTracking = config.Bind(
            "Camera",
            "Camera Tracking Mode",
            GetDefaultCameraTrackingMode(),
            "Defines how camera tracking is done. If the game renders nothing in VR (a flat, empty colour), this is the first setting to change. Changing this might require restarting the level.");

        RelativeCameraSetStereoView = config.Bind(
            "Relative Camera",
            "Use SetStereoView for Relative Camera",
            false,
            "Some games are better with this on, some are better with this off. Just try it and see which one is better. Changing this might require restarting the level.");

        AlignCameraToHorizon = config.Bind(
            "Camera",
            "Align To Horizon",
            false,
            "Prevents pitch and roll changes on the camera, allowing only yaw changes.");

        CameraPositionOffsetX = config.Bind(
            "Camera",
            "Camera Position Offset X",
            0f,
            "Changes position of tracked VR cameras");

        CameraPositionOffsetY = config.Bind(
            "Camera",
            "Camera Position Offset Y",
            0f,
            "Changes position of tracked VR cameras");

        CameraPositionOffsetZ = config.Bind(
            "Camera",
            "Camera Position Offset Z",
            0f,
            "Changes position of tracked VR cameras");

        OverrideDepth = config.Bind(
            "Camera",
            "Override Depth",
            false,
            "In some games, the VR camera won't display anything unless we override the camera depth value.");

        VrCameraDepth = config.Bind(
            "Camera",
            "Depth Value",
            1,
            new ConfigDescription(
                "Requires enabling 'Override Depth'. Range is -100 to 100, but you should try to find the lowest value that fixes visibility.",
                new AcceptableValueRange<int>(-100, 100)));

        PhysicsMatchHeadsetRefreshRate = config.Bind(
            "General",
            "Force physics rate to match headset refresh rate",
            false,
            "Can help fix jiterriness in games that rely a lot on physics. Might break a lot of games too.");

        PreferredUiPatchMode = config.Bind(
            "UI",
            "UI Patch Mode",
            UiPatchMode.Mirror,
            "Method to use for patching UI for VR.");

        VrUiLayerOverride = config.Bind(
            "UI",
            "VR UI Layer Override",
            -1,
            new ConfigDescription(
                "Layer to use for VR UI. By default (value -1) UUVR falls back to an unused (unnamed) layer.",
                new AcceptableValueRange<int>(-1, 31)));

        VrUiPosition = config.Bind(
            "UI",
            "VR UI Position",
            Vector3.forward * 1f,
            "Position of the VR UI projection relative to the camera.");

        VrUiScale = config.Bind(
            "UI",
            "VR UI Scale",
            1f,
            "Scale of the VR UI projection.");

        VrUiShader = config.Bind(
            "UI",
            "VR UI Shader",
            "",
            "Name of shader to use for the VR UI projection (passed to Unity's Shader.Find). Leave empty to let UUVR pick for you.");

        VrUiRenderQueue = config.Bind(
            "UI",
            "VR UI Render Queue",
            5000,
            new ConfigDescription(
                "Render queue to use for the VR UI projection. Default is 5000, which is the same as Unity's default canvas material.",
                new AcceptableValueRange<int>(0, 5000)));

        ScreenSpaceCanvasTypesToPatch = config.Bind(
            "UI",
            "Screen-space UI Elements to Patch",
            ScreenSpaceCanvasType.NotToTexture,
            "Screen-space UI elements are already visible in VR with no patches. But in some games, they are difficult to see in VR. So you can choose to patch some (or all) of them to be rendered in the VR UI screen.");

        PreferredUiRenderMode = config.Bind(
            "UI",
            "Preferred UI Plane Render Mode",
#if MODERN
            UiRenderMode.InWorld,
#else
            // Ideally we'd do overlay in all games but that mode can cause a lot of issues.
            // Most of the issues seem to be in more recent games, so at least for legacy we can default to overlay.
            UiRenderMode.OverlayCamera,
#endif
            "How to render the VR UI Plane. Overlay is usually better, but doesn't work in every game.");

        ObjectsToDeactivateByComponent = config.Bind(
            "Fixes",
            "Objects to Deactivate by Component",
            "",
            "Any objects that contains one of these components gets deactivated. List of fully qualified C# type names, separated by /. Example: 'Canvas, UnityEngine/HUD, Assembly-CSharp'");

        ComponentsToDisable = config.Bind(
            "Fixes",
            "Components to Disable.",
            "",
            "Names of components to disable. List of fully qualified C# type names, separated by /. Example: 'Canvas, UnityEngine/HUD, Assembly-CSharp'");

        ComponentSearchInterval = config.Bind(
            "Fixes",
            "Component Search Interval",
            1f,
            new ConfigDescription("Value in seconds, the interval between searches for components to disable.",
                new AcceptableValueRange<float>(0.5f, 30f)));
    }
}