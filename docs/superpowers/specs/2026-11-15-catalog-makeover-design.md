# Freshkit catalog expansion and Apps makeover

Scope (user request): review changes since the last session; review the UI and propose/make a makeover; keep a single portable EXE; add every ninite.com app; review default selections.

Catalog: all Ninite apps mapped to verified Chocolatey IDs (checked against community.chocolatey.org). Arch variants folded to one x64 entry; Node 22/24 → nodejs-lts. ChatGPT and Windows App via winget msstore product IDs (engine picks `msstore` for 12-char 9…/X… IDs). `::GROUP` lines in the list define categories; Runner ignores them.

Defaults: the original curated 40 stay ticked except EOL .NET 7; Ninite additions are unticked. Four starter profiles change apps only (no tweaks=/remove=).

UI: category rail with counts (All, Selected, categories), responsive card width (DynamicResource AppCardWidth from viewport), compact cards showing only non-Chocolatey source badges, one toolbar (search, profile, ⋯ menu), Defaults/Clear in the footer. Bundled logos for 156 of 164 catalog apps (8 use letter tiles plus online lookup).

Portability: logos and catalog are embedded resources; verified Plan runs from a folder containing only Freshkit.exe.
