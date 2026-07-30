using System;
using System.Collections.Generic;
using System.Reflection;
#if CPP
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
#endif

namespace Uuvr.VrTogglers;

// Starts VR through Unity's XR subsystem API, without needing any XR package.
//
// Unity's XR Plugin Management package is only a managed wrapper: it looks up subsystem
// descriptors registered by the native XR plugins, creates the display and input subsystems,
// and starts them. Games built without the XR packages have none of that managed code, but the
// subsystem API itself lives in the engine (UnityEngine.XRModule / SubsystemsModule) and is
// always there, and UUVR installs the native plugin manifests that register the descriptors.
//
// A wrinkle on IL2CPP: games that never touch the subsystem API from their own code get the
// managed wrapper bodies stripped by the linker, so calling CreateImpl/Start through interop
// fails with 'Method unstripping failed'. The engine functions behind those wrappers live in
// the prebuilt UnityPlayer binary though, and icalls resolve regardless of managed stripping.
// So on IL2CPP we call the engine directly:
//
//   descriptor.m_Ptr  ->  SubsystemDescriptorBindings::Create  ->  the engine registers a
//   managed twin in SubsystemManager.s_IntegratedSubsystems  ->  IntegratedSubsystem::Start.
//
// Field reads (m_Ptr, the descriptor store lists) always work under interop — only method
// bodies get stripped — which is what makes this route dependable.
//
// Member names and icall signatures verified against the interop assemblies of a real
// Unity 2021.3.16 IL2CPP game (tools/Uuvr.ApiCheck) and UnityCsReference 2021.3.16f1
// (Modules/Subsystems/IntegratedSubsystem.bindings.cs).
public class SubsystemXrToggler : VrToggler
{
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

        _displaySubsystem = CreateSubsystem(
            descriptors, "UnityEngine.XR.XRDisplaySubsystemDescriptor", "UnityEngine.XR.XRDisplaySubsystem", "display", null);

        if (_displaySubsystem == null)
        {
            UuvrTrace.LogError("couldn't create an XR display subsystem, so VR can't be started.");
            return false;
        }

        // Keep display and input on the same runtime (e.g. both OpenXR), so we don't end up
        // rendering through one API while tracking through another.
        var runtimePrefix = GetRuntimePrefix(_displaySubsystemDescriptorId);

        _inputSubsystem = CreateSubsystem(
            descriptors, "UnityEngine.XR.XRInputSubsystemDescriptor", "UnityEngine.XR.XRInputSubsystem", "input", runtimePrefix);

        // Input is nice to have; without it there's no head tracking from this path,
        // but UUVR reads poses through InputTracking anyway.
        if (_inputSubsystem == null)
        {
            UuvrTrace.LogWarning("no XR input subsystem was created; tracking may not work.");
        }

