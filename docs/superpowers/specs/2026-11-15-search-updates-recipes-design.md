# App identity, search, updates and recipes

Intent: implement the four recommended next steps accepted by the user with “Do it”. Keep the portable WPF/C# 5 and Windows PowerShell 5.1 architecture and the existing uncommitted icon changes.

Approach: extend existing models and UI in place. A new installed-app snapshot resolves exact source/package IDs and repository URLs; GitHub name-only matches are tentative until explicitly linked by the user. Explicit links identify a particular uninstall entry and its stable metadata, never a release-version coincidence. GitHub updates and the PowerShell updater use the same identity rules. A successful explicit installation records its association for future checks.

Alternatives: keeping the release-version heuristic fails when an installed app falls behind; adding a package service or database would compromise the standalone architecture. Choose local registry evidence and a small operational identity record instead.

Search: show results after each source completes, label incomplete searches, use a common deadline, and cancel HTTP requests/processes when a search is replaced or closed. Snapshot installed metadata once per search. Avoid sharing mutable ranking results between publications. Fresh update checks bypass cached GitHub releases; failed release lookups are not cached forever.

Updates: aggregate independent source checks concurrently. Distinguish complete/current, updates available, and incomplete checks including zero results. Never declare success when a source failed. Name-only GitHub matches require linking before automatic updates.

Recipes: explicit export/import through the Apps menu. A versioned JSON document stores the session list and selected apps, tweaks and removals. Import validates the whole document before changing session state, previews its effect and requires an explicit Apply action. It never runs setup. Support winget export JSON as an app-only import, preserving current tweaks. No automatic file loading, preference saving, installer downloads, or embedded credentials/backups. Imported selections must resolve to validated entries; invalid/unknown schema versions leave the session untouched.

Validation: synthetic identity collisions, repository links, changed registration metadata, failed update sources, cancellation and progressive source completion, recipe round trips and malformed imports; all existing safe suites; standalone build and rendered UI. No live installs, updates, task registration or reboots.

Execution: user authorised implementation. Work inline in the supplied dirty workspace, preserve the existing edits, and leave changes reviewable without committing/pushing. No additional implementation approval is needed under the session authorisation instructions.
