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

    public static void Create()
    {
        new GameObject("UUVR").AddComponent<UuvrCore>();
    }

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.AddComponent<VrCameraManager>();

        // TODO: Emulate input.
        // UuvrBehaviour.Create<UuvrInput>(transform);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        Debug.Log("UUVR has been destroyed. This shouldn't have happened. Recreating...");

        Create();
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
        
        _vrUi = UuvrBehaviour.Create<VrUiManager>(transform);
        _thingDisabler = UuvrBehaviour.Create<ThingDisabler>(transform);
        _menu = UuvrBehaviour.Create<UuvrMenu>(transform);

        _vrTogglerManager = new VrTogglerManager();

        SetPositionTrackingEnabled(false);
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
