using System;
using UnityEngine;

namespace Uuvr.VrCamera;

// TODO: add manual offsets.
public class VrCameraOffset: UuvrBehaviour
{
#if CPP
    public VrCameraOffset(System.IntPtr pointer) : base(pointer)
    {
    }
#endif

    protected override void OnBeforeRender()
    {
        base.OnBeforeRender();
        UpdateTransform();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        OnSettingChanged();
    }

    protected override void OnSettingChanged()
    {
        base.OnSettingChanged();
        var config = ModConfiguration.Instance;

        transform.localPosition = new Vector3(
            config.CameraPositionOffsetX.Value,
            config.CameraPositionOffsetY.Value,
            config.CameraPositionOffsetZ.Value);

        ApplyWorldScale();
    }

    // Scaling the tracked camera's parent scales the whole experience:
    // bigger parent scale means bigger IPD, which makes the world feel smaller.
    // So we invert the configured world scale to get the expected result.
    private void ApplyWorldScale()
    {
        var worldScale = Mathf.Clamp(ModConfiguration.Instance.WorldScale.Value, 0.1f, 10f);
        var parentScale = 1f / worldScale;
        transform.localScale = new Vector3(parentScale, parentScale, parentScale);
    }

    private void Update()
    {
        UpdateTransform();
    }
    
    private void LateUpdate()
    {
        UpdateTransform();
    }

    private void UpdateTransform()
    {
        if (ModConfiguration.Instance.AlignCameraToHorizon.Value)
        {
            var forward = Vector3.ProjectOnPlane(transform.parent.forward, Vector3.up);
            transform.LookAt(transform.position + forward, Vector3.up);
        }
        else
        {
            transform.localRotation = Quaternion.identity;
        }
    }
}
