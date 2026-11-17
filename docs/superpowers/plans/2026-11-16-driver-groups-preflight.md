# Installer groups and preflight implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement task by task.

**Goal:** Compact driver preview by installer and show actionable setup readiness checks.
**Architecture:** Existing preview rows remain the inventory contract. A package grouping model serves the preview and compact review; a read-only scanner captures facts for a pure selection-aware evaluator.
**Tech Stack:** C#5, WPF, .NET Framework, Windows PowerShell 5.1 tests.
**Spec:** docs/superpowers/specs/2026-11-16-driver-groups-preflight.md

## Global constraints

- Preserve standalone EXE and session-only preferences.
- No installs, reboots, process termination, source repairs or settings writes in preflight.
- Installed Windows driver versions and package versions are separate.
- Preserve existing uncommitted work; no commits or branch changes.

## Review focus

- Many identical chipset names must retain individual device IDs and versions.
- Unknown/unmatched devices must not imply installer coverage.
- Offline kit selection must not imply offline app installation works.
- Old asynchronous results must not enable a reopened review prematurely.
- Running-app warnings apply only when settings restore is selected.

## Task 1: Group installer previews

- [x] Add failing many-device, mixed-package and unmatched grouping tests in tests/DriverPreviewTests.cs; run tests/run-driver-preview.ps1.
- [x] Implement DriverPreviewReport.Groups and compact Summary in src/app/DriverPreview.cs; render one card per group with collapsed device details in src/app/MainUi.DriverPreview.cs.
- [x] Run model and UI checks, including minimum-size preview renders.

## Task 2: Preflight scanner and review

- [x] Add failing selection-aware readiness tests in tests/PreflightTests.cs and tests/run-preflight.ps1.
- [x] Implement snapshot, source probes and evaluator in src/app/Preflight.cs, and background review lifecycle in src/app/MainUi.Preflight.cs. Add XAML results/Recheck and start gating in MainUi.Review.cs.
- [x] Test source failures/unknown state, local-space thresholds, restart and running processes, stale scans and review-option changes without touching host settings.
- [x] Build the standalone EXE, run all safe regression scripts, inspect rendered layouts and update README.

## Verification and review

All 14 safe test scripts passed after the final fix, including 126 rendered UI checks, 14 driver-group model checks and 12 preflight checks. Minimum-size driver/review renders inspected. Read-only host smoke check confirmed all four HTTPS endpoints and winget ready; a pending restart was correctly reported. No installs or Windows settings changes performed.

Independent review found one important issue: empty backup profiles were included in process warnings. Reproduced with a synthetic ZIP, filtered manifest Apps by Items > 0 to match the restore engine, and verified RED→GREEN plus the full suite. No other important findings or deferred minors.

Build output: dist/updated/Freshkit.exe. The existing dist/Freshkit.exe is open and locked; it was left running to preserve the current session. No commits or branch changes made.
