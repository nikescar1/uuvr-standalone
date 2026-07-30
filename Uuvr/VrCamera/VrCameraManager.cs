using System;
using System.Collections.Generic;
using System.Text;
#if CPP
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
#endif
using UnityEngine;

namespace Uuvr.VrCamera;

// Finds the game's cameras and turns them into VR cameras.
//
// Getting the list of cameras sounds trivial, but every API that does it is managed code
// wrapping an engine call, so IL2CPP strips whichever ones the game itself never uses. When
// that happened here the whole class threw on its first frame, no camera was ever picked up,
// and the game rendered nothing but its clear colour with no indication why. So the search
// tries each route in turn, remembers the one that works, and reports what it found.
public class VrCameraManager: MonoBehaviour
{
#if CPP
    public VrCameraManager(IntPtr pointer) : base(pointer)
    {
    }
#endif

    private enum SearchRoute
    {
        GetAllCameras,
        AllCamerasProperty,
        FindObjectsOfType,
    }

    private static readonly SearchRoute[] SearchRoutes =
    {
        // Cheapest first. The first two only return enabled cameras, which is what we want;
        // the scene search is the fallback that needs its own filtering.
        SearchRoute.GetAllCameras,
        SearchRoute.AllCamerasProperty,
        SearchRoute.FindObjectsOfType,
    };

    public static VrCameraManager? Instance { get; private set; }

    // The search is only how quickly a new camera gets adopted, so it doesn't need to run
    // every frame — and the scene-search fallback is far too expensive to.
    private const float SearchInterval = 0.25f;

