# Verification and comparison implementation plan

> **For agentic workers:** Use superpowers:executing-plans for task-by-task implementation.

**Goal:** Read-only post-setup checks and recipe/current-selection comparison.
**Architecture:** A shared immutable target and inventory snapshot feed a pure evaluator. Background scans reuse existing registry identity and tweak definitions. WPF results are independent of installer progress and retry data.
**Tech Stack:** C#5, WPF, .NET Framework, Windows PowerShell 5.1.
**Spec:** docs/superpowers/specs/2026-11-16-verification-comparison.md

## Global constraints

- Standalone EXE; no persistent preferences or automatic recipe loading.
- No installs, repairs, elevation, process termination or Windows changes during auditing.
- Preserve existing uncommitted changes and use dist/Freshkit.exe; no commits.

## Review focus

- Partial/translated/truncated package inventory must not claim missing apps.
- Same-name GitHub apps require confirmed repository identity.
- Reboot recovery and retries must retain the original verification target.
- A comparison uses the loaded recipe's backup rules, without changing session state.
- Results finishing after a new job or a closed window must be discarded.

## Task 1: Audit data, scanner and evaluator

- [x] Write failing audit model tests; run tests/run-audit.ps1.
- [x] Implement SetupAudit.cs, AuditScan.cs, AuditScan.ps1 and accurate registry completeness.
- [x] Test actual synthetic script invocation via injectable inventory and safe fixture archives; verify model tests pass.

## Task 2: Integration and UI

- [x] Write failing lifecycle, nonmutation, retry-target and UI result tests.
- [x] Preserve VerificationJob in SetupRecord; integrate tracked/nontracked completion and progress buttons.
- [x] Add comparison menu/button and shared issue-filtered details window with read-only background Recheck.
- [x] Run safe regressions, inspect compact renders, independent code review, fix important findings and build dist/Freshkit.exe.

## Verification and review record

All 16 safe test scripts passed after the final changes. This includes 134 UI/render checks and 25 audit model checks, plus the synthetic inventory script test. Minimum-size comparison, app footer and full seven-stage completed Progress renders were inspected. Normal build produced dist/Freshkit.exe; dist/updated remains absent. git diff --check passed. No commits or branch changes.

Read-only host smoke check exercised actual package/registry detection, desired Windows settings, device problems and restart signals. It correctly retained an unconfirmed GitHub identity as Unknown and distinguished disabled hardware from driver-install failures. No real installs, settings changes or reboot recovery were exercised; those execution paths still require a disposable VM for end-to-end testing.

Independent review found three P2 issues, each reproduced and fixed with regression coverage: map the scanner's actual Not applied state to Different; require complete registry inventory for name-only uniqueness; start verification on terminal tracked jobs without engine reports while avoiding reboot/UAC/active/report-owned completion paths. Additional synthetic edge cases cover case-insensitive removal aliases and unknown device presence. Full suite passed after fixes. No deferred review findings.

Implementation choice: Store product identities that cannot be established from exact local inventory remain Unknown rather than using display-name guesses. Existing recipe import validation remains authoritative for file structure; comparison never applies a recipe. Verification reports are session-only and separate from installer/retry results.
