# Freshkit UI first pass

Authorized scope: the recommended first pass accepted in chat: polish Add apps, improve contrast and badges, and expand the install preview into a setup review.

Keep native WPF, C# 5, .NET Framework 4.x, the portable EXE and session-only preferences. Retain the dark palette and existing job engine. No new services or dependencies.

Approach: improve existing screens and reuse the install options in a dedicated review sheet. A new dashboard or wholesale redesign would add navigation without helping this flow.

Search: centered initial, loading, no-results and error states; retry action; source and package details on results; invalidate earlier requests on new searches or close. Escape closes and returns focus.

Apps: brighter muted text; package ID on its own line; explicit source and Installed badges that fit within cards; selected counts include detected installed apps without implying unscanned apps are absent.

Review: both preview and install open the same scrollable summary of selected apps, tweaks and removals. Show editable links to their pages and the existing options for drivers, upgrades, offline kit, restore point and settings restore. Start uses those options and the existing JobSpec engine. Opening, editing or cancelling cannot run setup. Make skipped sections explicit, guard an empty plan, and fit 1080x680.

Verify: synthetic WPF integration checks for search request ordering/close, review selection and JobSpec options without starting jobs; existing safe tests; build and rendered screens at normal/minimum size.
