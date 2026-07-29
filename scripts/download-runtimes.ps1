# Downloads the BepInEx runtimes that get bundled into the standalone package,
# into build/runtimes/<name>.zip. Used by CI and for local packaging.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$runtimesDir = Join-Path $root "build/runtimes"
New-Item -ItemType Directory -Force -Path $runtimesDir | Out-Null

$monoVersion = "5.4.23.4"

foreach ($arch in "x64", "x86") {
    $out = Join-Path $runtimesDir "mono-$arch.zip"
    if (Test-Path $out) { continue }
    $url = "https://github.com/BepInEx/BepInEx/releases/download/v$monoVersion/BepInEx_win_${arch}_$monoVersion.zip"
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $out
}

# The IL2CPP flavor is compiled against BepInEx 6.0.0-be.692, so prefer that exact
# bleeding-edge build; the artifact file names contain a commit hash, so we scrape
# the build page for the real link. Falls back to the 6.0.0-pre.2 GitHub release.
$bleedingEdgeBuild = "692"

function Get-BleedingEdgeUrl([string]$arch) {
    $page = Invoke-WebRequest "https://builds.bepinex.dev/projects/bepinex_be/$bleedingEdgeBuild" -UseBasicParsing
    $link = $page.Links | Where-Object { $_.href -match "IL2CPP-win-$arch.*\.zip$" } | Select-Object -First 1
    if (-not $link) { throw "No IL2CPP win-$arch artifact found on build page" }
    $href = $link.href
    if ($href -notmatch '^https?://') { $href = "https://builds.bepinex.dev$href" }
    return $href
}

foreach ($arch in "x64", "x86") {
    $out = Join-Path $runtimesDir "il2cpp-$arch.zip"
    if (Test-Path $out) { continue }
    try {
        $url = Get-BleedingEdgeUrl $arch
        Write-Host "Downloading $url"
        Invoke-WebRequest $url -OutFile $out
    }
    catch {
        Write-Warning "Bleeding-edge download failed ($_); falling back to 6.0.0-pre.2"
        $url = "https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.2/BepInEx-Unity.IL2CPP-win-$arch-6.0.0-pre.2.zip"
        Write-Host "Downloading $url"
        Invoke-WebRequest $url -OutFile $out
    }
}

Write-Host "Runtimes ready in $runtimesDir"