    private readonly List<Camera> _cameras = new();
    private float _nextSearchTime;
    private int _routeIndex;
    private bool _searchFailed;
    private bool _reportPending = true;

#if CPP
    private Il2CppReferenceArray<Camera>? _cameraBuffer;
#else
    private Camera[]? _cameraBuffer;
#endif

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_searchFailed || Time.unscaledTime < _nextSearchTime) return;
        _nextSearchTime = Time.unscaledTime + SearchInterval;

        if (!TryFindCameras()) return;

        for (var index = 0; index < _cameras.Count; index++)
        {
            var camera = _cameras[index];

            try
            {
                var existing = camera == null ? null : camera.GetComponent<VrCamera>();
                if (existing != null)
                {
                    // Releases a camera the filters (or a hand-picked selection) no longer
                    // want, so those settings can be changed without restarting the level.
                    if (!CameraFilter.ShouldUseCamera(camera))
                    {
                        UuvrTrace.Log($"releasing camera '{camera.name}'");
                        Destroy(existing);
                    }

                    continue;
                }

                if (GetSkipReason(camera) != null) continue;

                UuvrTrace.Log($"using camera '{camera.name}' for VR");
                camera.gameObject.AddComponent<VrCamera>();
            }
            catch (Exception exception)
            {
                UuvrTrace.LogWarning($"couldn't set up a VR camera: {UuvrReflection.Describe(exception)}");
            }
        }

        // Held until a search actually produces something, so the report describes a loaded
        // scene rather than the empty one the mod starts up in.
        if (_reportPending && _cameras.Count > 0)
        {
            _reportPending = false;
            LogCameraReport();
        }
    }

    // Null means the camera is usable. Anything else is the reason it was passed over, which
    // is the single most useful thing to know when a game shows nothing in VR.
    private static string? GetSkipReason(Camera camera, bool applyFilters = true)
    {
        if (camera == null) return "destroyed";
        if (!camera.enabled || !camera.gameObject.activeInHierarchy) return "not enabled";
        if (VrCamera.IsIgnored(camera)) return "created by UUVR";
        if (camera.targetTexture != null) return "renders to a texture";
        if (camera.stereoTargetEye == StereoTargetEyeMask.None) return "stereo target eye is None";
        // Deliberately not marked as ignored, so changing the filters can pick it up later.
        if (applyFilters && !CameraFilter.ShouldUseCamera(camera)) return "not the selected camera";

        return null;
    }

    // Steps through [automatic] -> each camera the game is currently rendering with -> back.
    // Automatic detection takes every usable camera, which is usually right; this is for the
    // games where it isn't, and there's no way to guess which one is the one you're looking
    // through. The choice is remembered per scene, since menus and gameplay often differ.
    public void CycleVrCamera()
    {
        try
        {
            if (!TryFindCameras()) return;

            var candidates = new List<string>();
            for (var index = 0; index < _cameras.Count; index++)
            {
                var camera = _cameras[index];
                if (GetSkipReason(camera, false) != null) continue;
                if (!candidates.Contains(camera.name)) candidates.Add(camera.name);
            }

            if (candidates.Count == 0)
            {
                UuvrTrace.LogWarning("no cameras to choose from right now");
                return;
            }

            // Automatic sits at the front, so one more press past the last camera returns to it.
            var current = ForcedCameraMemory.GetForcedCameraName();
            var nextIndex = current == null ? 0 : candidates.IndexOf(current) + 1;
            var next = nextIndex >= candidates.Count ? null : candidates[nextIndex];

            ForcedCameraMemory.SetForcedCameraName(next);

            var scene = ForcedCameraMemory.GetCurrentSceneName();
            var sceneLabel = scene.Length > 0 ? $"scene '{scene}'" : "this game";
            UuvrTrace.Log(next == null
                ? $"VR camera for {sceneLabel}: automatic (all {candidates.Count} usable cameras)"
                : $"VR camera for {sceneLabel}: '{next}' ({nextIndex + 1} of {candidates.Count})");
        }
        catch (Exception exception)
        {
            UuvrTrace.LogWarning($"couldn't change the VR camera: {UuvrReflection.Describe(exception)}");
        }
    }

    public void LogCameraReport()
    {
        try
        {
            if (!TryFindCameras())
            {
                UuvrTrace.Log("camera report: couldn't list the game's cameras at all");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"--- camera report ({_cameras.Count} found via {SearchRoutes[_routeIndex]}) ---");
            report.AppendLine($"  Unity {Application.unityVersion}, render pipeline: {DescribeRenderPipeline()}");
            report.AppendLine($"  screen {Screen.width}x{Screen.height}, scene '{ForcedCameraMemory.GetCurrentSceneName()}'");

            for (var index = 0; index < _cameras.Count; index++)
            {
                var camera = _cameras[index];
                if (camera == null)
                {
                    report.AppendLine("  <destroyed>");
                    continue;
                }

                var status = VrCamera.IsVrCamera(camera) ? "VR CAMERA" : GetSkipReason(camera) ?? "usable";
                report.AppendLine(
                    $"  '{camera.name}' depth={camera.depth} enabled={camera.enabled} " +
                    $"mask=0x{camera.cullingMask:x8} clear={camera.clearFlags} bg={camera.backgroundColor} " +
                    $"target={(camera.targetTexture != null ? "texture" : "screen")} eye={camera.stereoTargetEye} " +
                    $"-> {status}");
            }

            report.Append("--- end of camera report ---");
            UuvrTrace.Log(report.ToString());
        }
        catch (Exception exception)
        {
            UuvrTrace.LogWarning($"couldn't write the camera report: {UuvrReflection.Describe(exception)}");
        }
    }

    // A scriptable render pipeline decides for itself how (and whether) a camera renders in
    // stereo, so knowing whether a game is on one — and which — is the difference between a
    // camera problem and a render pipeline problem. Found by reflection because the type
    // doesn't exist in the older Unity versions the legacy flavors are built against.
    private static string DescribeRenderPipeline()
    {
        try
        {
            var graphicsSettingsType = UuvrTypeFinder.FindType("UnityEngine.Rendering.GraphicsSettings");
            var pipelineProperty = graphicsSettingsType?.GetProperty("currentRenderPipeline") ??
                                   graphicsSettingsType?.GetProperty("renderPipelineAsset");
            if (pipelineProperty == null) return "built-in (no scriptable pipeline in this Unity version)";

            var pipeline = pipelineProperty.GetValue(null, null);
            return pipeline == null ? "built-in" : pipeline.GetType().Name;
        }
        catch (Exception exception)
        {
            return $"unknown ({UuvrReflection.Describe(exception)})";
        }
    }

    private bool TryFindCameras()
    {
        for (var index = _routeIndex; index < SearchRoutes.Length; index++)
        {
            try
            {
                _cameras.Clear();
                Fill(SearchRoutes[index], _cameras);

                if (index != _routeIndex)
                {
                    _routeIndex = index;
                    UuvrTrace.Log($"finding cameras through {SearchRoutes[index]}");
                }

                return true;
            }
            catch (Exception exception)
            {
                UuvrTrace.LogWarning(
                    $"can't find cameras through {SearchRoutes[index]} in this game: {UuvrReflection.Describe(exception)}");
            }
        }

        _searchFailed = true;
        UuvrTrace.LogError(
            "none of the ways of listing the game's cameras work here, so VR can't take a camera over. " +
            "Please report this game.");
        return false;
    }

    private void Fill(SearchRoute route, List<Camera> destination)
    {
        switch (route)
        {
            case SearchRoute.GetAllCameras:
            {
                var count = Camera.allCamerasCount;
                // At first this looks like it works with a Camera[], but Camera.GetAllCameras
                // just fills the array with nulls unless we use Il2CppReferenceArray.
                if (_cameraBuffer == null || _cameraBuffer.Length < count)
                {
                    _cameraBuffer = new Camera[Mathf.Max(count, 1)];
                }

                Camera.GetAllCameras(_cameraBuffer);
                for (var index = 0; index < count; index++)
                {
                    if (_cameraBuffer[index] != null) destination.Add(_cameraBuffer[index]);
                }

                break;
            }

            case SearchRoute.AllCamerasProperty:
            {
                var cameras = Camera.allCameras;
                if (cameras == null) throw new NotSupportedException("Camera.allCameras returned null");

                for (var index = 0; index < cameras.Length; index++)
                {
                    if (cameras[index] != null) destination.Add(cameras[index]);
                }

                break;
            }

            case SearchRoute.FindObjectsOfType:
            {
#if CPP
                var found = FindObjectsOfType(Il2CppType.Of<Camera>());
                if (found == null) throw new NotSupportedException("FindObjectsOfType returned null");

                for (var index = 0; index < found.Length; index++)
                {
                    var camera = found[index]?.TryCast<Camera>();
                    if (camera != null) destination.Add(camera);
                }
#else
                foreach (var camera in FindObjectsOfType<Camera>())
                {
                    if (camera != null) destination.Add(camera);
                }
#endif
                break;
            }
        }
    }
}