        return true;
    }

    private string _displaySubsystemDescriptorId = "";

    private static string? GetRuntimePrefix(string descriptorId)
    {
        if (string.IsNullOrEmpty(descriptorId)) return null;
        var spaceIndex = descriptorId.IndexOf(' ');
        return spaceIndex > 0 ? descriptorId.Substring(0, spaceIndex) : null;
    }

    private List<object> GetSubsystemDescriptors()
    {
        // Route 1: read the descriptor registry's static lists directly (field reads, immune
        // to stripping). Route 2 and 3 are managed queries for engines without the store.
        var results = ReadDescriptorStoreLists();
        if (results.Count > 0) return results;

        results = QueryViaSubsystemManager();
        if (results.Count > 0) return results;

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

    private object? CreateSubsystem(
        List<object> descriptors, string descriptorTypeName, string subsystemTypeName, string description, string? preferredRuntimePrefix)
    {
        var descriptorType = UuvrTypeFinder.FindType(descriptorTypeName);
        if (descriptorType == null)
        {
            UuvrTrace.LogWarning($"{descriptorTypeName} not found in this game.");
            return null;
        }

        // Wrappers carry their declared type under IL2CPP, so the il2cpp side decides
        // whether each descriptor really is the wanted kind (see UuvrReflection.CastToType).
        var matched = new List<object>();
        foreach (var descriptor in descriptors)
        {
            var typedDescriptor = UuvrReflection.CastToType(descriptor, descriptorType);
            if (typedDescriptor != null) matched.Add(typedDescriptor);
        }

        if (matched.Count == 0)
        {
            UuvrTrace.Log($"no descriptor matched {descriptorType.Name}");
            return null;
        }

        // Order of attempts matters a lot here. OpenVR's native plugin fully initializes
        // itself when its subsystem is created and fails with an error code when it can't.
        // Unity's OpenXR native plugin instead relies on its managed package having set up
        // the OpenXR session first, and hard-crashes the game when driven without it — so
        // OpenXR goes last, and is skipped entirely while other runtimes are available.
        var hasNonOpenXr = false;
        foreach (var descriptor in matched)
        {
            if (!GetDescriptorId(descriptor).StartsWith("OpenXR")) hasNonOpenXr = true;
        }

        matched.Sort((left, right) => ScoreDescriptor(right, preferredRuntimePrefix) - ScoreDescriptor(left, preferredRuntimePrefix));

        foreach (var descriptor in matched)
        {
            var descriptorId = GetDescriptorId(descriptor);

#if CPP
            if (descriptorId.StartsWith("OpenXR") && hasNonOpenXr)
            {
                UuvrTrace.Log(
                    $"skipping native creation from '{descriptorId}': Unity's OpenXR plugin crashes without its managed package, and other runtimes are available");
            }
            else
            {
                if (descriptorId.StartsWith("OpenXR"))
                {
                    UuvrTrace.LogWarning(
                        "only OpenXR descriptors are available; trying it natively even though Unity's OpenXR plugin is known to crash without its managed package");
                }

                var nativeResult = CreateSubsystemNatively(descriptor, subsystemTypeName, description);
                if (nativeResult != null)
                {
                    if (description == "display") _displaySubsystemDescriptorId = descriptorId;
                    return nativeResult;
                }
            }
#endif

            var managedResult = CreateSubsystemManaged(descriptor, subsystemTypeName, description);
            if (managedResult != null)
            {
                if (description == "display") _displaySubsystemDescriptorId = descriptorId;
                return managedResult;
            }
        }

        return null;
    }

    private static int ScoreDescriptor(object descriptor, string? preferredRuntimePrefix)
    {
        var descriptorId = GetDescriptorId(descriptor);
        var score = 0;

        // Same runtime as the display subsystem beats everything else.
        if (preferredRuntimePrefix != null && descriptorId.StartsWith(preferredRuntimePrefix)) score += 400;

        if (descriptorId.StartsWith("OpenVR")) score += 200;
        else if (!descriptorId.StartsWith("OpenXR")) score += 100;

        return score;
    }

    private static object? CreateSubsystemManaged(object descriptor, string subsystemTypeName, string description)
    {
        var createMethod = FindCreateMethod(descriptor.GetType());
        if (createMethod == null)
        {
            UuvrTrace.LogWarning(
                $"{descriptor.GetType().Name} has none of the known creation methods ({string.Join(", ", CreateMethodNames)}).");
            return null;
        }

        try
        {
            UuvrTrace.Log($"creating {description} subsystem from '{GetDescriptorId(descriptor)}' via {createMethod.Name}");
            var subsystem = createMethod.Invoke(descriptor, null);
            if (subsystem == null)
            {
                UuvrTrace.LogWarning($"{createMethod.Name} returned null for the {description} subsystem.");
                return null;
            }

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
            return null;
        }
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

#if CPP
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CreateSubsystemDelegate(IntPtr descriptorPtr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SubsystemActionDelegate(IntPtr subsystemObjectPtr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SubsystemQueryDelegate(IntPtr subsystemObjectPtr);

    private static bool _icallsResolved;
    private static CreateSubsystemDelegate? _createSubsystemIcall;
    private static SubsystemActionDelegate? _startSubsystemIcall;
    private static SubsystemActionDelegate? _stopSubsystemIcall;
    private static SubsystemQueryDelegate? _isRunningIcall;

    private static void ResolveIcalls()
    {
        if (_icallsResolved) return;
        _icallsResolved = true;

        _createSubsystemIcall = ResolveIcall<CreateSubsystemDelegate>("UnityEngine.SubsystemDescriptorBindings::Create");
        _startSubsystemIcall = ResolveIcall<SubsystemActionDelegate>("UnityEngine.IntegratedSubsystem::Start");
        _stopSubsystemIcall = ResolveIcall<SubsystemActionDelegate>("UnityEngine.IntegratedSubsystem::Stop");
        _isRunningIcall = ResolveIcall<SubsystemQueryDelegate>("UnityEngine.IntegratedSubsystem::IsRunning");
    }

    private static TDelegate? ResolveIcall<TDelegate>(string name) where TDelegate : Delegate
    {
        try
        {
            var pointer = IL2CPP.il2cpp_resolve_icall(name);
            if (pointer == IntPtr.Zero)
            {
                UuvrTrace.LogWarning($"engine icall not found: {name}");
                return null;
            }

            UuvrTrace.Log($"resolved engine icall {name}");
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(pointer);
        }
        catch (Exception exception)
        {
            UuvrTrace.LogWarning($"failed to resolve icall {name}: {UuvrReflection.Describe(exception)}");
            return null;
        }
    }

    private static object? CreateSubsystemNatively(object descriptor, string subsystemTypeName, string description)
    {
        ResolveIcalls();
        if (_createSubsystemIcall == null || _startSubsystemIcall == null) return null;

        var descriptorPtr = GetIntPtrMember(descriptor, "m_Ptr");
        if (descriptorPtr == IntPtr.Zero)
        {
            UuvrTrace.LogWarning($"couldn't read m_Ptr from {descriptor.GetType().Name}");
            return null;
        }

        UuvrTrace.Log(
            $"creating {description} subsystem natively from '{GetDescriptorId(descriptor)}' (if the game dies here, the XR runtime crashed it)");
        var subsystemPtr = _createSubsystemIcall(descriptorPtr);
        if (subsystemPtr == IntPtr.Zero)
        {
            UuvrTrace.LogWarning(
                $"native create returned null for '{GetDescriptorId(descriptor)}' — that runtime probably isn't active");
            return null;
        }

        // Creating the native subsystem makes the engine register a managed twin
        // (UsedByNativeCode, so it survives stripping); find it to drive Start/Stop.
        var managedSubsystem = FindManagedSubsystemByPtr(subsystemPtr);
        if (managedSubsystem == null)
        {
            UuvrTrace.LogError(
                $"the {description} subsystem was created natively, but its managed twin wasn't found in SubsystemManager.");
            return null;
        }

        var subsystemType = UuvrTypeFinder.FindType(subsystemTypeName);
        if (subsystemType != null)
        {
            managedSubsystem = UuvrReflection.CastToType(managedSubsystem, subsystemType) ?? managedSubsystem;
        }

        UuvrTrace.Log($"created {description} subsystem natively ({managedSubsystem.GetType().Name})");
        return managedSubsystem;
    }

    private static object? FindManagedSubsystemByPtr(IntPtr subsystemPtr)
    {
        try
        {
            var managerType = UuvrTypeFinder.FindType("UnityEngine.SubsystemManager");
            var listProperty = managerType?.GetProperty(
                "s_IntegratedSubsystems", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var list = listProperty?.GetValue(null, null);
            if (list == null) return null;

            var items = new List<object>();
            EnumerateListInto(items, list);

            foreach (var item in items)
            {
                if (GetIntPtrMember(item, "m_Ptr") == subsystemPtr) return item;
            }
        }
        catch (Exception exception)
        {
            UuvrTrace.LogWarning($"couldn't search SubsystemManager for the managed subsystem: {UuvrReflection.Describe(exception)}");
        }

        return null;
    }

    private static IntPtr GetIntPtrMember(object instance, string name)
    {
        try
        {
            var property = instance.GetType().GetProperty(
                name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property?.GetValue(instance, null) is IntPtr value) return value;
        }
        catch (Exception)
        {
        }

        return IntPtr.Zero;
    }

    // The il2cpp object pointer of the wrapper — what instance icalls take as 'this'.
    private static IntPtr GetObjectPointer(object wrapper)
    {
        try
        {
            var property = wrapper.GetType().GetProperty("Pointer", BindingFlags.Public | BindingFlags.Instance);
            if (property?.GetValue(wrapper, null) is IntPtr value) return value;
        }
        catch (Exception)
        {
        }

        return IntPtr.Zero;
    }
#endif

    protected override bool EnableVr()
    {
        if (_displaySubsystem == null) return false;

        if (!StartSubsystemUnified(_displaySubsystem, "display")) return false;
        if (_inputSubsystem != null) StartSubsystemUnified(_inputSubsystem, "input");

        var running = GetIsRunningUnified(_displaySubsystem);
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

        if (_inputSubsystem != null) StopSubsystemUnified(_inputSubsystem, "input");
        return StopSubsystemUnified(_displaySubsystem, "display");
    }

    private static bool StartSubsystemUnified(object subsystem, string description)
    {
#if CPP
        if (_startSubsystemIcall != null)
        {
            var objectPtr = GetObjectPointer(subsystem);
            if (objectPtr != IntPtr.Zero)
            {
                UuvrTrace.Log($"calling native Start on the {description} subsystem (if the game dies here, the XR runtime crashed it)");
                try
                {
                    _startSubsystemIcall(objectPtr);
                    return true;
                }
                catch (Exception exception)
                {
                    UuvrTrace.LogError($"native Start failed on the {description} subsystem: {UuvrReflection.Describe(exception)}");
                    return false;
                }
            }
        }
#endif
        return InvokeSubsystem(subsystem, "Start", description);
    }

    private static bool StopSubsystemUnified(object subsystem, string description)
    {
#if CPP
        if (_stopSubsystemIcall != null)
        {
            var objectPtr = GetObjectPointer(subsystem);
            if (objectPtr != IntPtr.Zero)
            {
                try
                {
                    _stopSubsystemIcall(objectPtr);
                    return true;
                }
                catch (Exception exception)
                {
                    UuvrTrace.LogError($"native Stop failed on the {description} subsystem: {UuvrReflection.Describe(exception)}");
                    return false;
                }
            }
        }
#endif
        return InvokeSubsystem(subsystem, "Stop", description);
    }

    private static bool? GetIsRunningUnified(object subsystem)
    {
#if CPP
        if (_isRunningIcall != null)
        {
            var objectPtr = GetObjectPointer(subsystem);
            if (objectPtr != IntPtr.Zero)
            {
                try
                {
                    return _isRunningIcall(objectPtr);
                }
                catch (Exception)
                {
                }
            }
        }
#endif
        return GetIsRunningManaged(subsystem);
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

    private static bool? GetIsRunningManaged(object subsystem)
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
