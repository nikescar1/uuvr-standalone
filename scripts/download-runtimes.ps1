# Downloads the BepInEx runtimes that get bundled into the standalone package,
# into build/runtimes/<name>.zip. Used by CI and for local packaging.
#
# Each runtime also gets a <name>.version.txt recording what was actually downloaded.
# That matters because the IL2CPP runtime has a fallback path, and a silent fallback
# means shipping a runtime older than the one the mod was compiled against.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$runtimesDir = Join-Path $root "build/runtimes"
New-Item -ItemType Directory -Force -Path $runtimesDir | Out-Null

$monoVersion = "5.4.23.4"

# Keep in sync with the BepInEx.Unity.IL2CPP PackageReference in Uuvr/Uuvr.csproj.
$il2cppBuild = "692"
$il2cppCompiledAgainst = "6.0.0-be.$il2cppBuild"
$il2cppFallbackVersion = "6.0.0-pre.2"

function Write-RuntimeVersion([string]$name, [string]$version, [string]$url, [bool]$isFallback) {
    $lines = @(
        "runtime=$name",
        "version=$version",
        "url=$url",
        "fallback=$($isFallback.ToString().ToLower())"
    )
    if ($isFallback) {
        $lines += "compiled-against=$il2cppCompiledAgainst"
    }
    Set-Content -Path (Join-Path $runtimesDir "$name.version.txt") -Value $lines
}

foreach ($arch in "x64", "x86") {
    $name = "mono-$arch"
    $out = Join-Path $runtimesDir "$name.zip"
    $url = "https://github.com/BepInEx/BepInEx/releases/download/v$monoVersion/BepInEx_win_${arch}_$monoVersion.zip"
    if (-not (Test-Path $out)) {
        Write-Host "Downloading $url"
        Invoke-WebRequest $url -OutFile $out
    }
    Write-RuntimeVersion $name $monoVersion $url $false
}

# The IL2CPP flavor is compiled against BepInEx $il2cppCompiledAgainst, so that exact
# bleeding-edge build is what we want. Its artifact file names embed a commit hash, so the
# URL has to be discovered rather than constructed.
function Get-BleedingEdgeUrl([string]$arch) {
    $pages = @(
        "https://builds.bepinex.dev/projects/bepinex_be/$il2cppBuild",
        "https://builds.bepinex.dev/projects/bepinex_be"
    )

    foreach ($page in $pages) {
        try {
            $response = Invoke-WebRequest $page -UseBasicParsing
        }
        catch {
            Write-Host "  no listing at $page ($($_.Exception.Message))"
            continue
        }

        $link = $response.Links |
            Where-Object { $_.href -match "IL2CPP-win-$arch.*\.zip$" -and $_.href -match "be\.$il2cppBuild" } |
            Select-Object -First 1

        if (-not $link) {
            # Same build number, any artifact naming.
            $link = $response.Links |
                Where-Object { $_.href -match "IL2CPP-win-$arch.*be\.$il2cppBuild.*\.zip$" } |
                Select-Object -First 1
        }

        if ($link) {
            $href = $link.href
            if ($href -notmatch '^https?://') {
                $href = "https://builds.bepinex.dev" + $(if ($href.StartsWith("/")) { $href } else { "/$href" })
            }
            return $href
        }

        Write-Host "  listing at $page had no IL2CPP win-$arch artifact for build $il2cppBuild"
    }

    throw "could not discover a bleeding-edge IL2CPP win-$arch artifact for build $il2cppBuild"
}

$fellBack = $false

foreach ($arch in "x64", "x86") {
    $name = "il2cpp-$arch"
    $out = Join-Path $runtimesDir "$name.zip"
    if (Test-Path $out) { continue }

    $version = $il2cppCompiledAgainst
    $isFallback = $false

    try {
        $url = Get-BleedingEdgeUrl $arch
        Write-Host "Downloading $url"
        Invoke-WebRequest $url -OutFile $out
    }
    catch {
        $isFallback = $true
        $fellBack = $true
        $version = $il2cppFallbackVersion
        Write-Warning "Could not get BepInEx $il2cppCompiledAgainst for $arch ($($_.Exception.Message.Trim()))"
        Write-Warning "Falling back to the $il2cppFallbackVersion release, which is OLDER than what the IL2CPP mod is compiled against."
        $url = "https://github.com/BepInEx/BepInEx/releases/download/v$il2cppFallbackVersion/BepInEx-Unity.IL2CPP-win-$arch-$il2cppFallbackVersion.zip"
        Write-Host "Downloading $url"
        Invoke-WebRequest $url -OutFile $out
    }

    Write-RuntimeVersion $name $version $url $isFallback
}

Write-Host "Runtimes ready in $runtimesDir"

if ($fellBack) {
    Write-Host ""
    Write-Warning "=================================================================="
    Write-Warning " The bundled IL2CPP runtime is $il2cppFallbackVersion, but the mod is built"
    Write-Warning " against $il2cppCompiledAgainst. IL2CPP games will run on the older"
    Write-Warning " runtime unless the user already has BepInEx installed."
    Write-Warning " The loader reports this at install time."
    Write-Warning "=================================================================="
}
