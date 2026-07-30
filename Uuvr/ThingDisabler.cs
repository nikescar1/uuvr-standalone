using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using UnityEngine;
#if CPP
using Il2CppInterop.Runtime;
#endif

namespace Uuvr;

// TODO a lot of uncached reflection here.
// I want to overhaul the config for this anyway, so will clean this up when that time comes.
public class ThingDisabler : UuvrBehaviour
{
#if CPP
    public ThingDisabler(IntPtr pointer) : base(pointer)
    {
    }
#endif
    private float _previousSearchTime;

    protected override void Awake()
    {
        base.Awake();

        OnSettingChanged();
    }

    private void Update()
    {
        if (Time.unscaledTime < _previousSearchTime + ModConfiguration.Instance.ComponentSearchInterval.Value) return;

        _previousSearchTime = Time.unscaledTime;
        SearchAndDisableComponents();
    }

    private static void SearchAndDisableComponents()
    {
        try
        {
            DeactivateObjectsByComponent();
            DisableComponents();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ThingDisabler] Error during component search: {ex.Message}");
        }
    }

    private static IEnumerable<UnityEngine.Object>? FindObjects(string componentNamesString)
    {
        if (componentNamesString.IsNullOrWhiteSpace()) return null;
        
        return componentNamesString
            .Split('/')
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrEmpty(name))
            .SelectMany(name =>
            {
                var type = Type.GetType(name);
                if (type == null)
                {
                    Debug.LogWarning($"[ThingDisabler] Failed to find type '{name}'");
                    return Enumerable.Empty<UnityEngine.Object>();
                }
#if CPP
                var foundObjects = FindObjectsOfType(Il2CppType.From(type));
                var results = new List<UnityEngine.Object>();
                if (foundObjects != null)
                {
                    for (var index = 0; index < foundObjects.Length; index++)
                    {
                        results.Add(foundObjects[index]);
                    }
                }
                return (IEnumerable<UnityEngine.Object>)results;
#else
                return FindObjectsOfType(type);
#endif
            });
    }

    private static void DeactivateObjectsByComponent()
    {
        var componentObjects = FindObjects(ModConfiguration.Instance.ObjectsToDeactivateByComponent.Value);
        if (componentObjects == null) return;

        foreach (var componentObject in componentObjects)
        {
#if CPP
            // IL2CPP wrappers carry their declared type (Object here), so reflecting for
            // 'gameObject' on the wrapper finds nothing; ask the il2cpp side instead.
            var component = componentObject.TryCast<Component>();
            var gameObject = component != null ? component.gameObject : null;
#else
            var gameObjectProperty = componentObject.GetType()
                .GetProperty("gameObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (gameObjectProperty == null) continue;
            var gameObject = (GameObject)gameObjectProperty.GetValue(componentObject, null);
#endif
            if (gameObject == null) continue;
            gameObject.SetActive(false);
        }
    }

    private static void DisableComponents()
    {
        var componentObjects = FindObjects(ModConfiguration.Instance.ComponentsToDisable.Value);
        if (componentObjects == null) return;

        foreach (var componentObject in componentObjects)
        {
#if CPP
            var behaviour = componentObject.TryCast<Behaviour>();
            if (behaviour != null) behaviour.enabled = false;
#else
            var type = componentObject.GetType();
            var enabledProperty = type.GetProperty("enabled",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            if (enabledProperty == null || !enabledProperty.CanWrite ||
                enabledProperty.PropertyType != typeof(bool)) continue;

            enabledProperty.SetValue(componentObject, false, null);
#endif
        }
    }
}