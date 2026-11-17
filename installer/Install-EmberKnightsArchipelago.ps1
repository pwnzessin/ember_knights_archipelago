<#
.SYNOPSIS
    Installs or removes the Ember Knights Archipelago client mod, including BepInEx.

.DESCRIPTION
    What it does, in order:
      1. Finds your Ember Knights folder (Steam), or uses -GamePath.
      2. Checks the game is not running.
      3. Installs BepInEx 6 (IL2CPP) if it is missing. The zip is downloaded from the official BepInEx build server and
         its SHA-256 is checked before anything is extracted. Nothing is overwritten if the check fails.
      4. Copies the plugin (the folder next to this script) into BepInEx\plugins.
      5. Writes your connection settings into the plugin's config file, without touching any other setting.
      6. Adds the standard on-screen settings (HUD log window, title screen line) to the config, but only the ones that
         are not in it yet. A setting you already have is never changed.

    It never touches your save files. BepInEx generates the game-specific files itself the first time you start the
    game (the first start is slow); those are never part of this download.

    Re-running it is safe. It updates the plugin and keeps your existing config and settings.

.PARAMETER GamePath
    Path to the Ember Knights folder (the one with EmberKnights_64.exe). Detected automatically when omitted.

.PARAMETER Uninstall
    Removes the plugin and its config. Add -RemoveBepInEx to remove BepInEx as well.

.PARAMETER NonInteractive
    Never asks questions. Uses -HostName/-Port/-SlotName/-Password if given, otherwise leaves the config alone.

.PARAMETER BepInExZip
    Use a BepInEx zip you already downloaded instead of downloading it. It must still match the expected SHA-256.
