param(
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $PSScriptRoot "tools\dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

$csproj = Join-Path $PSScriptRoot "src\EmberKnights.Archipelago\EmberKnights.Archipelago.csproj"
& $dotnet build $csproj --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "C# build failed."
}

$buildRoot = Join-Path $PSScriptRoot "build"
$pluginOutput = Join-Path $buildRoot "BepInEx\plugins\EmberKnights.Archipelago"
$compiledOutput = Join-Path $PSScriptRoot "src\EmberKnights.Archipelago\bin\Release\net6.0"
New-Item -ItemType Directory -Path $pluginOutput -Force | Out-Null

$clientFiles = @(
    "EmberKnights.Archipelago.dll",
    "EmberKnights.Archipelago.pdb",
    "Archipelago.MultiClient.Net.dll",
    "Newtonsoft.Json.dll"
)
foreach ($file in $clientFiles) {
    $source = Join-Path $compiledOutput $file
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $pluginOutput $file) -Force
    }
    elseif ($file -notlike "*.pdb") {
        throw "Required client dependency is missing: $file"
    }
}

$python = Join-Path $PSScriptRoot "tools\python-ap\Scripts\python.exe"
$archipelagoRoot = Join-Path $PSScriptRoot "tools\archipelago-0.6.8\Archipelago-0.6.8"
$apworldPath = Join-Path $buildRoot "ember_knights.apworld"
$env:SKIP_REQUIREMENTS_UPDATE = "true"
$env:PYTHONDONTWRITEBYTECODE = "1"
$env:PYTHONWARNINGS = "ignore"
# Archipelago prints benign warnings (for example the optional _speedups notice) to stderr.
# Windows PowerShell turns native stderr into errors under "Stop", so judge success by exit code only.
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & $python (Join-Path $PSScriptRoot "tools\package_apworld.py") $archipelagoRoot (Join-Path $PSScriptRoot "apworld\ember_knights") $apworldPath 2>&1 |
        Where-Object { "$_" -match "^Packaged|Error|Traceback|must not" } |
        ForEach-Object { Write-Host $_ }
    $packagingExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousPreference
}
if ($packagingExitCode -ne 0) {
    throw "APWorld packaging failed (exit code $packagingExitCode)."
}

if ($Install) {
    $loaderMarker = Join-Path $projectRoot "winhttp.dll"
    if (-not (Test-Path -LiteralPath $loaderMarker)) {
        Copy-Item -Path (Join-Path $PSScriptRoot "tools\bepinex\*") -Destination $projectRoot -Recurse -Force
    }
    $unityLibTarget = Join-Path $projectRoot "BepInEx\unity-libs"
    if (-not (Test-Path -LiteralPath $unityLibTarget)) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "tools\interop\unity-libs") -Destination (Join-Path $projectRoot "BepInEx") -Recurse -Force
    }
    Copy-Item -Path (Join-Path $buildRoot "BepInEx\*") -Destination (Join-Path $projectRoot "BepInEx") -Recurse -Force
    Write-Host "Installed the plugin into $projectRoot"
}

Write-Host "Plugin package: $pluginOutput"
Write-Host "APWorld: $apworldPath"
