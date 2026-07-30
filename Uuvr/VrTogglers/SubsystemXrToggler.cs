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
public class SubsystemXrToggler : VrToggler
{
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

        _displaySubsystem = CreateSubsystem(descriptors, "XRDisplaySubsystemDescriptor", "display");
        _inputSubsystem = CreateSubsystem(descriptors, "XRInputSubsystemDescriptor", "input");

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
        var results = new List<object>();

        var subsystemManagerType = UuvrTypeFinder.FindType("UnityEngine.SubsystemManager");
        if (subsystemManagerType == null)
        {
            UuvrTrace.LogError("UnityEngine.SubsystemManager not found.");
            return results;
        }

        // Prefer the non-generic call: taking the list type straight from the parameter
        // avoids having to construct a generic type by hand, which is fragile under IL2CPP.
        var getAll = subsystemManagerType.GetMethod(
            "GetAllSubsystemDescriptors", BindingFlags.Public | BindingFlags.Static);

        if (getAll != null && getAll.GetParameters().Length == 1)
        {
            UuvrTrace.Log("querying subsystem descriptors via GetAllSubsystemDescriptors");
            CollectInto(results, getAll, getAll.GetParameters()[0].ParameterType, null);
            if (results.Count > 0) return results;
        }

        // Otherwise use the generic form, once per descriptor type we care about.
        var getDescriptors = subsystemManagerType.GetMethod(
            "GetSubsystemDescriptors", BindingFlags.Public | BindingFlags.Static);

        if (getDescriptors == null || !getDescriptors.IsGenericMethodDefinition)
        {
            UuvrTrace.LogError("no usable descriptor query found on SubsystemManager.");
            return results;
        }

        foreach (var descriptorTypeName in new[]
                 {
                     "UnityEngine.XR.XRDisplaySubsystemDescriptor",
                     "UnityEngine.XR.XRInputSubsystemDescriptor",
                 })
        {
            var descriptorType = UuvrTypeFinder.FindType(descriptorTypeName);
            if (descriptorType == null)
            {
                UuvrTrace.LogWarning($"{descriptorTypeName} not found.");
                continue;
            }

            try
            {
                UuvrTrace.Log($"querying subsystem descriptors via GetSubsystemDescriptors<{descriptorType.Name}>");
                var method = getDescriptors.MakeGenericMethod(descriptorType);
                CollectInto(results, method, method.GetParameters()[0].ParameterType, null);
            }
            catch (Exception exception)
            {
                UuvrTrace.LogWarning($"GetSubsystemDescriptors<{descriptorType.Name}> failed: {exception.Message}");
            }
        }

        return results;
    }

    private static void CollectInto(List<object> results, MethodInfo method, Type listType, object? target)
    {
        try
        {
            var list = Activator.CreateInstance(listType);
            method.Invoke(target, new[] { list });

            var count = listType.GetProperty("Count")?.GetValue(list, null) as int? ?? 0;
            var itemGetter = listType.GetMethod("get_Item");

            for (var index = 0; index < count; index++)
            {
                var item = itemGetter?.Invoke(list, new object[] { index });
                if (item != null && !results.Contains(item)) results.Add(item);
            }
        }
        catch (Exception exception)
        {
            UuvrTrace.LogWarning($"{method.Name} failed: {exception.Message}");
        }
    }

    private static string GetDescriptorId(object descriptor)
    {
        try
        {
            return descriptor.GetType().GetProperty("id")?.GetValue(descriptor, null) as string ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static object? CreateSubsystem(List<object> descriptors, string descriptorTypeName, string description)
    {
        foreach (var descriptor in descriptors)
        {
            if (!IsOfType(descriptor.GetType(), descriptorTypeName)) continue;

            try
            {
                var createMethod = descriptor.GetType().GetMethod("Create");
                if (createMethod == null)
                {
                    UuvrTrace.LogWarning($"{descriptor.GetType().Name} has no Create method.");
                    continue;
                }

                UuvrTrace.Log($"creating {description} subsystem from '{GetDescriptorId(descriptor)}'");
                var subsystem = createMethod.Invoke(descriptor, null);
                if (subsystem == null) continue;

                UuvrTrace.Log($"created {description} subsystem");
                return subsystem;
            }
            catch (Exception exception)
            {
                UuvrTrace.LogWarning($"failed to create {description} subsystem: {exception.Message}");
            }
        }

        return null;
    }

    // Descriptors can be subclasses, so walk the hierarchy by name rather than
    // depending on types we can't reference at compile time.
    private static bool IsOfType(Type? type, string typeName)
    {
        while (type != null)
        {
            if (type.Name == typeName) return true;
            type = type.BaseType;
        }

        return false;
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
            var method = subsystem.GetType().GetMethod(methodName);
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
            UuvrTrace.LogError($"{methodName} failed on the {description} subsystem: {exception.Message}");
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
