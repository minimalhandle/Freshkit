# Plan: no defaults and setup extras

1. Catalog: untick all entries, add the two apps and logos (`tools/add-missing-icons.ps1`, append `sources.json` only),
   add `::BEGIN_REQUIRES`. Keep the file's CRLF endings.
2. UI: remove Defaults button and "List defaults" (profile indices shift by one); Runner check limited to app actions.
3. Model `src/app/SetupExtras.cs` (no UI): AppDependencies, CliRecipe, SetupEstimate, PortableExport, RecipeGaps;
   Build Tools workloads in `AppOptions.cs`. Tests in `tests/ExtrasTests.cs` (temp folder only, no network).
4. UI glue in `MainUi.Extras.cs`: starter banner, export menu item, review estimate and size check, comparison button;
   dependency ticking in `MainUi.Apps.cs`; workload checkboxes in `MainUi.AppOptions.cs`.
5. CLI recipe in `Program.cs`; check-catalog winget-pkgs fallback; `.github/workflows/catalog-health.yml`.
6. UI tests, README, `tests\run-all.ps1`, portable `Plan <recipe>` check, `git diff --check`.
