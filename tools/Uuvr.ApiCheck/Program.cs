// Verifies the member names UUVR's reflection relies on against a real game's assemblies.
// See README.md in this folder.

using System.Reflection;

if (args.Length < 1)
{
    Console.WriteLine("usage: uuvr-api-check <interop-folder> [bepinex-core-folder]");
    return 2;
}

var dlls = Directory.GetFiles(args[0], "*.dll");
var extra = args.Length > 1 && Directory.Exists(args[1]) ? Directory.GetFiles(args[1], "*.dll") : Array.Empty<string>();
var resolver = new PathAssemblyResolver(dlls.Concat(extra).Concat(
    Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location), "*.dll")).ToList());
using var mlc = new MetadataLoadContext(resolver);
Type Find(string n) { foreach (var d in dlls) { try { var t = mlc.LoadFromAssemblyPath(d).GetType(n); if (t != null) return t; } catch {} } return null; }

int fail = 0;
void Check(string what, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}{(detail.Length > 0 ? "  -> " + detail : "")}");
    if (!ok) fail++;
}
MethodInfo Method(Type t, string name) =>
    t?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy)
      .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 0);

// --- exactly the lookups SubsystemXrToggler performs ---
var mgr = Find("UnityEngine.SubsystemManager");
Check("SubsystemManager found", mgr != null);
var getAll = mgr?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "GetAllSubsystemDescriptors");
Check("GetAllSubsystemDescriptors(1 param)", getAll != null && getAll.GetParameters().Length == 1,
      getAll == null ? "missing" : getAll.GetParameters()[0].ParameterType.FullName);

// the list type comes from the parameter, as the toggler does
var listType = getAll?.GetParameters()[0].ParameterType;
Check("list type has parameterless ctor", listType?.GetConstructor(Type.EmptyTypes) != null);
Check("list type has Count", listType?.GetProperty("Count") != null);
Check("list type has get_Item", listType?.GetMethods().Any(m => m.Name == "get_Item") == true);
Check("list type has ToArray (fallback)", listType?.GetMethods().Any(m => m.Name == "ToArray") == true);

foreach (var dn in new[]{"UnityEngine.XR.XRDisplaySubsystemDescriptor", "UnityEngine.XR.XRInputSubsystemDescriptor"})
{
    var d = Find(dn);
    Check($"{dn.Split('.').Last()} found", d != null);
    var create = new[]{"CreateImpl","UnityEngine.ISubsystemDescriptor.Create","Create"}
        .Select(n => Method(d, n)).FirstOrDefault(m => m != null);
    Check($"  creation method resolves", create != null, create?.Name ?? "NONE");
    Check($"  id readable", d?.GetProperty("id") != null || Method(d, "get_id") != null,
          d?.GetProperty("id") != null ? "property id" : "get_id()");
}

foreach (var sn in new[]{"UnityEngine.XR.XRDisplaySubsystem", "UnityEngine.XR.XRInputSubsystem"})
{
    var s = Find(sn);
    Check($"{sn.Split('.').Last()} found", s != null);
    Check("  Start()", Method(s, "Start") != null);
    Check("  Stop()", Method(s, "Stop") != null);
    Check("  running", s?.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.FlattenHierarchy).Any(p => p.Name == "running") == true
                       || Method(s, "get_running") != null);
}

var xrSettings = Find("UnityEngine.XR.XRSettings");
Check("XRSettings found", xrSettings != null);
foreach (var p in new[]{"enabled","isDeviceActive","loadedDeviceName"})
    Check($"  XRSettings.{p}", xrSettings?.GetProperty(p) != null || Method(xrSettings, "get_" + p) != null);

// the 0.5.5 fallback path, expected absent in this game
Check("GetSubsystemDescriptors<T> absent (fallback unused here)",
      mgr?.GetMethods(BindingFlags.Public|BindingFlags.Static).Any(m => m.Name == "GetSubsystemDescriptors") != true);

Console.WriteLine(fail == 0 ? "\nALL PASS" : $"\n{fail} FAILURES");
return fail == 0 ? 0 : 1;
