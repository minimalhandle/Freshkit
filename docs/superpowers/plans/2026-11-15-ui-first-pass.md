# UI First Pass Implementation Plan

**Goal:** Polish app selection/search and provide a readable setup review.
**Architecture:** Existing XAML theme and partial MainUi; a dedicated review sheet reuses JobSpec. Search presentation and request ownership remain in the Apps controller.
**Tech Stack:** WPF, C# 5, .NET Framework 4.x.
**Spec:** docs/superpowers/specs/2026-11-15-ui-first-pass-design.md

## Global Constraints
- Native WPF, C# 5, .NET Framework 4.x; no dependencies or services.
- Portable EXE and session-only preferences.
- No install, system settings or network changes during synthetic tests.

## Review Focus
- Older searches must not replace newer results or revive a closed sheet.
- Existing selections and installed badges must remain truthful.
- Review/edit/cancel must never launch setup.
- Disabled review sections must not enter JobSpec; an empty plan cannot start.
- Long selections and minimum-size windows must scroll without hiding actions.

### Task 1: Search and app polish
- [x] Add behavioral WPF tests for request ordering and closed-sheet completion.
- [x] Implement search state presentation, retry and request ownership in MainUi.Apps.cs; improve badges/contrast in MainWindow.xaml and source labels in view models.
- [x] Run UI integration checks and inspect rendered search/cards.

### Task 2: Setup review
- [x] Add behavioral tests for review not starting a job, selection summary and options-to-JobSpec mapping.
- [x] Add review XAML and MainUi.Review.cs; consolidate preview/install into a single Review setup action; preserve all job options.
- [x] Run UI integration checks, all existing safe tests, build, and render minimum/normal window layouts.

Execution: inline in this chat. This directory is not a Git repository; preserve a source snapshot in obj/ui-before for review rather than using Git-dependent workflow scripts.

Verification: 20 UI checks, 18 core checks, job-status, engine-progress and settings-roundtrip all pass. Standalone EXE builds successfully. Rendered Apps, search and review at normal and minimum window sizes. Independent review found one keyboard-containment issue; reproduced RED, fixed by disabling the underlying surfaces and cycling Tab within sheets, verified GREEN. No deferred material findings.
