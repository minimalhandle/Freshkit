# Guided setup refinements implementation plan

Spec: ../specs/2026-11-16-guided-options-planned-tweaks-driver-preview.md
Architecture: C#5/WPF models and partial UI; embedded PowerShell5.1 read-only preview. Work in the existing supplied checkout and preserve dirty changes. User authorised implementation.

- [x] Guided options: verified language/component models, atomic locale/profile migration, advanced-switch preservation and guided/advanced live preview; focused tests and actual factory integration.
- [x] Tweak intent: failing tests; detection only changes current-state fields; pending feature calculation; separate labels and counts; job/review pending work only; preserve recipe/profile intent.
- [x] Driver preview: schema/model/read-only backend, async modal table, installed-driver versus available-package versions and compatibility links; bind Drivers and setup review, gate Continue until scan completes.
- [x] Integration: documentation, safe suites, independent review, renders at small size, standalone build/Plan; leave uncommitted.

Delegation: app_arguments owns option models/editor/catalog editing and focused option tests; drivers owns preview model/script/partial UI and focused backend tests. Root owns tweak model, main XAML/Pages/Review integration, extraction resources and broad UI tests.

Ruling: implement the explicitly recommended next batch of three refinements. The other five optional ideas from the earlier list remain future suggestions.

User confirmed this three-item scope. Completed 2026-11-16; changes left uncommitted.

Verification: 121 UI checks with rendered options, tweaks, review and driver preview at minimum sizes; 41 core checks; 43 app-option checks; 10 driver-preview model checks; 19 synthetic preview backend checks; 42 existing driver checks. App-options payload, tweak state, job status, engine progress, settings roundtrip, identity and schedule payload suites passed. All 35 project PowerShell files parsed and git diff --check passed. Final build succeeded; copied only Freshkit.exe into obj/standalone-review and ran Plan with exit 0. No real package installations, tweak writes or task registration were performed. Real installer/hardware/reboot behavior remains for VM or hardware validation.

Independent review found a mixed current/pending detection refresh could add work to an already-enabled review. Added a failing regression, then fixed review to clear tweak inclusion when its pending set grows; explicit re-selection accepts the updated list. Also added a failing regression and defensive job-construction guard for driver scans still in progress after preview closure. Both fixes passed and reviewer confirmed no remaining material findings.
