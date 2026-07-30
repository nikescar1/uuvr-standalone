using System;
using System.Collections.Generic;
using System.Reflection;

namespace Uuvr.VrTogglers;

// Starts VR through Unity's XR subsystem API, without needing any XR package.
//
// Unity's XR Plugin Management package is only a managed wrapper: it looks up subsystem
// descriptors registered by the native XR plugins, creates the display and input subsystems,
// and starts them. Games built without the XR packages have none of that managed code, but the
// subsystem API itself lives in the engine (UnityEngine.XRModule / SubsystemsModule) and is
// always there, and UUVR installs the native plugin manifests that register the descriptors.
//
// So for those games we do what the package would have done, by reflection, so this works from
// the legacy IL2CPP build with no compile-time XR references.
//
// Member names were checked against the interop assemblies of a real Unity 2021 IL2CPP game
// (see tools/Uuvr.ApiCheck), so they're the actual API rather than what the public docs imply.
public class SubsystemXrToggler : VrToggler
{
    // The registry the native XR plugins report their descriptors into. Reading its static
    // lists needs no method arguments and no list construction on our side, which makes it
    // the least fragile route under IL2CPP interop.
    private const string DescriptorStoreTypeName = "UnityEngine.SubsystemsImplementation.SubsystemDescriptorStore";

    private static readonly string[] DescriptorStoreListNames =
    {
        "s_IntegratedDescriptors",
        "s_StandaloneDescriptors",
        "s_DeprecatedDescriptors",
    };

    // There is no public Create() on a descriptor: IntegratedSubsystemDescriptor<T> exposes
    // CreateImpl(), and ISubsystemDescriptor.Create is an explicit interface implementation,
    // whose reflected name therefore carries the interface prefix.
    private static readonly string[] CreateMethodNames =
    {
        "CreateImpl",
        "UnityEngine.ISubsystemDescriptor.Create",
        "Create",
    };

    private object? _displaySubsystem;
    private object? _inputSubsystem;

    public static bool IsSupported()
    {
        return UuvrTypeFinder.FindType("UnityEngine.SubsystemManager") != null &&
               UuvrTypeFinder.FindType("UnityEngine.XR.XRDisplaySubsystem") != null;
    }

    protected override bool SetUp()
    {
        var descriptors = GetSubsystemDescriptors();
        if (descriptors.Count == 0)
        {
            UuvrTrace.LogError(
                "no XR subsystem descriptors are registered. The native XR plugins didn't load; check that the UnitySubsystems folder and the Plugins folder made it into the game, and that SteamVR or your OpenXR runtime is running.");
            return false;
        }

        UuvrTrace.Log($"found {descriptors.Count} subsystem descriptor(s):");
        foreach (var descriptor in descriptors)
        {
            UuvrTrace.Log($"  {descriptor.GetType().Name} id='{GetDescriptorId(descriptor)}'");
        }

        _displaySubsystem = CreateSubsystem(descriptors, "UnityEngine.XR.XRDisplaySubsystemDescriptor", "UnityEngine.XR.XRDisplaySubsystem", "display");
        _inputSubsystem = CreateSubsystem(descriptors, "UnityEngine.XR.XRInputSubsystemDescriptor", "UnityEngine.XR.XRInputSubsystem", "input");

        if (_displaySubsystem == null)
        {
            UuvrTrace.LogError("couldn't create an XR display subsystem, so VR can't be started.");
            return false;
        }

        // Input is nice to have; without it there's no head tracking from this path,
        // but UUVR reads poses through InputTracking anyway.
        if (_inputSubsystem == null)
        {
            UuvrTrace.LogWarning("no XR input subsystem was created; tracking may not work.");
        }

        return true;
    }

    private static List<object> GetSubsystemDescriptors()
    {
        // Route 1: read the descriptor registry's static lists directly.
        var results = ReadDescriptorStoreLists();
        if (results.Count > 0) return results;

        // Route 2: SubsystemManager.GetAllSubsystemDescriptors(list).
        results = QueryViaSubsystemManager();
        if (results.Count > 0) return results;

        // Route 3: older Unity versions expose a generic query instead.
        return QueryViaGenericGetDescriptors();
    }

