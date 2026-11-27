# No default selection, two catalog apps and setup extras

## Goal
The built-in selection started as one person's picks. Ship Freshkit with nothing ticked, make it easy to start anyway,
and add tools that help on PCs with and without Freshkit.

## Changes
- **No default selection.** Every catalog entry is unticked. The Defaults button and the "List defaults" profile entry
  are removed; starter profiles stay. The engine only refuses `Install`, `Plan` and `List` when nothing is selected,
  so driver and settings commands keep working.
- **Catalog:** OpenRGB Effects Plugin (winget `OpenRGB.OpenRGBEffectsPlugin`) and Visual Studio Build Tools 2026
  (winget `Microsoft.VisualStudio.BuildTools`, default override: C++ workload with recommended components).
- **Command-line recipes:** `Freshkit.exe Install | Plan | List <recipe.json>` accepts a Freshkit recipe or winget
  export. Only apps apply; the command line says Windows and backup selections need the window.
- **Dependencies:** `::REQUIRES app|needed,...` in `::BEGIN_REQUIRES`. A user tick adds what the app needs
  (transitively, catalog apps only). Unticking, profiles, recipes and batch selection never cascade.
- **Build Tools workloads:** checkboxes for the documented Build Tools workloads (2026 list; 2022 uses
  XamarinBuildTools) plus `--includeRecommended`. Rebuilt arguments keep silent switches and unrelated arguments.
- **Starter banner:** while nothing is ticked, the Apps page offers the starter profiles; dismissible per session.
- **Setup estimate:** typical sizes and times plus known large installers; an optional read-only check uses
  `winget show` and an HTTP HEAD for winget installers.
- **Portable export:** ticked apps to a `winget import` JSON (winget + msstore) and a Chocolatey `packages.config`.
  GitHub releases are skipped; dropped winget installer options are listed.
- **Recipe gaps:** a comparison window can select the recipe's apps reported Missing (never Unknown).
- **Catalog health:** `check-catalog.ps1` falls back to `microsoft/winget-pkgs` manifest folders without winget; a
  weekly GitHub Actions workflow runs it.

## Constraints kept
Detection stays read-only (the size check reads metadata only). Nothing is persisted beside the EXE. C# 5 and
PowerShell 5.1 only; no new dependencies.

## Follow-up
- **Recipe catalogs:** applying a recipe adds built-in apps (unticked), dependencies and starter profiles its catalog
  lacks, so apps added after export stay available. A version the recipe chose blocks its family's default.
- **Versions:** one entry, several packages of the same source: Build Tools 2026/2022/2019 (winget) and Python 3
  latest/3.14–3.11 (Chocolatey). Switching renames the entry and rewrites profile and dependency keys. Build Tools
  2019 has no verified workload list, so its workloads stay free text.
- **Release hygiene:** inert `en-GB` Firefox parameters and wording left from the original script were neutralised.
