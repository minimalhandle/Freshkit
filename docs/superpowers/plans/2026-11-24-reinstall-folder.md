# Plan: reinstall folder, daily backups, profiles and catalog

1. Catalog: add unticked winget/Store entries; fetch logos with `tools/add-missing-icons.ps1`; run `tools/check-catalog.ps1`.
2. Profiles: add Firefox, Stream Deck, Voicemeeter, Playnite, Elgato Studio and Windows Terminal to
   `Settings.Profiles.json`; add optional `appxName` detection to `Get-SettingsInstalledState`.
3. Model `src/app/Reinstall.cs` (no UI): manifest serialize/parse/validate, Steam library parsing, library merge and
   system-drive filtering. Tests in `tests/ReinstallTests.cs` (synthetic VDF/manifest text, temp folders only).
4. UI: Profile ⋯ → Prepare for reinstall… / Restore from reinstall folder…; JobSpec.SteamLibraries carried into the
   reviewed job and applied after setup.
5. Daily backup: `Settings.ps1 -Action Auto` (timestamped ZIP + pruning), Runner actions `ScheduleBackup` /
   `UnscheduleBackup`, Settings page card. Payload test for naming/pruning with synthetic profiles.
6. README, `tests\run-all.ps1`, `git diff --check`, commit.