    private static List<object> ReadDescriptorStoreLists()
    {
        var results = new List<object>();

        var storeType = UuvrTypeFinder.FindType(DescriptorStoreTypeName);
        if (storeType == null)
        {
            UuvrTrace.Log("descriptor store type not found (normal for Unity 2019 and older), trying SubsystemManager");
            return results;
        }

        foreach (var listName in DescriptorStoreListNames)
        {
            try
            {
                var listProperty = storeType.GetProperty(
                    listName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (listProperty == null)
                {
                    UuvrTrace.Log($"  descriptor store has no '{listName}' list");
                    continue;
                }

                var list = listProperty.GetValue(null, null);
                if (list == null)
                {
                    UuvrTrace.Log($"  descriptor store list '{listName}' is null");
                    continue;
                }

                var countBefore = results.Count;
                EnumerateListInto(results, list);
                UuvrTrace.Log($"  descriptor store list '{listName}': {results.Count - countBefore} entries");
            }
            catch (Exception exception)
            {
                UuvrTrace.LogWarning($"reading descriptor store list '{listName}' failed: {UuvrReflection.Describe(exception)}");
            }
        }

        return results;
    }

    private static List<object> QueryViaSubsystemManager()
    {
        var results = new List<object>();

        var subsystemManagerType = UuvrTypeFinder.FindType("UnityEngine.SubsystemManager");
        if (subsystemManagerType == null)
        {
            UuvrTrace.LogError("UnityEngine.SubsystemManager not found.");
            return results;
        }

        var getAll = subsystemManagerType.GetMethod(
            "GetAllSubsystemDescriptors", BindingFlags.Public | BindingFlags.Static);

        if (getAll == null || getAll.GetParameters().Length != 1)
        {
            UuvrTrace.Log("SubsystemManager.GetAllSubsystemDescriptors not available");
            return results;
        }

        try
        {
            UuvrTrace.Log("querying subsystem descriptors via GetAllSubsystemDescriptors");
            var listType = getAll.GetParameters()[0].ParameterType;
            var list = Activator.CreateInstance(listType);
            getAll.Invoke(null, new[] { list });
            EnumerateListInto(results, list!);
        }
        catch (Exception exception)
        {
            UuvrTrace.LogWarning($"GetAllSubsystemDescriptors failed: {UuvrReflection.Describe(exception)}");
        }

        return results;
    }

    private static List<object> QueryViaGenericGetDescriptors()
    {
        var results = new List<object>();

        var subsystemManagerType = UuvrTypeFinder.FindType("UnityEngine.SubsystemManager");
        var getDescriptors = subsystemManagerType?.GetMethod(
            "GetSubsystemDescriptors", BindingFlags.Public | BindingFlags.Static);

        if (getDescriptors == null || !getDescriptors.IsGenericMethodDefinition)
        {
            UuvrTrace.Log("SubsystemManager.GetSubsystemDescriptors<T> not available either");
            return results;
        }

        foreach (var descriptorTypeName in new[]
                 {
                     "UnityEngine.XR.XRDisplaySubsystemDescriptor",
                     "UnityEngine.XR.XRInputSubsystemDescriptor",
                 })
        {
            var descriptorType = UuvrTypeFinder.FindType(descriptorTypeName);
            if (descriptorType == null) continue;

            try
            {
                UuvrTrace.Log($"querying subsystem descriptors via GetSubsystemDescriptors<{descriptorType.Name}>");
                var method = getDescriptors.MakeGenericMethod(descriptorType);
                var listType = method.GetParameters()[0].ParameterType;
                var list = Activator.CreateInstance(listType);
                method.Invoke(null, new[] { list });
                EnumerateListInto(results, list!);
            }
            catch (Exception exception)
            {
                UuvrTrace.LogWarning($"GetSubsystemDescriptors<{descriptorType.Name}> failed: {UuvrReflection.Describe(exception)}");
            }
        }

        return results;
    }

    private static void EnumerateListInto(List<object> results, object list)
    {
        var listType = list.GetType();
        var count = listType.GetProperty("Count")?.GetValue(list, null) as int? ?? 0;
        var itemGetter = listType.GetMethod("get_Item");
        if (itemGetter == null) return;

        for (var index = 0; index < count; index++)
        {
            var item = itemGetter.Invoke(list, new object[] { index });
            if (item != null && !results.Contains(item)) results.Add(item);
        }
    }

    private static string GetDescriptorId(object descriptor)
    {
        var descriptorType = descriptor.GetType();

        try
        {
            if (descriptorType.GetProperty("id")?.GetValue(descriptor, null) is string id) return id;
        }
        catch (Exception)
        {
        }

        try
        {
            if (descriptorType.GetMethod("get_id", Type.EmptyTypes)?.Invoke(descriptor, null) is string id) return id;
        }
        catch (Exception)
        {
        }

        return "";
    }

    private static object? CreateSubsystem(List<object> descriptors, string descriptorTypeName, string subsystemTypeName, string description)
    {
        var descriptorType = UuvrTypeFinder.FindType(descriptorTypeName);
        if (descriptorType == null)
        {
            UuvrTrace.LogWarning($"{descriptorTypeName} not found in this game.");
            return null;
        }

        foreach (var descriptor in descriptors)
        {
            // Wrappers carry their declared type under IL2CPP, so the il2cpp side decides
            // whether this descriptor really is the wanted kind (see UuvrReflection.CastToType).
            var typedDescriptor = UuvrReflection.CastToType(descriptor, descriptorType);
            if (typedDescriptor == null) continue;

            var createMethod = FindCreateMethod(typedDescriptor.GetType());
            if (createMethod == null)
            {
                UuvrTrace.LogWarning(
                    $"{typedDescriptor.GetType().Name} has none of the known creation methods ({string.Join(", ", CreateMethodNames)}).");
                continue;
            }

            try
            {
                UuvrTrace.Log($"creating {description} subsystem from '{GetDescriptorId(typedDescriptor)}' via {createMethod.Name}");
                var subsystem = createMethod.Invoke(typedDescriptor, null);
                if (subsystem == null)
                {
                    UuvrTrace.LogWarning($"{createMethod.Name} returned null for the {description} subsystem.");
                    continue;
                }

                // Same wrapper-type dance for the created subsystem, so Start/Stop/running
                // resolve on the concrete type.
                var subsystemType = UuvrTypeFinder.FindType(subsystemTypeName);
                if (subsystemType != null)
                {
                    subsystem = UuvrReflection.CastToType(subsystem, subsystemType) ?? subsystem;
                }

                UuvrTrace.Log($"created {description} subsystem ({subsystem.GetType().Name})");
                return subsystem;
            }
            catch (Exception exception)
            {
                UuvrTrace.LogWarning($"failed to create {description} subsystem: {UuvrReflection.Describe(exception)}");
            }
        }

        UuvrTrace.Log($"no descriptor matched {descriptorType.Name}");
        return null;
    }

    private static MethodInfo? FindCreateMethod(Type descriptorType)
    {
        foreach (var methodName in CreateMethodNames)
        {
            try
            {
                var method = descriptorType.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (method != null) return method;
            }
            catch (Exception)
            {
                // Ambiguous or inaccessible; try the next name.
            }
        }

        return null;
    }

    protected override bool EnableVr()
    {
        if (_displaySubsystem == null) return false;

        if (!InvokeSubsystem(_displaySubsystem, "Start", "display")) return false;
        if (_inputSubsystem != null) InvokeSubsystem(_inputSubsystem, "Start", "input");

        var running = GetIsRunning(_displaySubsystem);
        var deviceActive = ReadXrSettingsFlag("isDeviceActive");
        UuvrTrace.Log($"display subsystem running={running}, XRSettings.isDeviceActive={deviceActive}");

        if (running == false)
        {
            UuvrTrace.LogError("the display subsystem didn't start. Is SteamVR (or your OpenXR runtime) running?");
            return false;
        }

        return true;
    }

    protected override bool DisableVr()
    {
        if (_displaySubsystem == null) return false;

        if (_inputSubsystem != null) InvokeSubsystem(_inputSubsystem, "Stop", "input");
        return InvokeSubsystem(_displaySubsystem, "Stop", "display");
    }

    private static bool InvokeSubsystem(object subsystem, string methodName, string description)
    {
        try
        {
            var method = subsystem.GetType().GetMethod(methodName, Type.EmptyTypes);
            if (method == null)
            {
                UuvrTrace.LogError($"{description} subsystem has no {methodName} method.");
                return false;
            }

            UuvrTrace.Log($"calling {methodName} on the {description} subsystem (if the game dies here, the XR runtime crashed it)");
            method.Invoke(subsystem, null);
            return true;
        }
        catch (Exception exception)
        {
            UuvrTrace.LogError($"{methodName} failed on the {description} subsystem: {UuvrReflection.Describe(exception)}");
            return false;
        }
    }

    private static bool? GetIsRunning(object subsystem)
    {
        try
        {
            return subsystem.GetType().GetProperty("running")?.GetValue(subsystem, null) as bool?;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? ReadXrSettingsFlag(string propertyName)
    {
        try
        {
            var xrSettingsType = UuvrTypeFinder.FindType("UnityEngine.XR.XRSettings");
            return xrSettingsType?.GetProperty(propertyName)?.GetValue(null, null) as bool?;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
