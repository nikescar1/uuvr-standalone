using System;
using System.Reflection;
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
    private float _originalFixedDeltaTime;

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
        _vrTogglerManager?.ToggleVr();
    }

    private void Start()
    {
        var xrDeviceType = Type.GetType("UnityEngine.XR.XRDevice, UnityEngine.XRModule") ??
                           Type.GetType("UnityEngine.XR.XRDevice, UnityEngine.VRModule") ??
                           Type.GetType("UnityEngine.VR.VRDevice, UnityEngine.VRModule") ??
                           Type.GetType("UnityEngine.VR.VRDevice, UnityEngine");

        _refreshRateProperty = xrDeviceType?.GetProperty("refreshRate");
        
        // Each part is created independently, so a game where one of them can't be set up
        // still gets everything else. Starting VR is the important one, so it goes last
        // and never depends on the UI having worked.
        _vrUi = TryCreate<VrUiManager>("VR UI");
        _thingDisabler = TryCreate<ThingDisabler>("thing disabler");
        _menu = TryCreate<UuvrMenu>("in-game menu");

        try
        {
            _vrTogglerManager = new VrTogglerManager();
        }
        catch (Exception exception)
        {
            Debug.LogError($"UUVR: failed to set up VR: {exception}");
        }

        SetPositionTrackingEnabled(false);
    }

    private T? TryCreate<T>(string description) where T : UuvrBehaviour
    {
        try
        {
            return UuvrBehaviour.Create<T>(transform);
        }
        catch (Exception exception)
        {
            Debug.LogError($"UUVR: failed to create the {description}, continuing without it: {exception}");
            return null;
        }
    }

    private void Update()
    {
        UpdateHotkeys();
        UpdatePhysicsRate();
    }

    private void UpdateHotkeys()
    {
        var config = ModConfiguration.Instance;
        if (config != null)
        {
            _toggleVrKey.Key = config.ToggleVrKey.Value;
            _toggleMenuKey.Key = config.ToggleMenuKey.Value;
            _recenterKey.Key = config.RecenterKey.Value;
        }

        if (_toggleVrKey.UpdateIsDown()) ToggleVr();
        if (_toggleMenuKey.UpdateIsDown()) _menu?.ToggleOpen();
        if (_recenterKey.UpdateIsDown()) VrRecenter.Recenter();
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
