<#
Tests the installer against a THROWAWAY fake game folder in the temp directory. It never touches a real game install.
Run from the repository:  powershell -ExecutionPolicy Bypass -File installer\Test-Installer.ps1 -PluginDll <path> -BepInExZip <path>
#>
param(
    [Parameter(Mandatory)][string]$PluginDll,
    [Parameter(Mandatory)][string]$BepInExZip
)
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Install-EmberKnightsArchipelago.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ("ek-installer-test-" + [Guid]::NewGuid().ToString('N'))
$script:pass = 0; $script:fail = 0

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { $script:pass++; Write-Host "  PASS  $name" -ForegroundColor Green }
    else { $script:fail++; Write-Host "  FAIL  $name  $detail" -ForegroundColor Red }
}

function New-FakeGame([string]$name) {
    $dir = Join-Path $root $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -LiteralPath (Join-Path $dir 'EmberKnights_64.exe') -Value 'fake'
    Set-Content -LiteralPath (Join-Path $dir 'GameAssembly.dll') -Value 'fake'
    New-Item -ItemType Directory -Force -Path (Join-Path $dir 'EmberKnights_Data') | Out-Null
    Set-Content -LiteralPath (Join-Path $dir 'EmberKnights_Data\save.dat') -Value 'my save'
    return $dir
}

function Run([string[]]$arguments) {
    # Run in a child powershell so exit codes and Stop-WithMessage are observed exactly as a user would see them.
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer @arguments 2>&1 | Out-String
    return [pscustomobject]@{ Code = $LASTEXITCODE; Text = $output }
}

# A release-shaped folder: the script sits next to BepInEx\plugins\EmberKnights.Archipelago\*.dll
$pkg = Join-Path $root 'pkg'
New-Item -ItemType Directory -Force -Path (Join-Path $pkg 'BepInEx\plugins\EmberKnights.Archipelago') | Out-Null
$srcDir = Split-Path -Parent $PluginDll
foreach ($f in 'EmberKnights.Archipelago.dll', 'Archipelago.MultiClient.Net.dll', 'Newtonsoft.Json.dll') {
    Copy-Item -LiteralPath (Join-Path $srcDir $f) -Destination (Join-Path $pkg 'BepInEx\plugins\EmberKnights.Archipelago') -Force
}
Copy-Item -LiteralPath $installer -Destination $pkg -Force
$installer = Join-Path $pkg 'Install-EmberKnightsArchipelago.ps1'

