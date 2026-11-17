# Guided options, planned tweaks and driver preview

Scope: implement the three-item next batch recommended immediately before the user's “Build your recommendations”: guided app options, current tweak state versus planned changes, and driver preview. Preserve the existing dirty work, standalone C#5/WPF/PowerShell5.1 architecture and session-only preferences.

Guided options: verified source/package-specific language and component controls, advanced arguments retained, explicit reset to defaults and a preview of the package/arguments saved. Firefox winget locales may switch only among reviewed official IDs. Changes are atomic, reject duplicate destinations, retain selections and update session-profile keys; recipes and job snapshots carry the resulting catalog. Unknown advanced switches cannot disappear when guided controls change.

Tweaks: detection describes current state and never selects planned work. Selection stays portable desired intent for recipes/profiles. Show current and next-setup labels separately. Build jobs from selected settings that are not already verified applied; preserve all selected intent in profiles/recipes. Multi-feature choices skip only if the desired full option is confirmed current. Unknown settings remain actionable. Counts/review show pending and already-current selections, and confirmation cannot introduce unreviewed work if detection changes while review is open.

Driver preview: read-only asynchronous hardware/package table accessible from Drivers and setup review, and shown before driver-only installation. Distinguish actual installed device-driver version from available package version; they are not equivalent schemes. Surface incomplete inventory/metadata errors and unsupported/OEM guidance. Never call installation paths during preview. Continue starts only the existing reviewed engine workflow; hardware is revalidated at execution.

Approach: native existing WPF controls and session catalog, focused preview script/model and existing detection model. Alternatives of universal guessed installer switches, automatically selecting currently-applied tweaks, or treating package versions as hardware-driver versions do not satisfy the intended behavior.

Verification: source-option preservation and atomic locale/profile migration; current-state/intent separation and no-op job avoidance; preview JSON/version separation, hybrid/unsupported/error states, routed UI and compact renders. Full safe regressions and standalone EXE build. No real installs, system changes, task registration, commits or pushes.
