# App options, drivers, Windows tweak controls and branding

User requested six functional additions and logo options; user selected B, Modular F, from the generated comparison board.

Retain C#5/WPF, embedded PowerShell and standalone/session-only operation. Preserve the previous dirty changes. Work in the supplied checkout without git mutations or live installers.

1. App-card context menu opens source-specific installation arguments, with validated advanced single-line text and a few verified presets. Chocolatey package parameters, winget installer overrides and GitHub installer arguments stay distinct. Save into the session catalog so review/recipes/recovery carry the values.
2. Verify AMD's existing driver pipeline and add modern Intel graphics with reviewed PCI IDs, hardware/OS compatibility gates and machine registration checks. Unreviewed hardware gets vendor guidance rather than a guessed incompatible installer.
3. Only show installed apps combines exact current-user Appx names and winget IDs; preserve all removal selections when filtering. Unknown scan state stays visible and is explained. Support vendor alias arrays.
4. Read-only background detection on opening Tweaks, plus Refresh. Reuse embedded vendor definitions/parser. Show applied/not applied/unknown; grouped settings show the detected option. Initially select detected applied settings only when untouched; refresh never replaces explicit user choices. Missing/unsupported/failed detection stays Unknown. No elevation or mutations for scanning.
5. Every tweak category gets Select all/Clear. Toggle selection affects displayed controls and picks only the first supported option for each mutually exclusive choice, matching vendor bulk-selection semantics.
6. Quick select Privacy & suggested content and AI features uses stable vendor category IDs and first-option semantics, preserving other categories.
7. Generated A/B/C/D logo board is preserved under docs/logo-concepts. Install selected B as the icon and sidebar mark, keeping the source PNG and reproducible multi-size ICO packaging.

Approach: native existing controls and models, with a focused read-only detection script. Alternatives such as launching the vendor UI for these controls or persisting preferences automatically do not meet the requested integrated/session-only behavior.

Validation: tests for argument propagation, hardware matching/continuation, read-only state semantics, filtering/selection preservation, per-section choices and presets, UI routing and minimum-size renders; full safe suites and EXE-only Plan. No actual installs, registry writes or scheduled task registration.