#>
[CmdletBinding()]
param(
    [string]$GamePath,
    [switch]$Uninstall,
    [switch]$RemoveBepInEx,
    [switch]$NonInteractive,
    [string]$HostName,
    [int]$Port = 0,
    [string]$SlotName,
    [string]$Password,
    [string]$BepInExZip,
    [switch]$SkipBepInEx
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The exact BepInEx build this mod was tested with. The hash is checked so a changed or corrupted download is rejected.
$BepInExVersion = '6.0.0-be.791'
$BepInExUrl     = 'https://builds.bepinex.dev/projects/bepinex_be/791/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.791%2Bb782da4.zip'
$BepInExSha256  = 'bb1c510909b69522733791f3a0817a487379a52da6468a492f1c9c19b3ec6f9a'

$GameExe    = 'EmberKnights_64.exe'
$SteamAppId = '1135230'
$PluginGuid = 'com.emberknights.archipelago'
$PluginDir  = 'EmberKnights.Archipelago'

function Write-Step([string]$text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Write-Ok([string]$text)   { Write-Host "   $text" -ForegroundColor Green }
function Write-Note([string]$text) { Write-Host "   $text" -ForegroundColor Gray }
function Write-Warn([string]$text) { Write-Host "   $text" -ForegroundColor Yellow }

function Stop-WithMessage([string]$text) {
    Write-Host "`nSTOPPED: $text" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------- game location

function Get-SteamLibraries {
    $roots = New-Object System.Collections.Generic.List[string]
    $steam = $null
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        $item = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($item) {
            foreach ($name in 'SteamPath', 'InstallPath') {
                if ($item.PSObject.Properties.Name -contains $name -and $item.$name) { $steam = $item.$name; break }
            }
        }
        if ($steam) { break }
    }
    if (-not $steam) { return $roots }
    $steam = ($steam -replace '/', '\')
    $roots.Add($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $vdf) {
        foreach ($match in (Select-String -LiteralPath $vdf -Pattern '"path"\s+"([^"]+)"')) {
            $path = ($match.Matches[0].Groups[1].Value -replace '\\\\', '\')
            if (-not $roots.Contains($path)) { $roots.Add($path) }
        }
    }
    return $roots
}

function Find-GameFolder {
    foreach ($library in (Get-SteamLibraries)) {
        $manifest = Join-Path $library "steamapps\appmanifest_$SteamAppId.acf"
        $guess = Join-Path $library 'steamapps\common\EmberKnights'
        if ((Test-Path -LiteralPath $manifest) -and (Test-Path -LiteralPath (Join-Path $guess $GameExe))) { return $guess }
        if (Test-Path -LiteralPath (Join-Path $guess $GameExe)) { return $guess }
    }
    return $null
}

function Test-GameFolder([string]$path) {
    return $path -and (Test-Path -LiteralPath (Join-Path $path $GameExe)) -and
           (Test-Path -LiteralPath (Join-Path $path 'GameAssembly.dll'))
}

function Test-GameRunning {
    return [bool](Get-Process -Name ($GameExe -replace '\.exe$', '') -ErrorAction SilentlyContinue)
}

# ---------------------------------------------------------------------- BepInEx

function Test-BepInExInstalled([string]$game) {
    return (Test-Path -LiteralPath (Join-Path $game 'winhttp.dll')) -and
           (Test-Path -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'))
}

function Get-BepInExZip([string]$workDir) {
    if ($BepInExZip) {
        if (-not (Test-Path -LiteralPath $BepInExZip)) { Stop-WithMessage "The BepInEx zip '$BepInExZip' does not exist." }
        $zipPath = (Resolve-Path -LiteralPath $BepInExZip).Path
        Write-Note "Using the BepInEx zip you supplied: $zipPath"
    }
    else {
        $zipPath = Join-Path $workDir 'BepInEx.zip'
        Write-Note "Downloading BepInEx $BepInExVersion (about 34 MB) from builds.bepinex.dev ..."
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            $oldProgress = $ProgressPreference
            $ProgressPreference = 'SilentlyContinue'
            try { Invoke-WebRequest -Uri $BepInExUrl -OutFile $zipPath -UseBasicParsing -TimeoutSec 300 }
            finally { $ProgressPreference = $oldProgress }
        }
        catch {
            Stop-WithMessage ("The BepInEx download failed: $($_.Exception.Message)`n" +
                "   Download it yourself from $BepInExUrl`n" +
                "   and run this script again with:  -BepInExZip <path to the zip>")
        }
    }

    $actual = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $BepInExSha256) {
        Stop-WithMessage ("The BepInEx zip does not match the version this mod was tested with.`n" +
            "   expected SHA-256 $BepInExSha256`n   got      SHA-256 $actual`n" +
            "   Nothing was installed. Re-download it, or ask for help.")
    }
    Write-Ok 'BepInEx download verified.'
    return $zipPath
}

function Install-BepInEx([string]$game, [string]$workDir) {
    $zipPath = Get-BepInExZip $workDir
    $extract = Join-Path $workDir 'bepinex'
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extract -Force
    # Copy over the game folder. Existing BepInEx config is never overwritten.
    $preserve = Join-Path $game 'BepInEx\config'
    Get-ChildItem -LiteralPath $extract -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $game -Recurse -Force
    }
    Write-Ok "BepInEx $BepInExVersion installed into the game folder."
}

# ----------------------------------------------------------------------- plugin

function Get-PluginSource {
    $candidates = @(
        (Join-Path $PSScriptRoot "BepInEx\plugins\$PluginDir"),
        (Join-Path $PSScriptRoot "plugins\$PluginDir"),
        (Join-Path $PSScriptRoot $PluginDir)
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'EmberKnights.Archipelago.dll')) { return $candidate }
    }
    return $null
}

function Install-Plugin([string]$game) {
    $source = Get-PluginSource
    if (-not $source) {
        Stop-WithMessage ("Could not find EmberKnights.Archipelago.dll next to this script.`n" +
            "   Extract the whole release zip first (do not run the installer from inside the zip).")
    }
    $target = Join-Path $game "BepInEx\plugins\$PluginDir"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    # The state folder holds the player's per-seed progress. Only the program files are replaced.
    foreach ($file in Get-ChildItem -LiteralPath $source -File) {
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
    $dll = Join-Path $target 'EmberKnights.Archipelago.dll'
    $version = (Get-Item -LiteralPath $dll).VersionInfo.ProductVersion
    Write-Ok "Plugin $version installed to BepInEx\plugins\$PluginDir."
}

# ----------------------------------------------------------------------- config

function Get-ConfigPath([string]$game) { return (Join-Path $game "BepInEx\config\$PluginGuid.cfg") }

function Set-ConfigValue([string]$configPath, [string]$section, [string]$key, [string]$value) {
    # Edits one key in a BepInEx config file and leaves every other line, including comments, untouched.
    $lines = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $configPath) {
        foreach ($line in [IO.File]::ReadAllLines($configPath)) { $lines.Add($line) }
    }
    $inSection = $false
    $sectionIndex = -1
    $keyIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $trimmed = $lines[$i].Trim()
        if ($trimmed -match '^\[(.+)\]$') {
            $inSection = ($Matches[1] -eq $section)
            if ($inSection) { $sectionIndex = $i }
            continue
        }
        if ($inSection -and $trimmed -match '^([^#=\s][^=]*?)\s*=') {
            if ($Matches[1].Trim() -eq $key) { $keyIndex = $i; break }
        }
    }
    $entry = "$key = $value"
    if ($keyIndex -ge 0) { $lines[$keyIndex] = $entry }
    elseif ($sectionIndex -ge 0) { $lines.Insert($sectionIndex + 1, $entry) }
    else {
        if ($lines.Count -gt 0 -and $lines[$lines.Count - 1].Trim() -ne '') { $lines.Add('') }
        $lines.Add("[$section]")
        $lines.Add($entry)
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $configPath) | Out-Null
    [IO.File]::WriteAllLines($configPath, $lines, (New-Object Text.UTF8Encoding($false)))
}

