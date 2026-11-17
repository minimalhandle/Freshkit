# Explorer icon and sidebar polish

User requests: correct the old EXE logo in Explorer, offer/select a replacement slogan, and make This PC readable with the Windows build on line two and useful extra information. Selected slogan: Fresh start. Your setup.

Evidence: ExtractIconEx directly from dist/Freshkit.exe returns the selected Modular F, so the binary resource is current and Explorer is showing a stale cached image. Notify the Shell after builds; refresh the existing Explorer view and verify the visible outcome without terminating Explorer or deleting caches.

Sidebar: separate wrapped Windows edition/release and full build/revision. Add architecture, system-drive free space and winget presence alongside Chocolatey and network status. Preserve the fixed sidebar width, allow the navigation region to scroll on short windows, and verify the complete PC panel stays on screen even with the resume banner present.

Implementation: focused system-facts model with pure formatting checks, existing runtime-loaded XAML, targeted Shell notification in build.ps1. Retain all prior dirty work; no commits/pushes or Windows setting changes.

Verification: render at minimum size with long OS text and resume state, confirm PC text and navigation remain accessible, build and inspect the icon directly, then check Explorer. Slogans are user-owned text; apply the selected phrase exactly.

Completion: 41 core checks and 98 rendered UI checks passed; the rebuilt EXE's Plan passed and git diff --check is clean. Inspected compact sidebar renders with all system facts and with a longer edition string/resume banner. build.ps1 -RefreshIconCache successfully sent Shell cache/item/folder notifications; its ordinary builds also send targeted item/folder updates. The current EXE resource was extracted directly and shows Modular F.

Visual Explorer limitation: the user's dist window was minimized. The computer-use surface exposed no working restore/activation action, so its current visible cached icon could not be inspected. Do not claim the Explorer display itself was verified. No Explorer restart, cache deletion, commits or pushes.
