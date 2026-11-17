# Options, drivers, tweaks and logo implementation plan

Spec: ../specs/2026-11-15-options-drivers-tweaks-design.md
Tech: C#5, WPF, .NET Framework 4.x, PowerShell5.1. Preserve the dirty workspace and session-only defaults. User authorised implementation.

- [x] App options: standalone failing tests, source-aware editor/context routing, catalog persistence, MSI propagation and real UI test.
- [x] Drivers: reviewed modern Intel PCI coverage, existing AMD/hybrid coverage, continuation/evidence tests and compatibility notes.
- [x] Tweaks: model tests first, vendor category/alias metadata, installed-only filtering, applied-state models, bulk category/quick selections and async script bridge.
- [x] Logo: comparison board generated, user selected B; preserve prompts/assets and package icon/sidebar.
- [x] Integrate: compile, safe test suites, independent review, rendered UI inspection and standalone Plan. Rebuild dist/Freshkit.exe, leave changes uncommitted.

Delegation: independent app-options, driver and read-only detection files assigned to separate workers. Root owns Debloat.cs, MainUi.Pages.cs/MainUi.Tweaks.cs, XAML, UI integration tests, branding and final verification.

Ruling: a bulk category action selects the first supported option for mutually exclusive choices, consistent with the vendor UI; selecting every contradictory option is impossible. Applied-state refresh preserves user edits. Intel compatibility is constrained to verified current-driver hardware.

## Completion evidence
- App options: 30 source-aware validation/catalog/recipe checks passed; mocked real GitHub MSI path passed; UI saves and preserves selection. The editor uses the app dark theme and is reachable from each app-card context menu.
- Drivers: 42 synthetic hardware, hybrid, package/evidence and continuation checks passed. Modern Intel compatibility is allowlisted; older/unreviewed Intel devices receive OEM guidance. Existing AMD detection remains broad vendor/class matching with documented legacy/OEM limitations.
- Tweaks: 96 rendered UI checks passed, covering initial applied-state selection, preservation of user edits, category and quick selection, alias arrays, installed filtering and job/scan epoch rejection. Backend synthetic tests cover registry kinds/raw values/order/deletions/unknowns and Appx errors.
- Read-only host scan inspected 106 feature definitions: 28 Applied, 47 Not applied, 31 Unknown. Appx inventory was unavailable in this execution environment and correctly stayed incomplete/unknown. No Windows settings were modified.
- Branding: user chose B, Modular F. Selected PNG and comparison board/prompts saved in the project; ICO includes 16/20/24/32/40/48/64/256 px. Sidebar and Windows icon updated. App options, tweaks and installed-only removal UI visually inspected at compact sizes.
- Regression suites: 39 core checks, 96 UI checks, 30 app-options checks, 42 driver checks, mocked MSI payload, detector, job-status, engine-progress, settings-roundtrip, identity and schedule-payload all passed. All 31 source/test/tool PowerShell files parsed. git diff --check clean.
- Independent review: type-aware installed matching and pre-job snapshot invalidation corrected with failing-then-passing regressions; final review found no remaining material issues.
- Final build: dist/Freshkit.exe, 4,577,792 bytes. EXE-only Plan passed from obj/standalone-review. No real driver/app installs, tweak changes, task registration, commits or pushes.