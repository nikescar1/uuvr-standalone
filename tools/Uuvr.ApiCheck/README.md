# UUVR API check

UUVR reaches Unity's XR types by reflection, because the mod is built without
compile-time references to them. Reflection has no compiler to catch a wrong member
name, so a typo shows up as "VR doesn't work" in someone's game.

This tool checks the names UUVR looks up against the assemblies of a real game.

For an IL2CPP game, BepInEx generates those assemblies into `BepInEx/interop` on first
launch. Point the tool at that folder:

    dotnet run --project tools/Uuvr.ApiCheck -- "<game>/BepInEx/interop"

It needs `UnityEngine.SubsystemsModule.dll`, `UnityEngine.XRModule.dll`,
`UnityEngine.CoreModule.dll` and `Il2Cppmscorlib.dll`. Il2CppInterop's own
`Il2CppInterop.Runtime.dll` (from `BepInEx/core`) is used to resolve base types when
present; pass its folder as a second argument.

Every reported name corresponds to a lookup in `Uuvr/VrTogglers/SubsystemXrToggler.cs`.
A FAIL means that game needs a different name, not that the game is unsupported.
