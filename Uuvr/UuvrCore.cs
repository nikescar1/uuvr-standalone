using System;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;
using Uuvr.ModUi;
using Uuvr.VrCamera;
using Uuvr.VrTogglers;
using Uuvr.VrUi;

namespace Uuvr;

public class UuvrCore: MonoBehaviour
{
#if CPP
    public UuvrCore(IntPtr pointer) : base(pointer)
    {
    }
#endif

    public static UuvrCore? Instance { get; private set; }

    private readonly KeyboardKey _toggleVrKey = new (KeyboardKey.KeyCode.F3);
    private readonly KeyboardKey _toggleMenuKey = new (KeyboardKey.KeyCode.F2);
    private readonly KeyboardKey _recenterKey = new (KeyboardKey.KeyCode.F4);
    private readonly KeyboardKey _cycleCameraTrackingKey = new (KeyboardKey.KeyCode.F5);
    private readonly KeyboardKey _cycleUiPatchModeKey = new (KeyboardKey.KeyCode.F6);
    private readonly KeyboardKey _toggleOverrideDepthKey = new (KeyboardKey.KeyCode.F7);
    private float _originalFixedDeltaTime;
    private float _vrStartTime = float.MaxValue;
    private bool _vrStartAttempted;

    private VrUiManager? _vrUi;
    private ThingDisabler? _thingDisabler;
    private UuvrMenu? _menu;
    private PropertyInfo? _refreshRateProperty;
    private VrTogglerManager? _vrTogglerManager;

    public bool IsVrEnabled => _vrTogglerManager is { IsVrEnabled: true };

    private static bool _isQuitting;

    public static void Create()
    {
        if (Instance != null) return;

        new GameObject("UUVR").AddComponent<UuvrCore>();
    }

    private void Awake()
    {
        // The core gets created very early, and can get destroyed by the first scene load,
        // which recreates it. Make sure that never leaves two cores fighting each other.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.AddComponent<VrCameraManager>();

        // TODO: Emulate input.
        // UuvrBehaviour.Create<UuvrInput>(transform);
    }

    private void OnDestroy()
    {
        // A duplicate cleaning itself up, not the live core going away.
        if (Instance != this) return;

        Instance = null;

        if (_isQuitting) return;

        Debug.Log("UUVR has been destroyed. This shouldn't have happened. Recreating...");

        Create();
    }

    private void OnApplicationQuit()
    {
        _isQuitting = true;
    }

    public void ToggleVr()
    {
        // Nothing to toggle yet when automatic startup is off or hasn't happened;
        // treat the first press as "start VR now".
        if (_vrTogglerManager == null)
        {
            StartVr();
            return;
        }

        _vrTogglerManager.ToggleVr();
    }

    private void Start()
    {
        var xrDeviceType = Type.GetType("UnityEngine.XR.XRDevice, UnityEngine.XRModule") ??
                           Type.GetType("UnityEngine.XR.XRDevice, UnityEngine.VRModule") ??
                           Type.GetType("UnityEngine.VR.VRDevice, UnityEngine.VRModule") ??
                           Type.GetType("UnityEngine.VR.VRDevice, UnityEngine");

        _refreshRateProperty = xrDeviceType?.GetProperty("refreshRate");
        
        UuvrTrace.Log("core starting up");

        // Each part is created independently, so a game where one of them can't be set up
        // still gets everything else.
        _vrUi = TryCreate<VrUiManager>("VR UI");
        _thingDisabler = TryCreate<ThingDisabler>("thing disabler");
        _menu = TryCreate<UuvrMenu>("in-game menu");

        // VR deliberately isn't started here. Bringing up an XR runtime before the game has
        // finished loading its first scene can take the whole process down at the native
        // level, so it waits until the game is actually running (see UpdateVrStartup).
        _vrStartTime = Time.unscaledTime + Mathf.Max(0f, ModConfiguration.Instance.VrStartDelay.Value);

        SetPositionTrackingEnabled(false);
        UuvrTrace.Log("core started");
    }

    private T? TryCreate<T>(string description) where T : UuvrBehaviour
    {
        try
        {
            return UuvrBehaviour.Create<T>(transform);
        }
        catch (Exception exception)
        {
            UuvrTrace.LogError($"failed to create the {description}, continuing without it: {exception}");
            return null;
        }
    }

    private void Update()
    {
        UpdateVrStartup();
        UpdateHotkeys();
        UpdatePhysicsRate();
    }

