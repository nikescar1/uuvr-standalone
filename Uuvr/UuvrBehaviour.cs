using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
#if CPP
using Il2CppInterop.Runtime.Injection;
#endif

namespace Uuvr;

public class UuvrBehaviour: MonoBehaviour
{
#if CPP
    private Action? _onBeforeRenderAction;
#endif

    // Not every game keeps Application.onBeforeRender around; when it's been stripped,
    // every enable/disable would otherwise spam the log with the same failure.
    private static bool _beforeRenderUnavailable;

#if CPP
    public UuvrBehaviour(IntPtr pointer) : base(pointer)
    {
    }
#endif

    public static T Create<T>(Transform parent) where T: UuvrBehaviour
    {
#if CPP
        // Adding a component whose type was never injected into IL2CPP throws a confusing
        // type initializer error, so make sure it's registered rather than relying on
        // every new behaviour being added to the list in UuvrPlugin.
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<T>())
        {
            Debug.LogWarning($"UUVR: type {typeof(T).Name} wasn't registered in IL2CPP, registering it now.");
            ClassInjector.RegisterTypeInIl2Cpp<T>();
        }
#endif

        return new GameObject(typeof(T).Name)
        {
            transform =
            {
                parent = parent,
                localPosition = Vector3.zero,
                localRotation = Quaternion.identity
            }
        }.AddComponent<T>();
    }
    
    protected virtual void Awake()
    {
#if CPP
        _onBeforeRenderAction = OnBeforeRender;
#endif
    }

    protected virtual void OnEnable()
    {
        if (!_beforeRenderUnavailable)
        {
            try
            {
#if CPP
                Application.add_onBeforeRender(_onBeforeRenderAction);
#else
                // Doesn't exist for Unity <2017.
                Application.onBeforeRender += OnBeforeRender;
#endif
            }
            catch (Exception exception)
            {
                _beforeRenderUnavailable = true;
                Debug.LogWarning(
                    $"UUVR: this game has no usable BeforeRender callback ({exception.Message}). Falling back to Update/LateUpdate for tracking. This won't be logged again.");
            }
        }

#if MODERN
        RenderPipelineManager.beginFrameRendering += OnBeginFrameRendering;
        RenderPipelineManager.endFrameRendering += OnEndFrameRendering;
#endif

        ModConfiguration.Instance.Config.SettingChanged += ConfigOnSettingChanged;
    }

    protected virtual void OnDisable()
    {
        if (!_beforeRenderUnavailable)
        {
            try
            {
#if CPP
                Application.remove_onBeforeRender(_onBeforeRenderAction);
#else
                Application.onBeforeRender -= OnBeforeRender;
#endif
            }
            catch (Exception)
            {
                // Already reported when subscribing failed; nothing useful to add here.
                _beforeRenderUnavailable = true;
            }
        }


#if MODERN
        // TODO: This might not exist? maybe ok for modern though.
        RenderPipelineManager.beginFrameRendering -= OnBeginFrameRendering;
        RenderPipelineManager.endFrameRendering -= OnEndFrameRendering;
#endif
        
        ModConfiguration.Instance.Config.SettingChanged -= ConfigOnSettingChanged;
    }

    private void ConfigOnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        OnSettingChanged();
    }

    protected virtual void OnBeforeRender() {}

    protected virtual void OnSettingChanged() {}

#if MODERN
    private void OnBeginFrameRendering(ScriptableRenderContext arg1, Camera[] arg2)
    {
        OnBeginFrameRendering();
    }

    private void OnEndFrameRendering(ScriptableRenderContext arg1, Camera[] arg2)
    {
        OnEndFrameRendering();
    }
    
    protected virtual void OnBeginFrameRendering() {}
    
    protected virtual void OnEndFrameRendering() {}
#endif
}
