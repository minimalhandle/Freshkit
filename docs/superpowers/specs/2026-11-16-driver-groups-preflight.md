# Installer groups and setup preflight

User request: replace repeated driver-device cards with one installer and its covered devices, then build the previously proposed preflight checks.

Use the existing native C#5/WPF/.NET Framework architecture and session-only preferences. Group only nonempty package identities, case-insensitively; unmatched hardware retains separate guidance. Keep installed INF versions on each device, package versions on the installer, and preserve compatibility caveats. Device lists are collapsed by default. The setup review uses a compact package summary rather than repeating every device.

Preflight is a read-only background scan in setup review with visible results and Recheck. Check system/temp drive free space (low-space guidance is a heuristic, not an installer size estimate), standard pending-restart signals, the required sources' HTTPS endpoints, winget availability when required, and processes associated with settings restoration. Expose unknown checks as warnings. Do not close processes, reboot, elevate, repair sources, or change selections automatically. Wait for scanning to finish before starting. Re-evaluate results when review options change; refresh the scan on review opening and explicitly with Recheck. Expired results require a new scan. The existing engine remains authoritative at execution.

Approach: aggregate existing device rows in the presentation model and evaluate an immutable preflight snapshot against current selections. Alternatives of grouping by vendor (merges distinct installers) and automatically fixing prerequisites (unreviewed system changes) do not fit.

Verification: synthetic many-device/mixed-package/unmatched fixtures, preflight low-space/unknown/restart/source/process scenarios, UI review gating/recheck/changed options, safe regression scripts, compact renders, standalone EXE build. Preserve pre-existing work; no installs or Windows changes in verification.
