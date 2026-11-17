<#
Builds the client release zip: the plugin plus the installer, laid out so it can be extracted anywhere and installed by
double-clicking Install.bat, or copied straight into the game folder for a manual install.

  powershell -ExecutionPolicy Bypass -File installer\Package-Client.ps1 -PluginDir <folder with the three DLLs> -Version 1.3.0 -OutDir <folder>
#>
param(
    [Parameter(Mandatory)][string]$PluginDir,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutDir,
    # Only for a release that changes the installer or docs and ships the plugin DLL unchanged (for example 1.6.1 around the
    # 1.6.0 DLL). The DLL may then report an OLDER version than the release; a DLL that is NEWER, or from an unrelated version
    # line, is still refused. Without this switch the DLL must report the version being packaged.
    [string]$AllowDllVersion
)
$ErrorActionPreference = 'Stop'
$required = 'EmberKnights.Archipelago.dll', 'Archipelago.MultiClient.Net.dll', 'Newtonsoft.Json.dll'
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $PluginDir $file))) { throw "Missing $file in $PluginDir" }
}

$dllVersion = (Get-Item -LiteralPath (Join-Path $PluginDir 'EmberKnights.Archipelago.dll')).VersionInfo.ProductVersion
if ($AllowDllVersion) {
    # The caller states exactly which DLL version is expected, so a wrong DLL cannot slip through under the switch.
    if ($dllVersion -notlike "$AllowDllVersion*") { throw "The plugin DLL reports version $dllVersion but -AllowDllVersion says it should be $AllowDllVersion." }
    if ([version]($AllowDllVersion -replace '[^\d.].*$', '') -gt [version]($Version -replace '[^\d.].*$', '')) {
        throw "-AllowDllVersion $AllowDllVersion is newer than the release $Version; that is not an installer-only release."
    }
    Write-Host "Packaging release $Version around the unchanged plugin DLL $dllVersion."
}
elseif ($dllVersion -notlike "$Version*") { throw "The plugin DLL reports version $dllVersion but you asked to package $Version." }

$stage = Join-Path ([IO.Path]::GetTempPath()) ("ek-package-" + [Guid]::NewGuid().ToString('N'))
$pluginTarget = Join-Path $stage 'BepInEx\plugins\EmberKnights.Archipelago'
New-Item -ItemType Directory -Force -Path $pluginTarget | Out-Null
try {
    foreach ($file in $required) { Copy-Item -LiteralPath (Join-Path $PluginDir $file) -Destination $pluginTarget }
    foreach ($file in 'Install.bat', 'Uninstall.bat', 'Install-EmberKnightsArchipelago.ps1', 'README.txt') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage
    }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $zip = Join-Path $OutDir "EmberKnights.Archipelago-v$Version.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Wrote $zip"
    Write-Host "SHA-256 $hash"
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
