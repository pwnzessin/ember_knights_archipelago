EMBER KNIGHTS ARCHIPELAGO - CLIENT MOD
======================================

EASY INSTALL (Windows, Steam)

  1. Extract this whole zip into a folder (right-click > Extract All).
     Do not run anything from inside the zip window.
  2. Close Ember Knights, then double-click  Install.bat
     It finds your game, installs BepInEx and the mod, and asks for your
     server address, port and slot name. Press Enter to skip any question.
  3. Start Ember Knights from Steam. The FIRST start is slow (a minute or two).
     Wait for the main menu.
  4. Load a single-player save. The mod connects by itself, or press F6.
     F7 disconnects.

If Windows shows a blue "protected your PC" box for Install.bat, click
"More info" and then "Run anyway". The script is plain text; you can open
Install-EmberKnightsArchipelago.ps1 in Notepad to read exactly what it does.

WHAT IT CHANGES
  - Adds BepInEx to the game folder (winhttp.dll, doorstop_config.ini, the
    BepInEx and dotnet folders). The BepInEx download is checked against a known
    SHA-256 before anything is installed.
  - Adds BepInEx\plugins\EmberKnights.Archipelago.
  - Writes BepInEx\config\com.emberknights.archipelago.cfg, including the
    standard on-screen settings (HUD log window on, scale 0.8, held 10 seconds,
    idle opacity 0.4). It only adds a setting that is not there yet.
  It never touches your save files.

UPDATING   Run Install.bat again. Your settings and progress are kept: a
           setting you already have in the config is never changed.

UNINSTALLING
  Uninstall.bat removes the mod and its config and leaves BepInEx and saves.
  To remove BepInEx too, open a Command Prompt in this folder and run:
      Install.bat -Uninstall -RemoveBepInEx

MANUAL INSTALL (if you already use BepInEx 6 IL2CPP)
  Copy the BepInEx folder from this zip into the game folder, start the game
  once, then edit BepInEx\config\com.emberknights.archipelago.cfg.

HELP   Send BepInEx\LogOutput.log from the game folder when reporting a problem.
       Full documentation: see the README on the project's GitHub page.
