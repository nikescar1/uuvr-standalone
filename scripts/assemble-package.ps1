# Stages the standalone release package in dist/UUVR-Standalone:
#   UuvrLoader.exe                 (published single file)
#   Payload/Mods/<flavor>/...      (built mod flavors from build/)
#   Payload/BepInEx/<runtime>/...  (extracted runtimes from build/runtimes/)
# Run after building the flavors, publishing the loader, and download-runtimes.ps1.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root "dist/UUVR-Standalone"

Remove-Item -Recurse -Force $stage -ErrorAction Ignore
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$loaderExe = Join-Path $root "Uuvr.Loader/bin/Release/net8.0-windows/win-x64/publish/UuvrLoader.exe"
if (-not (Test-Path $loaderExe)) { throw "Loader not published yet: $loaderExe" }
Copy-Item $loaderExe $stage

$modsDir = Join-Path $stage "Payload/Mods"
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null
foreach ($flavor in "uuvr-mono-legacy", "uuvr-mono-modern", "uuvr-il2cpp-legacy") {
    $source = Join-Path $root "build/$flavor"
    if (-not (Test-Path $source)) { throw "Mod flavor not built yet: $source" }
    Copy-Item -Recurse $source (Join-Path $modsDir $flavor)
}

$runtimesTarget = Join-Path $stage "Payload/BepInEx"
New-Item -ItemType Directory -Force -Path $runtimesTarget | Out-Null
foreach ($runtime in "mono-x64", "mono-x86", "il2cpp-x64", "il2cpp-x86") {
    $zip = Join-Path $root "build/runtimes/$runtime.zip"
    if (-not (Test-Path $zip)) { throw "Runtime not downloaded yet: $zip (run scripts/download-runtimes.ps1)" }
    $target = Join-Path $runtimesTarget $runtime
    Expand-Archive $zip -DestinationPath $target

    # Carry the version record into the payload so the loader can report which BepInEx
    # a game actually gets, including when the IL2CPP download fell back to an older one.
    $versionFile = Join-Path $root "build/runtimes/$runtime.version.txt"
    if (Test-Path $versionFile) {
        Copy-Item $versionFile (Join-Path $target "uuvr-runtime-version.txt")
    }
}

@"
UUVR — Universal Unity VR (standalone)

1. Run UuvrLoader.exe
2. Pick a game from the list (or drag its .exe onto the window)
3. Click "Install VR mod"
4. Start SteamVR (or your OpenXR runtime), then launch the game

In game:  F2 = UUVR menu   F3 = toggle VR   F4 = recenter view

To play flat again, use "Disable VR (play flat)" in the loader — no
reinstall needed. "Uninstall" removes everything the loader added.

Keep this folder structure intact: the loader needs the Payload folder.
"@ | Set-Content (Join-Path $stage "GETTING-STARTED.txt")

Write-Host "Package staged at $stage"
