# Freshkit: notes for Claude

Freshkit is a portable Windows 11 setup app. The user-facing behaviour is documented in `README.md`. This file covers how to work on the code.

## Hard constraints
- **Ship as one EXE.** `dist\Freshkit.exe` must run on its own from any folder, including a USB drive. Embed every new asset (catalog, XAML, scripts, images, DLLs) as a resource in `build.ps1`, and also in any test runner that needs it.
- **Use only what Windows already provides:** .NET Framework 4.x `csc.exe` (the **C# 5** language level, so no `?.`, `$""`, `nameof` or expression-bodied members), WPF, and **Windows PowerShell 5.1**. Don't add NuGet packages, SDKs or other runtimes.
- **Preferences last for one session only.** Never read or write a `Freshkit.lists.txt` or `FreshkitKit` beside the EXE automatically. Recipes are import/export only. Job snapshots are recovery data, not preferences.
- **Detection is read-only.** Scans (tweak state, drivers, preflight, audit) never change selections, close apps, reboot, elevate or repair anything. Report evidence that is missing or ambiguous as Unknown; never count it as success or as missing.
- **GitHub apps:** a name-only match is tentative. Treat an installed app as identified only when its uninstall entry has the repository URL or the user explicitly linked it.

## Layout
- `src/app/*.cs`: the WPF app. `MainUi.*.cs` are partial classes, one per page or feature. Feature models (`Preflight.cs`, `SetupAudit.cs`, `AppOptions.cs`, …) contain no UI code and are tested directly.
- `src/MainWindow.xaml`: loaded at runtime from an embedded resource.
- `src/payload/`: install, settings and driver engine (PowerShell modules). `src/scripts/`: helper scripts run by the app.
- `src/Freshkit.lists.txt`: the embedded catalog (`::PACKAGE` ticked, `::DISABLED` unticked, `::WINGET`, `::GITHUB`, `::GROUP`). Run `tools/check-catalog.ps1` after editing it.
- `vendor/Win11Debloat`: vendored code that is zipped into the EXE. Don't edit it.
- `docs/superpowers/specs|plans/`: one spec and one plan per feature batch, named `YYYY-MM-DD-topic.md`.

## Build and test
```
powershell -ExecutionPolicy Bypass -File tests\run-all.ps1
```
This builds the EXE, runs every `tests\run-*.ps1` runner and every standalone `tests\*.ps1` payload script, and prints a pass/fail table in about 15 seconds. A non-zero exit code means something failed. To build only, run `build.ps1`. For a rendered UI pass, run `tests\run-ui.ps1 -Render`.

- When you add a payload test script under `tests\`, `run-all.ps1` picks it up automatically. When you add a C# test file, add it to the `$sources` of the matching runner.
- Tests must not install software, change Windows settings or depend on the network. Use synthetic fixtures (fake CIM, registry and inventory data).
- To check portability, copy only `dist\Freshkit.exe` to an empty folder and run `Freshkit.exe Plan`.

## Workflow
- Work on a feature branch and commit each finished, tested batch. Don't leave work uncommitted across sessions. Never push unless asked.
- Before claiming a batch is done, run `tests\run-all.ps1` and `git diff --check`. If something couldn't be verified (for example, what Explorer shows), say so.
- Keep `README.md` up to date with any behaviour a user would notice.