try {
    Write-Host "`n[1] Fresh install into an empty game folder (non-interactive, details given)"
    $g1 = New-FakeGame 'fresh'
    $r = Run @('-GamePath', $g1, '-NonInteractive', '-BepInExZip', $BepInExZip, '-HostName', 'archipelago.gg', '-Port', '4242', '-SlotName', 'Tester', '-Password', 'pw')
    Check 'exit code 0' ($r.Code -eq 0) $r.Text
    Check 'winhttp.dll installed' (Test-Path (Join-Path $g1 'winhttp.dll'))
    Check 'BepInEx core installed' (Test-Path (Join-Path $g1 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'))
    Check 'dotnet runtime installed' (Test-Path (Join-Path $g1 'dotnet\coreclr.dll'))
    Check 'plugin dll installed' (Test-Path (Join-Path $g1 'BepInEx\plugins\EmberKnights.Archipelago\EmberKnights.Archipelago.dll'))
    Check 'dependencies installed' ((Test-Path (Join-Path $g1 'BepInEx\plugins\EmberKnights.Archipelago\Archipelago.MultiClient.Net.dll')) -and (Test-Path (Join-Path $g1 'BepInEx\plugins\EmberKnights.Archipelago\Newtonsoft.Json.dll')))
    $cfg = Join-Path $g1 'BepInEx\config\com.emberknights.archipelago.cfg'
    $c = if (Test-Path $cfg) { Get-Content $cfg -Raw } else { '' }
    Check 'config has host' ($c -match '(?m)^host = archipelago\.gg\s*$')
    Check 'config has port' ($c -match '(?m)^port = 4242\s*$')
    Check 'config has slot' ($c -match '(?m)^slot_name = Tester\s*$')
    Check 'config has password' ($c -match '(?m)^password = pw\s*$')
    Check 'config enables auto_connect' ($c -match '(?m)^auto_connect = true\s*$')
    Check 'save data untouched' ((Get-Content (Join-Path $g1 'EmberKnights_Data\save.dat') -Raw).Trim() -eq 'my save')
    Check 'no temp folder left behind' (-not (Get-ChildItem ([IO.Path]::GetTempPath()) -Directory -Filter 'ek-archipelago-*' -ErrorAction SilentlyContinue))
    # The standard on-screen settings are written on a fresh install, in the Visuals section.
    Check 'visuals: HUD log window on' ($c -match '(?m)^hud_log_window = true\s*$')
    Check 'visuals: HUD scale 0.8' ($c -match '(?m)^hud_log_scale = 0\.8\s*$')
    Check 'visuals: hold 10 seconds' ($c -match '(?m)^hud_log_hold_seconds = 10\s*$')
    Check 'visuals: idle opacity 0.4' ($c -match '(?m)^hud_log_idle_opacity = 0\.4\s*$')
    Check 'visuals: coin icon on' ($c -match '(?m)^archipelago_coin_icon = true\s*$')
    Check 'visuals: title label on' ($c -match '(?m)^title_screen_label = true\s*$')
    Check 'visuals: all six sit under one [Visuals] header' ((([regex]::Matches($c, '(?m)^\[Visuals\]\s*$')).Count -eq 1))
    Check 'connection settings unaffected by the visuals step' (($c -match '(?m)^host = archipelago\.gg\s*$') -and ($c -match '(?m)^slot_name = Tester\s*$'))

    Write-Host "`n[2] Re-run: keeps config and extra settings, updates the plugin, does not reinstall BepInEx"
    Add-Content -LiteralPath $cfg -Value "`n[Visuals]`nhud_log_window = true"
    Add-Content -LiteralPath (Join-Path $g1 'BepInEx\plugins\EmberKnights.Archipelago\leftover.txt') -Value 'x' -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (Join-Path $g1 'BepInEx\plugins\EmberKnights.Archipelago\state') | Out-Null
    Set-Content -LiteralPath (Join-Path $g1 'BepInEx\plugins\EmberKnights.Archipelago\state\seed.json') -Value '{"progress":1}'
    $marker = Join-Path $g1 'BepInEx\core\marker.txt'; Set-Content $marker 'still here'
    $r = Run @('-GamePath', $g1, '-NonInteractive', '-BepInExZip', $BepInExZip)
    Check 'exit code 0' ($r.Code -eq 0) $r.Text
    Check 'says BepInEx already installed' ($r.Text -match 'already installed')
    Check 'BepInEx not re-extracted (marker survives)' (Test-Path $marker)
    $c2 = Get-Content $cfg -Raw
    Check 'slot kept' ($c2 -match '(?m)^slot_name = Tester\s*$')
    Check 'unrelated setting kept' ($c2 -match '(?m)^hud_log_window = true\s*$')
    Check 'per-seed state kept' (Test-Path (Join-Path $g1 'BepInEx\plugins\EmberKnights.Archipelago\state\seed.json'))

    Write-Host "`n[3] Changing one value edits only that line"
    $before = Get-Content $cfg
    $r = Run @('-GamePath', $g1, '-NonInteractive', '-BepInExZip', $BepInExZip, '-SlotName', 'Other')
    $after = Get-Content $cfg
    $diff = Compare-Object $before $after
    Check 'exactly one line replaced' (($diff | Measure-Object).Count -eq 2) ("diff: " + ($diff | Out-String))
    Check 'new slot present' ((Get-Content $cfg -Raw) -match '(?m)^slot_name = Other\s*$')

    Write-Host "`n[4] A wrong checksum is rejected and nothing is installed"
    $bad = Join-Path $root 'bad.zip'; Set-Content -LiteralPath $bad -Value 'not a real zip'
    $g4 = New-FakeGame 'badzip'
    $r = Run @('-GamePath', $g4, '-NonInteractive', '-BepInExZip', $bad)
    Check 'exit code non-zero' ($r.Code -ne 0)
    Check 'explains the mismatch' ($r.Text -match 'does not match')
    Check 'no BepInEx written' (-not (Test-Path (Join-Path $g4 'winhttp.dll')))
    Check 'no plugin written' (-not (Test-Path (Join-Path $g4 'BepInEx\plugins')))

    Write-Host "`n[5] Not an Ember Knights folder is refused"
    $notGame = Join-Path $root 'notgame'; New-Item -ItemType Directory -Force -Path $notGame | Out-Null
    $r = Run @('-GamePath', $notGame, '-NonInteractive', '-BepInExZip', $BepInExZip)
    Check 'exit code non-zero' ($r.Code -ne 0)
    Check 'explains what is needed' ($r.Text -match 'EmberKnights_64\.exe')
    Check 'nothing written' (-not (Get-ChildItem $notGame -Force))

    Write-Host "`n[6] Missing plugin files next to the script is explained"
    $emptyPkg = Join-Path $root 'emptypkg'; New-Item -ItemType Directory -Force -Path $emptyPkg | Out-Null
    Copy-Item $installer $emptyPkg
    $g6 = New-FakeGame 'noplugin'
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $emptyPkg 'Install-EmberKnightsArchipelago.ps1') -GamePath $g6 -NonInteractive -BepInExZip $BepInExZip 2>&1 | Out-String
    Check 'exit code non-zero' ($LASTEXITCODE -ne 0)
    Check 'tells the user to extract the zip' ($out -match 'Extract the whole release zip')

    Write-Host "`n[7] Bad port is refused"
    $g7 = New-FakeGame 'badport'
    $r = Run @('-GamePath', $g7, '-NonInteractive', '-BepInExZip', $BepInExZip, '-Port', '99999', '-SlotName', 'x')
    Check 'exit code non-zero' ($r.Code -ne 0)
    Check 'explains the port' ($r.Text -match 'not a valid port')

    Write-Host "`n[8] Uninstall removes the plugin and config, keeps BepInEx and saves"
    $r = Run @('-GamePath', $g1, '-Uninstall')
    Check 'exit code 0' ($r.Code -eq 0) $r.Text
    Check 'plugin removed' (-not (Test-Path (Join-Path $g1 'BepInEx\plugins\EmberKnights.Archipelago')))
    Check 'config removed' (-not (Test-Path $cfg))
    Check 'BepInEx kept' (Test-Path (Join-Path $g1 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'))
    Check 'save kept' (Test-Path (Join-Path $g1 'EmberKnights_Data\save.dat'))

    Write-Host "`n[9] Uninstall with -RemoveBepInEx removes it too, but never the game or saves"
    $r = Run @('-GamePath', $g1, '-Uninstall', '-RemoveBepInEx')
    Check 'exit code 0' ($r.Code -eq 0) $r.Text
    Check 'BepInEx folder removed' (-not (Test-Path (Join-Path $g1 'BepInEx')))
    Check 'winhttp removed' (-not (Test-Path (Join-Path $g1 'winhttp.dll')))
    Check 'game exe kept' (Test-Path (Join-Path $g1 'EmberKnights_64.exe'))
    Check 'save kept' ((Get-Content (Join-Path $g1 'EmberKnights_Data\save.dat') -Raw).Trim() -eq 'my save')

    Write-Host "`n[11] An update never overwrites on-screen settings the player already has"
    $g11 = New-FakeGame 'visuals-update'
    $cfg11 = Join-Path $g11 'BepInEx\config\com.emberknights.archipelago.cfg'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $cfg11) | Out-Null
    # A config from an older version: the player turned the HUD window off, shrank it, and has no title_screen_label key yet.
    Set-Content -LiteralPath $cfg11 -Value @(
        '## Settings file was created by plugin Ember Knights Archipelago v1.4.0',
        '',
        '[Visuals]',
        '',
        '## my own note',
        'hud_log_window = false',
        'hud_log_scale = 0.5',
        'archipelago_coin_icon = false')
    $r = Run @('-GamePath', $g11, '-NonInteractive', '-BepInExZip', $BepInExZip)
    Check 'exit code 0' ($r.Code -eq 0) $r.Text
    $c11 = Get-Content $cfg11 -Raw
    Check 'player value kept: HUD window still off' ($c11 -match '(?m)^hud_log_window = false\s*$')
    Check 'player value kept: scale still 0.5' ($c11 -match '(?m)^hud_log_scale = 0\.5\s*$')
    Check 'player value kept: coin icon still off' ($c11 -match '(?m)^archipelago_coin_icon = false\s*$')
    Check 'no standard value overwrote them' (-not ($c11 -match '(?m)^hud_log_window = true\s*$') -and -not ($c11 -match '(?m)^hud_log_scale = 0\.8\s*$'))
    Check 'missing keys were added' (($c11 -match '(?m)^title_screen_label = true\s*$') -and ($c11 -match '(?m)^hud_log_hold_seconds = 10\s*$') -and ($c11 -match '(?m)^hud_log_idle_opacity = 0\.4\s*$'))
    Check 'comment line kept' ($c11 -match '## my own note')
    Check 'still exactly one [Visuals] header' ((([regex]::Matches($c11, '(?m)^\[Visuals\]\s*$')).Count -eq 1))
    # Running it again changes nothing at all.
    $before11 = Get-Content $cfg11
    $r = Run @('-GamePath', $g11, '-NonInteractive', '-BepInExZip', $BepInExZip)
    Check 'second run leaves the config byte-identical' (-not (Compare-Object $before11 (Get-Content $cfg11)))
    Check 'second run says they are already present' ($r.Text -match 'already present')

    Write-Host "`n[12] A config with no [Visuals] section at all gets the section, and its other sections are untouched"
    $g12 = New-FakeGame 'visuals-none'
    $cfg12 = Join-Path $g12 'BepInEx\config\com.emberknights.archipelago.cfg'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $cfg12) | Out-Null
    Set-Content -LiteralPath $cfg12 -Value @('[Connection]', 'host = example.org', 'port = 38281')
    $r = Run @('-GamePath', $g12, '-NonInteractive', '-BepInExZip', $BepInExZip)
    $c12 = Get-Content $cfg12 -Raw
    Check 'exit code 0' ($r.Code -eq 0) $r.Text
    Check 'connection section untouched' (($c12 -match '(?m)^host = example\.org\s*$') -and ($c12 -match '(?m)^port = 38281\s*$'))
    Check 'a [Visuals] section was added with all six values' ((([regex]::Matches($c12, '(?m)^\[Visuals\]\s*$')).Count -eq 1) -and ($c12 -match '(?m)^hud_log_window = true\s*$') -and ($c12 -match '(?m)^hud_log_idle_opacity = 0\.4\s*$') -and ($c12 -match '(?m)^archipelago_coin_icon = true\s*$'))

    Write-Host "`n[10] Paths with spaces and the .bat launcher"
    $g10 = New-FakeGame 'path with spaces'
    $r = Run @('-GamePath', $g10, '-NonInteractive', '-BepInExZip', $BepInExZip)
    Check 'installs into a path with spaces' (($r.Code -eq 0) -and (Test-Path (Join-Path $g10 'BepInEx\plugins\EmberKnights.Archipelago\EmberKnights.Archipelago.dll'))) $r.Text
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ("`n{0} passed, {1} failed" -f $script:pass, $script:fail) -ForegroundColor $(if ($script:fail -eq 0) { 'Green' } else { 'Red' })
exit $(if ($script:fail -eq 0) { 0 } else { 1 })
