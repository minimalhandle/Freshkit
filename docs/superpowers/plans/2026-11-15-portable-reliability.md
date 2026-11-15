# Freshkit portable reliability implementation plan

Goal: deliver improvements 1–4 as a standalone portable EXE.
Architecture: session-only C# list state; embedded backup catalog; persisted job checklist and recipe; existing protected install engine for driver/app recovery.
Tech: C# 5, WPF, .NET Framework 4.x, Windows PowerShell 5.1.
Spec: ../specs/2026-11-15-portable-reliability-design.md

## Constraints and review focus
- No automatic companion files; explicit exports and transient job state only.
- Missing CONFIG entries cannot remove newly shipped definitions.
- Failed native commands must block later stages and survive reopening.
- Restart and retry must preserve the exact session selections.
- Discovery must exclude caches, credentials and reparse points and report inaccessible paths.
- A partial restore must remain visible and retryable.

## Tasks
- [x] 1. Add failing regression harnesses for session state/catalog merging, discovery, job outcomes and retries. Run to establish missing behavior.
- [x] 2. Implement session lists, in-app editor, explicit kit folder selection and embedded catalog merging. Verify source/profile selection consistency.
- [x] 3. Implement structured stage results and native exit handling, checklist UI, persisted retry recipe, driver/app report integration and restore outcome. Verify failure harness and restart reconstruction.
- [x] 4. Implement asynchronous settings discovery cards and refresh. Verify synthetic files/exclusions/running/empty/error cases and backup round trip.
- [x] 5. Update documentation, preserve existing sidecars outside dist, build and run all checks, inspect WPF layout, review and fix material findings.

Execution ledger: work inline in supplied workspace; no Git metadata is present, so no worktree/commit operations. User already requested implementation; no additional design approval is required under the session authorization instructions.

Validation: final build passed; 18 C# core checks passed; Windows PowerShell 5.1 native-failure, engine-transition and settings-roundtrip harnesses passed; all source PowerShell parsed; standalone SettingsList and Plan passed with only Freshkit.exe in dist. Backup availability cards visually inspected in WPF. Independent review findings (unrelated report selection, resume failure reporting, interrupted restore recovery) fixed and re-reviewed. No live installs, driver changes or reboots executed.
Ruling: existing companion data moved intact to obj/previous-portable-data. The UI preview executable was moved to obj/Freshkit-ui-preview.exe while open so the final release could be rebuilt without terminating the user's window. Close the existing preview before launching the rebuilt release.
