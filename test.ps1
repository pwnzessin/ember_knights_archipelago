$ErrorActionPreference = "Stop"
$python = Join-Path $PSScriptRoot "tools\python-ap\Scripts\python.exe"
$worldSource = Join-Path $PSScriptRoot "apworld\ember_knights"
$apworld = Join-Path $PSScriptRoot "build\ember_knights.apworld"

# Archipelago 0.6.8 is the released launcher version; main is the newest development tree.
$roots = @(
    (Join-Path $PSScriptRoot "tools\archipelago-0.6.8\Archipelago-0.6.8"),
    (Join-Path $PSScriptRoot "tools\archipelago-source\Archipelago-main")
)

if (-not (Test-Path -LiteralPath $python)) {
    throw "The isolated Python environment is missing."
}

$env:AP_TEST_WORLDS = "ember_knights"
$env:PYTHONDONTWRITEBYTECODE = "1"
$env:SKIP_REQUIREMENTS_UPDATE = "true"

foreach ($archipelagoRoot in $roots) {
    if (-not (Test-Path -LiteralPath $archipelagoRoot)) {
        throw "The Archipelago validation source is missing: $archipelagoRoot"
    }

    Write-Host "=== Folder-world tests against $(Split-Path -Leaf $archipelagoRoot) ==="
    Copy-Item -LiteralPath $worldSource -Destination (Join-Path $archipelagoRoot "worlds") -Recurse -Force
    Push-Location $archipelagoRoot
    try {
        & $python -m pytest "worlds/ember_knights/test" "test/general" --maxfail=1
        if ($LASTEXITCODE -ne 0) {
            throw "Archipelago world tests failed against $archipelagoRoot."
        }
    }
    finally {
        Pop-Location
    }
}

# Load the packaged .apworld exactly as the launcher does: a zip with no extracted files.
if (-not (Test-Path -LiteralPath $apworld)) {
    throw "Build the .apworld first: ArchipelagoMod\build.ps1"
}
foreach ($archipelagoRoot in $roots) {
    Write-Host "=== Packaged .apworld load test against $(Split-Path -Leaf $archipelagoRoot) ==="
    $worldCopy = Join-Path $archipelagoRoot "worlds\ember_knights"
    if (Test-Path -LiteralPath $worldCopy) {
        Remove-Item -LiteralPath $worldCopy -Recurse -Force
    }
    $customWorlds = Join-Path $archipelagoRoot "custom_worlds"
    New-Item -ItemType Directory -Path $customWorlds -Force | Out-Null
    Copy-Item -LiteralPath $apworld -Destination (Join-Path $customWorlds "ember_knights.apworld") -Force

    Push-Location $archipelagoRoot
    # Unrelated bundled worlds log import errors to stderr in this minimal test environment.
    # Judge the result by the verifier's exit code and its own output only.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $python (Join-Path $PSScriptRoot "tools\verify_packaged_world.py") 2>&1
        $exitCode = $LASTEXITCODE
        $output | Where-Object { "$_" -match "^OK:|Ember Knights did not load|Unexpected data|not from the packaged" } |
            ForEach-Object { Write-Host $_ }
    }
    finally {
        $ErrorActionPreference = $previousPreference
        Pop-Location
        Remove-Item -LiteralPath (Join-Path $customWorlds "ember_knights.apworld") -Force -ErrorAction SilentlyContinue
    }
    if ($exitCode -ne 0) {
        throw "Packaged .apworld failed to load against $archipelagoRoot."
    }
}
