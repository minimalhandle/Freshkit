# Reinstall folder, daily settings backups and more backup profiles

## Goal
Make "wipe Windows and get back to my setup" a two-step job without tailoring Freshkit's defaults to one person:
**Prepare for reinstall** before wiping, **Restore from reinstall folder** afterwards. Personal choices travel in an
ordinary `.freshkit.json` recipe inside that folder.

## Prepare for reinstall (Profile ⋯ menu)
- The user picks a folder (ideally on a drive that won't be wiped). Freshkit warns when it is on the Windows drive.
- Writes `setup.freshkit.json` (the current session's recipe: catalog, apps, tweaks, removals, backup selections).
- Records Steam library folders that are **not** on the Windows drive (those survive a reinstall).
- Runs a settings backup of the selected backup profiles (all available ones when none are selected) into
  `settings-backup-YYYYMMDD-HHMMSS.zip`. Running apps are skipped with a warning, as for any backup.
- Writes `freshkit-reinstall.json`: `{Format:"FreshkitReinstall", Version:1, CreatedUtc, Recipe, SettingsBackup, SteamLibraries[]}`.
  File references are plain file names inside the folder.
- **No drivers are exported.** A reinstall installs current vendor drivers through the hardware scan, so a driver that
  is misbehaving on the old install is never copied across. The existing optional driver/Wi-Fi kit stays separate.

## Restore from reinstall folder (Profile ⋯ menu)
- The user picks the folder; nothing is read automatically.
- Validates the manifest, parses the recipe (same validation as Import), shows the usual import preview plus the
  settings backup and Steam libraries that will be used.
- Apply: imports the recipe into the session, sets the settings backup as the restore archive (Review setup picks it
  up), and remembers the Steam libraries for this setup.
- After setup finishes, if Steam is installed and not running, missing library folders that still exist are added to
  Steam's `steamapps\libraryfolders.vdf`. Steam scans them on its next start, so installed games are found instead of
  downloaded again. Failure (no Steam, Steam running, no permission) is logged with manual instructions; it never fails setup.

## Daily settings backup (Settings backup page)
- Opt-in. Choose a folder, a time and how many backups to keep (default 7).
- The task runs as the signed-in user without elevation, using a copy of the settings engine and the session's merged
  backup profiles stored in `%LOCALAPPDATA%\Freshkit\ScheduledBackup`, so it keeps working without the EXE or USB.
- Each run writes `settings-backup-auto-YYYYMMDD-HHMMSS.zip`; running apps are skipped. Only `settings-backup-auto-*.zip`
  files are pruned. Turning it off removes the task and its engine copy (backups are kept).

## Backup profiles added
Firefox (profiles; passwords, cookies, caches, telemetry and downloadable codecs excluded), Elgato Stream Deck
(profiles, plugins, icon packs), Voicemeeter (settings XML), Playnite (configuration and library), Elgato Studio
(settings) and Windows Terminal (settings). Store-installed apps are detected through an optional `appxName` field
because they have no uninstall entry.

## Catalog
Unticked additions: Voicemeeter Potato, Elgato Stream Deck, Elgato Studio, Elgato 4K Capture Utility, MSI Afterburner,
RivaTuner Statistics Server, Windows Terminal, Rustup, Microsoft 365 Apps and Ludusavi. Defaults are unchanged; personal
picks belong in a recipe. The NVIDIA App (Store `XP8CLZL93F5Z4P`) installs through winget but is missing from Microsoft's
public Store catalog, so `check-catalog.ps1` can't verify it; it stays out of the built-in catalog.

## Not included
- Generating an autounattend file (users bring their own install media).
- Copying drivers between installs.
- Restoring Windows default-app associations (Windows 11 protects them with a per-user hash).