function Read-ConfigValue([string]$configPath, [string]$section, [string]$key) {
    if (-not (Test-Path -LiteralPath $configPath)) { return $null }
    $inSection = $false
    foreach ($line in [IO.File]::ReadAllLines($configPath)) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^\[(.+)\]$') { $inSection = ($Matches[1] -eq $section); continue }
        if ($inSection -and $trimmed -match '^([^#=\s][^=]*?)\s*=\s*(.*)$' -and $Matches[1].Trim() -eq $key) { return $Matches[2].Trim() }
    }
    return $null
}

function Ask([string]$prompt, [string]$default) {
    $suffix = if ($default) { " [$default]" } else { '' }
    $answer = Read-Host "   $prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $default }
    return $answer.Trim()
}

function Write-Connection([string]$game) {
    $configPath = Get-ConfigPath $game
    $haveGiven = $HostName -or $Port -gt 0 -or $SlotName -or $PSBoundParameters.ContainsKey('Password')

    if (-not $haveGiven) {
        if ($NonInteractive) { Write-Note 'Connection settings left as they are (non-interactive).'; return }
        Write-Host ''
        Write-Host '   Connection details. Press Enter to skip any of them; you can also set them later in the game' -ForegroundColor Gray
        Write-Host '   with the connection box on first start, or by editing the config file.' -ForegroundColor Gray
        $currentHost = Read-ConfigValue $configPath 'Connection' 'host'
        $currentPort = Read-ConfigValue $configPath 'Connection' 'port'
        $currentSlot = Read-ConfigValue $configPath 'Connection' 'slot_name'
        $script:HostName = Ask 'Server host (for example archipelago.gg)' $(if ($currentHost) { $currentHost } else { '' })
        $portText        = Ask 'Server port' $(if ($currentPort) { $currentPort } else { '' })
        $script:SlotName = Ask 'Your slot name (exactly as in your YAML)' $(if ($currentSlot -and $currentSlot -ne 'Player1') { $currentSlot } else { '' })
        $script:Password = Ask 'Room password (Enter if none)' ''
        if ($portText -match '^\d+$') { $script:Port = [int]$portText }
        $HostName = $script:HostName; $SlotName = $script:SlotName; $Password = $script:Password; $Port = $script:Port
    }

    if ($HostName) { Set-ConfigValue $configPath 'Connection' 'host' $HostName.Trim() }
    if ($Port -gt 0) {
        if ($Port -gt 65535) { Stop-WithMessage "Port $Port is not a valid port number (1 to 65535)." }
        Set-ConfigValue $configPath 'Connection' 'port' "$Port"
    }
    if ($SlotName) { Set-ConfigValue $configPath 'Connection' 'slot_name' $SlotName.Trim() }
    if ($PSBoundParameters.ContainsKey('Password') -or $Password) { Set-ConfigValue $configPath 'Connection' 'password' $Password }
    if ($HostName -or $Port -gt 0 -or $SlotName) {
        Set-ConfigValue $configPath 'Connection' 'auto_connect' 'true'
        Write-Ok 'Connection settings saved. The mod will connect by itself a few seconds after the game starts.'
    }
}

# The on-screen defaults a standard install starts with. They are the author's own settings: the HUD log window on (it is
# off in the plugin itself), a little larger, held on screen longer and faintly visible between messages. They are only
# ever applied to a setting that is not in the config yet, which is the case for a fresh install, so an update never
# overwrites a value the player has already set or changed. The plugin's own built-in defaults are unchanged.
$VisualDefaults = [ordered]@{
    'archipelago_coin_icon' = 'true'
    'title_screen_label'    = 'true'
    'hud_log_window'        = 'true'
    'hud_log_scale'         = '0.8'
    'hud_log_hold_seconds'  = '10'
    'hud_log_idle_opacity'  = '0.4'
}

