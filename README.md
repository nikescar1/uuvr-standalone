# UUVR — Universal Unity VR (standalone)

[![Raicuparta's VR mods](https://raicuparta.com/img/badge.svg)](https://raicuparta.com)

Adds VR support to flat (non-VR) Unity games — now fully standalone, in the spirit of
[UEVR](https://uevr.io) for Unreal Engine. No Rai Pal or manual BepInEx setup required:
one download, pick a game, click install, play.

Based on [Raicuparta's UUVR](https://github.com/Raicuparta/uuvr).

## How it works

Unlike UEVR, which injects into a running Unreal process, Unity decides its VR device
list while the engine boots (older Unity versions literally bake it into
`globalgamemanagers`). So instead of attaching to a live process, the **UUVR Loader**
installs a mod loader ([BepInEx](https://github.com/BepInEx/BepInEx)) plus the matching
UUVR build into the game folder once, and VR then kicks in automatically every time the
game starts. Everything the loader adds is tracked and fully reversible.

## Getting started

1. Download `UUVR-Standalone.zip` from the releases page and extract it anywhere
   (keep the folder structure — the loader needs the `Payload` folder next to it).
2. Run `UuvrLoader.exe`.
   - Steam games are detected automatically.
   - For anything else, click **Add game…** or drag the game's `.exe` onto the window.
3. Select the game and click **Install VR mod**. The loader detects the Unity version,
   scripting backend (Mono/IL2CPP) and architecture, and installs the right flavor.
4. Start SteamVR (or your OpenXR runtime), then launch the game.

### In game

| Key | Action |
| --- | ------ |
| `F2` | Open/close the UUVR settings menu |
| `F3` | Toggle VR on/off |
| `F4` | Recenter the VR view |
| `F5` | Cycle camera tracking mode |
| `F6` | Cycle UI patch mode |
| `F7` | Toggle depth override |
| `F8` | Write a camera report to `uuvr-trace.log` |
| `F9` | Cycle which camera VR looks through |

`F5`–`F9` exist because the menu needs Unity's IMGUI, which some IL2CPP games strip out
entirely. They reach the settings that decide whether a game renders at all, and write the
value they changed to `uuvr-trace.log`.

`F9` is the escape hatch for games that render through several cameras and UUVR picks the
wrong one. It steps through the cameras the game is currently drawing with — starting on
automatic, and returning to it one press past the last camera — and remembers the choice per
scene, since a game's menu and its gameplay usually need different answers. `F8` lists every
camera and what UUVR decided to do with each, which is the fastest way to see why a game
shows nothing.

The in-game menu lets you tweak everything live: camera tracking mode, world scale,
near clip, camera offsets, UI patch modes, camera filters, hotkeys, and more. Settings
are saved to the game's BepInEx config and can also be edited there directly
(`BepInEx/config/raicuparta.uuvr-*.cfg`).

With the default *Mirror* UI mode, the menu (and the game's flat UI) is projected onto a
screen inside the headset, so you can use the menu with the headset on.

### Playing flat again

You don't need to uninstall to play without VR:

- **In game:** press `F3` to toggle VR off.
- **From the loader:** click **Disable VR (play flat)** — the game launches completely
  stock until you enable it again. This also restores the original global settings file
  for older games.
- **Uninstall** removes exactly the files the loader added (tracked in a manifest) and
  restores any patched game files from backup.

### Command line

Everything the loader does is scriptable:

```
UuvrLoader detect    <game.exe>
UuvrLoader install   <game.exe> [--generation legacy|modern]
UuvrLoader uninstall <game.exe>
UuvrLoader enable    <game.exe>
UuvrLoader disable   <game.exe>
UuvrLoader launch    <game.exe>
```

## Game compatibility

Support depends on the game's Unity version and scripting backend:

| Flavor | Unity | Backend | Status |
| ------ | ----- | ------- | ------ |
| `uuvr-mono-legacy` | ≤ 2019 | Mono | Best supported |
| `uuvr-mono-modern` | ≥ 2020 | Mono | Well supported (OpenXR or OpenVR) |
| `uuvr-il2cpp-legacy` | ≤ 2019 | IL2CPP | Supported |
| modern IL2CPP | ≥ 2020 | IL2CPP | Experimental — the legacy IL2CPP build is used, starting VR through Unity's XR subsystem API by reflection (see below) |

### How VR gets started

Unity has no single way to turn VR on, so UUVR tries these in order and logs which it picked:

1. **XR Plugin Management** — for games that ship Unity's XR packages.
2. **XR subsystems** — for modern games built *without* the XR packages. XR Plugin
   Management is only a wrapper around this engine-level API, so UUVR does what the
   package would have done: look up the subsystem descriptors registered by the native
   XR plugins it installs, create the display and input subsystems, and start them.
3. **Legacy built-in VR** — Unity 2019 and older only; removed from the engine in 2020.

`VR Startup Method` in the config forces one of these if the automatic choice picks badly.

Tips when a game misbehaves:

- **VR works but everything is a flat, empty colour** (the view still moves with your
  head): the scene is being culled away. Change **Camera Tracking Mode** to `Child`, with
  `F5` or in the menu. `Relative Matrix` overrides the camera's view matrix, which breaks
  culling in some games — modern Unity games especially. Modern games now default to
  `Child`, and configs written by an older UUVR are moved over automatically unless you
  picked a mode yourself.
- **VR runs but the game looks untouched, or the wrong view is in the headset**: press `F8`
  and look at the camera report in `uuvr-trace.log`. It lists every camera and says whether
  UUVR took it over or why it passed on it. If it picked the wrong one, cycle with `F9`.
- Try a different **Camera Tracking Mode** in the menu (some games need `Child`,
  others `Relative Matrix`/`Relative Transform`).
- If the image is black or missing, enable **Override Depth** and raise the value.
- If VR grabs the wrong camera (minimap, portrait), use the **Camera Filters** section.
- If close objects get cut off, enable **Override Near Clip**.
- IL2CPP games take a few minutes on first launch while BepInEx generates interop
  assemblies — that's normal.
- If VR never turns on, open `BepInEx/plugins/UUVR/uuvr-trace.log` — it records every VR
  startup step and survives a hard crash, so its last line is where things went wrong.
  `BepInEx/LogOutput.log` has the same `UUVR:` lines mixed in with everything else.
- **If the game crashes or hangs on startup**, VR is being started before the game is ready.
  In `BepInEx/config/raicuparta.uuvr-*.cfg` raise `VR Start Delay`, or set
  `Start VR Automatically = false` and press the toggle VR key (F3) once you're in-game.

## Building from source

```
dotnet build Uuvr/Uuvr.csproj -c legacy-mono          # also: modern-mono, legacy-il2cpp
dotnet build Uuvr.Patcher/Uuvr.Patcher.csproj -c legacy-mono
dotnet publish Uuvr.Loader/Uuvr.Loader.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
pwsh scripts/download-runtimes.ps1
pwsh scripts/assemble-package.ps1                     # stages dist/UUVR-Standalone
```

Mod flavors build into the repo-local `build/` folder. Setting the `BepInExDir`
environment variable redirects output into a Rai Pal / BepInEx installation instead
(the old development workflow). The IL2CPP flavors restore packages from the BepInEx
NuGet feed (see `nuget.config`). GitHub Actions builds and packages everything on
every push (`.github/workflows/build.yml`).

## License

    UUVR
    Copyright (C) 2024  Raicuparta

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
