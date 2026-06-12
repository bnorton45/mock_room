<#
.SYNOPSIS
    Build the MockRoom Windows installer from a NativeAOT win-x64 publish.

.DESCRIPTION
    NativeAOT cannot cross-compile, so this MUST run on Windows x64 with:
      - .NET 10 SDK
      - Desktop C++ build tools / MSVC (clang+link toolchain) for NativeAOT
      - Inno Setup 6 (ISCC.exe on PATH, or pass -Iscc)

    Steps: regenerate icons, publish AOT (win-x64), then compile mockroom.iss.

.PARAMETER NoPublish
    Reuse an existing publish output instead of rebuilding.

.PARAMETER Iscc
    Full path to ISCC.exe if it is not on PATH.

.EXAMPLE
    pwsh packaging/windows/build.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoPublish,
    [string]$Iscc = "ISCC.exe"
)
$ErrorActionPreference = "Stop"

$RepoRoot   = (Resolve-Path "$PSScriptRoot/../..").Path
$PkgDir     = Join-Path $RepoRoot "packaging"
$IconsDir   = Join-Path $PkgDir "icons"
$Project    = Join-Path $RepoRoot "src/MockRoom/MockRoom.csproj"
$Rid        = "win-x64"
$PublishDir = Join-Path $RepoRoot "src/MockRoom/bin/Release/net10.0/$Rid/publish"

# Version from the csproj <Version> element.
[xml]$csproj = Get-Content $Project
$Version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
if (-not $Version) { $Version = "1.0.0" }
Write-Host ">> MockRoom $Version ($Rid)"

if (-not (Test-Path (Join-Path $IconsDir "mockroom.ico"))) {
    Write-Host ">> generating icons"
    python (Join-Path $PkgDir "make-icons.py") $IconsDir
}

if (-not $NoPublish) {
    Write-Host ">> dotnet publish (NativeAOT, $Rid)"
    dotnet publish $Project -r $Rid -c Release -p:PublishAot=true
}
if (-not (Test-Path (Join-Path $PublishDir "MockRoom.exe"))) {
    throw "publish output not found at $PublishDir\MockRoom.exe"
}

Write-Host ">> compiling installer with Inno Setup"
& $Iscc "/DAppVersion=$Version" "/DPublishDir=$PublishDir" "/DIconsDir=$IconsDir" `
        (Join-Path $PSScriptRoot "mockroom.iss")
Write-Host ">> done — see packaging/dist/"