function Write-VisualDefaults([string]$game) {
    $configPath = Get-ConfigPath $game
    $added = 0
    foreach ($key in $VisualDefaults.Keys) {
        # Read-ConfigValue returns $null when the key is absent. Any present value, even one equal to the default or
        # one the player deliberately changed, is left exactly as it is.
        if ($null -ne (Read-ConfigValue $configPath 'Visuals' $key)) { continue }
        Set-ConfigValue $configPath 'Visuals' $key $VisualDefaults[$key]
        $added++
    }
    if ($added -gt 0) { Write-Ok "On-screen settings set up ($added value(s)). Existing settings were left alone." }
    else { Write-Note 'On-screen settings already present; left as they are.' }
}

# -------------------------------------------------------------------- uninstall

function Invoke-Uninstall([string]$game) {
    Write-Step 'Removing the mod'
    $pluginPath = Join-Path $game "BepInEx\plugins\$PluginDir"
    if (Test-Path -LiteralPath $pluginPath) {
        Remove-Item -LiteralPath $pluginPath -Recurse -Force
        Write-Ok 'Plugin removed (this includes its per-seed progress files).'
    }
    else { Write-Note 'The plugin was not installed.' }
    $configPath = Get-ConfigPath $game
    if (Test-Path -LiteralPath $configPath) { Remove-Item -LiteralPath $configPath -Force; Write-Ok 'Plugin config removed.' }

    if ($RemoveBepInEx) {
        Write-Step 'Removing BepInEx'
        foreach ($name in 'BepInEx', 'dotnet', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt') {
            $path = Join-Path $game $name
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force; Write-Ok "Removed $name" }
        }
    }
    else { Write-Note 'BepInEx was left in place. Add -RemoveBepInEx to remove it too.' }
    Write-Host "`nDone. Your save files were not touched." -ForegroundColor Green
}

# ------------------------------------------------------------------------- main

Write-Host 'Ember Knights Archipelago - installer' -ForegroundColor White
if ($PSVersionTable.PSVersion.Major -lt 5) { Stop-WithMessage 'This installer needs Windows PowerShell 5.1 or newer.' }

Write-Step 'Finding Ember Knights'
if (-not $GamePath) { $GamePath = Find-GameFolder }
if (-not $GamePath -and -not $NonInteractive) {
    Write-Warn 'Could not find the game through Steam.'
    $GamePath = Read-Host '   Paste the path of your Ember Knights folder (the one containing EmberKnights_64.exe)'
    $GamePath = $GamePath.Trim('"', ' ')
}
if (-not (Test-GameFolder $GamePath)) {
    Stop-WithMessage ("'$GamePath' is not an Ember Knights folder (it must contain $GameExe and GameAssembly.dll).`n" +
        "   Run again with:  -GamePath `"C:\path\to\EmberKnights`"")
}
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
Write-Ok "Found: $GamePath"

if (Test-GameRunning) { Stop-WithMessage 'Ember Knights is running. Close the game and run this installer again.' }

if ($Uninstall) { Invoke-Uninstall $GamePath; exit 0 }

$workDir = Join-Path ([IO.Path]::GetTempPath()) ("ek-archipelago-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
try {
    $firstInstall = -not (Test-BepInExInstalled $GamePath)
    Write-Step 'BepInEx'
    if ($SkipBepInEx) { Write-Note 'Skipped (-SkipBepInEx).' }
    elseif ($firstInstall) { Install-BepInEx $GamePath $workDir }
    else { Write-Ok 'BepInEx is already installed; leaving it as it is.' }

    Write-Step 'Mod'
    Install-Plugin $GamePath

    Write-Step 'Connection'
    Write-Connection $GamePath

    Write-Step 'On-screen settings'
    Write-VisualDefaults $GamePath

    Write-Host "`nAll done." -ForegroundColor Green
    Write-Host '  Next:' -ForegroundColor White
    if ($firstInstall) {
        Write-Host '   1. Start Ember Knights from Steam. The FIRST start is slow (a minute or two): BepInEx is preparing' 
        Write-Host '      files for the game. Wait for the main menu, and do not close it early.'
    }
    else {
        Write-Host '   1. Start Ember Knights from Steam and load a single-player save.'
    }
    Write-Host '   2. Press F6 in game to connect (F7 disconnects). With auto-connect on it connects by itself.'
    Write-Host '   Problems? Send BepInEx\LogOutput.log from the game folder.'
}
finally {
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