    private void UpdateVrStartup()
    {
        if (_vrTogglerManager != null || _vrStartAttempted) return;
        if (!ModConfiguration.Instance.AutoStartVr.Value) return;
        if (Time.unscaledTime < _vrStartTime) return;

        StartVr();
    }

    // Also used by the toggle VR key, so VR can still be started by hand when
    // automatic startup is turned off (or when it failed).
    private void StartVr()
    {
        if (_vrStartAttempted && _vrTogglerManager == null)
        {
            UuvrTrace.Log("retrying VR startup");
        }

        _vrStartAttempted = true;

        try
        {
            UuvrTrace.Log("starting VR");
            _vrTogglerManager = new VrTogglerManager();
            UuvrTrace.Log("VR startup finished");
        }
        catch (Exception exception)
        {
            UuvrTrace.LogError($"failed to start VR: {exception}");
        }
    }

    private void UpdateHotkeys()
    {
        var config = ModConfiguration.Instance;
        if (config != null)
        {
            _toggleVrKey.Key = config.ToggleVrKey.Value;
            _toggleMenuKey.Key = config.ToggleMenuKey.Value;
            _recenterKey.Key = config.RecenterKey.Value;
            _cycleCameraTrackingKey.Key = config.CycleCameraTrackingKey.Value;
            _cycleUiPatchModeKey.Key = config.CycleUiPatchModeKey.Value;
            _toggleOverrideDepthKey.Key = config.ToggleOverrideDepthKey.Value;
        }

        if (_toggleVrKey.UpdateIsDown()) ToggleVr();
        if (_toggleMenuKey.UpdateIsDown()) _menu?.ToggleOpen();
        if (_recenterKey.UpdateIsDown()) VrRecenter.Recenter();

        if (config == null) return;

        if (_cycleCameraTrackingKey.UpdateIsDown()) CycleSetting(config.CameraTracking, "Camera Tracking Mode");
        if (_cycleUiPatchModeKey.UpdateIsDown()) CycleSetting(config.PreferredUiPatchMode, "UI Patch Mode");

        if (_toggleOverrideDepthKey.UpdateIsDown())
        {
            config.OverrideDepth.Value = !config.OverrideDepth.Value;
            UuvrTrace.Log($"Override Depth: {config.OverrideDepth.Value}");
        }
    }

    // Steps an enum setting to its next value and says which one it landed on. Reaching these
    // by hotkey matters in games that strip IMGUI, where the in-game menu can't open at all.
    private static void CycleSetting(ConfigEntryBase entry, string description)
    {
        try
        {
            var values = Enum.GetValues(entry.SettingType);
            var index = Array.IndexOf(values, entry.BoxedValue);
            var next = values.GetValue((index + 1) % values.Length);
            entry.BoxedValue = next;
            UuvrTrace.Log($"{description}: {next}");
        }
        catch (Exception exception)
        {
            UuvrTrace.LogWarning($"couldn't change {description}: {UuvrReflection.Describe(exception)}");
        }
    }

    private void UpdatePhysicsRate()
    {
        if (_originalFixedDeltaTime == 0)
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
        }

        if (_refreshRateProperty == null) return;

        var headsetRefreshRate = (float)_refreshRateProperty.GetValue(null, null);
        if (headsetRefreshRate <= 0) return;

        if (ModConfiguration.Instance.PhysicsMatchHeadsetRefreshRate.Value)
        {
            Time.fixedDeltaTime = 1f / (float) _refreshRateProperty.GetValue(null, null);
        }
        else
        {
            Time.fixedDeltaTime = _originalFixedDeltaTime;
        }
    }

    private static void SetPositionTrackingEnabled(bool positionTrackingEnabled)
    {
        var inputTrackingType = 
            Type.GetType("UnityEngine.XR.InputTracking, UnityEngine.XRModule") ??
            Type.GetType("UnityEngine.XR.InputTracking, UnityEngine.VRModule") ??
            Type.GetType("UnityEngine.VR.InputTracking, UnityEngine.VRModule") ??
            Type.GetType("UnityEngine.VR.InputTracking, UnityEngine");

        if (inputTrackingType != null)
        {
            var disablePositionalTrackingProperty = inputTrackingType.GetProperty("disablePositionalTracking");
            if (disablePositionalTrackingProperty != null)
            {
                disablePositionalTrackingProperty.SetValue(null, !positionTrackingEnabled, null);
            }
            else
            {
                Debug.LogWarning("Failed to get property disablePositionalTracking");
            }
        }
        else
        {
            Debug.LogWarning("Failed to get type UnityEngine.XR.InputTracking");
        }
    }
}
