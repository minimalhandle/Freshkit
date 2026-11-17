# App identity, search, updates and recipes implementation plan

> Use superpowers:executing-plans inline. User requested execution; preserve the supplied dirty workspace and do not commit or push.

**Goal:** reliable installed identities and update outcomes, responsive universal search, explicit portable recipes.

**Architecture:** focused C# models integrated into existing partial UI; shared JSON identity evidence read by C# and PowerShell.

**Tech Stack:** C# 5, .NET Framework 4.x, WPF, PowerShell 5.1.

**Spec:** ../specs/2026-11-15-search-updates-recipes-design.md

## Constraints and review focus
- Preserve existing icon changes and session-only defaults.
- No installs, updates or task registration during validation.
- Same-name projects and older installed versions cannot imply repository identity.
- Cancellation must stop source work and ignore queued UI callbacks.
- Failed zero-result checks cannot report up to date.
- Invalid recipes must not partially alter the session; winget app-only imports preserve tweaks.
- Imported scripts/parameters are data, never executed while importing.

## Tasks and execution ledger
- [x] 1. Add identity snapshot/resolution tests, observe failure, implement InstalledIdentity.cs, repository links and matching UI, and strict updater evidence. Verify C# and PowerShell identity tests.
- [x] 2. Add update outcome tests, observe failure, implement a concurrent source aggregator and explicit incomplete state; refresh release caches. Verify UI and source tests.
- [x] 3. Add progressive/cancel tests with synthetic sources, observe failure, implement a bounded search coordinator and cancellation-aware network/process operations; update search UI. Verify UI and source tests.
- [x] 4. Add recipe roundtrip, invalid-input and winget import tests, observe failure, implement SetupRecipe.cs and explicit save/open/preview actions. Verify core and UI suites.
- [x] 5. Update README, build, run safe suites, render and inspect UI, obtain a fresh whole-change review, resolve material findings and report evidence.

Ruling: implement the four-item roadmap at the end of the accepted review; the weekly updater redesign and other feature suggestions remain outside this request.

Ruling: preserve work in the provided workspace, without a worktree or git mutations, to retain existing uncommitted icon fixes and keep the result immediately reviewable.

## Completion evidence
- Identity: synthetic C# and PowerShell fixtures cover repository metadata, old installed versions, namesake rejection, explicit associations, changed registration metadata and consistent name normalization. Search offers an explicitly routed Link installed app action; the installed-app scan also requires repository evidence for GitHub entries.
- Updates: 39 core checks pass, including concurrent checks, Chocolatey source normalization/exit failures and retention of successful partial results. Fresh GitHub checks bypass the five-minute release cache.
- Search: progressive and active-source cancellation tests pass; stale and closed UI callbacks cannot repopulate results. Partial source failures are visible. Legacy icon-cache entries are ignored.
- Recipes: native roundtrip and winget import fixtures pass; schema/catalog/feature validation happens before session changes. The actual minimum-size preview keeps Apply visible, and importing stays idle until Review setup is started.
- Verification: 39 core checks, 78 rendered UI checks, job-status, engine-progress, settings-roundtrip, identity and schedule-payload suites passed. All 21 source/test PowerShell files parsed. Final standalone EXE-only Plan passed. git diff --check passed.
- Final independent review: no remaining material findings after fixes. Reviewed search identity and recipe preview renders at 1064 x 641.
- Rebuilt dist/Freshkit.exe. No real installs, app updates, task registrations, reboots, commits or pushes performed.

Ruling: expanded identity checks exceeded the existing weekly command budget, so compress large embedded payloads rather than drop GitHub apps. This preserves the standalone task and was verified at 19,032 encoded characters for both built-in GitHub apps. Existing tasks are replaced only when the user re-enables weekly updates.

No deferred material findings. Optional full offline installers, preflight checks, update exclusions/history and a standalone persistent updater remain outside the accepted four-item roadmap.