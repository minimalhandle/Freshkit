# Post-setup verification and setup comparison

User requests the two previously proposed features: confirm requested app presence and Windows device problems after setup; compare a recipe to this PC for missing apps, different Windows settings and unavailable backup settings.

Use one read-only audit scanner and evaluator with explicit Verified, Missing, Different, Attention and Unknown results. No automatic changes or repairs. Compare the current selection or explicitly choose a saved Freshkit/winget recipe without applying it. Verify only the actual job target after execution/restore finishes, preserving the original target across retries/restarts. Results remain session-only; they do not change the installer report, retry list or cleanup rules. Current detected versions do not prove requested installer arguments or latest versions were applied.

Apps use exact package identities or the existing unique desktop-name evidence, clearly labelled; GitHub still requires repository confirmation. Inventory failures/truncation/ambiguity must not become missing/success. Windows tweaks use existing read-only definitions; unsupported actions stay Unknown. Removed apps use exact current-user Appx/winget identities including reviewed aliases. Backup comparison checks the recipe's own selected profile definitions/locations without modifying the active catalog. Device checks inspect present device problem codes and distinguish disabled devices from missing drivers. Pending restart and incomplete setup stages remain visible.

Native WPF details window has a fixed footer, a scrollable result list, issue filter and Recheck. Progress shows a compact verification summary and access to results. Refreshing, closing, a new job or a new comparison must invalidate stale scan callbacks.

Approach: shared snapshot/evaluator with focused inventory script, existing package/registry identity helpers, and a shared results window. Separate scanners would duplicate identity logic; automatically installing differences would bypass setup review.

Verification: synthetic app identities, incomplete inventories, tweaks/removal aliases, recipe-only backup rules, device problems, frozen retry targets, lifecycle gating and minimum-size renders. Safe full regressions and normal dist/Freshkit.exe build; no actual installs or system changes.
