using System;
using System.Collections.Generic;
using System.IO;

namespace Freshkit
{
    internal static class ExtrasTests
    {
        public static void Run(Action<bool, string> check)
        {
            bool custom;
            string builtin = Engine.CurrentListText(out custom);
            var catalogKeys = new List<string>();
            foreach (var p in ListFile.Parse(builtin).Packages) catalogKeys.Add(Data.KeyFor(p.Source, p.Id));

            // Dependencies
            var map = AppDependencies.Parse(builtin);
            check(map.ContainsKey("winget:OpenRGB.OpenRGBEffectsPlugin") && map["winget:OpenRGB.OpenRGBEffectsPlugin"].Contains("openrgb.install") && map.ContainsKey("streamlink-twitch-gui"), "Catalog ::REQUIRES lines parse into app dependencies");
            var needed = AppDependencies.Missing("winget:OpenRGB.OpenRGBEffectsPlugin", new List<string>(), catalogKeys, map);
            check(needed.Count == 2 && needed.Contains("openrgb.install") && needed.Contains("pawnio"), "The OpenRGB Effects Plugin needs OpenRGB and, through it, PawnIO");
            check(AppDependencies.Missing("winget:OpenRGB.OpenRGBEffectsPlugin", new List<string> { "openrgb.install" }, catalogKeys, map).Count == 1, "Already selected dependencies are not repeated, but their own needs still are");
            var synthetic = AppDependencies.Parse("::BEGIN_REQUIRES\n::REQUIRES a|b,missing,a\n::REQUIRES b|a\n::END_REQUIRES\n");
            var cyclic = AppDependencies.Missing("a", new List<string>(), new List<string> { "a", "b" }, synthetic);
            check(cyclic.Count == 1 && cyclic[0] == "b", "Dependencies ignore self-references, apps outside the catalog and cycles");
            check(AppDependencies.Parse("::BEGIN_PACKAGES\n::END_PACKAGES\n").Count == 0, "A catalog without a REQUIRES section has no dependencies");

            // Command-line recipes
            check(CliRecipe.Accepts("install", new[] { "install", "x.json" }) && CliRecipe.Accepts("Plan", new[] { "Plan", "x.json" }) && !CliRecipe.Accepts("Install", new[] { "Install" }) && !CliRecipe.Accepts("Backup", new[] { "Backup", "x.zip" }), "Install, Plan and List take a recipe path; other commands keep their arguments");
            string dir = Path.Combine(Path.GetTempPath(), "freshkit-extras-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var recipe = new SetupRecipe { ListText = builtin, Apps = new List<string> { "winget:7zip.7zip", "openrgb.install" }, Tweaks = new List<string> { "DisableBing" } };
                string path = Path.Combine(dir, "setup.freshkit.json");
                File.WriteAllText(path, recipe.Serialize());
                var loaded = CliRecipe.Load(path, builtin);
                check(loaded.Apps.Count == 2 && loaded.ListText == builtin, "A command-line recipe loads its apps and catalog");
                check(CliRecipe.Notice(loaded).Contains("2 apps") && CliRecipe.Notice(loaded).Contains("not applied from the command line"), "The command line says that recipe Windows selections are not applied");
                string empty = Path.Combine(dir, "empty.freshkit.json");
                File.WriteAllText(empty, new SetupRecipe { ListText = builtin }.Serialize());
                bool threw = false; try { CliRecipe.Load(empty, builtin); } catch (InvalidDataException) { threw = true; }
                check(threw, "A recipe with no apps is rejected on the command line");
                threw = false; try { CliRecipe.Load(Path.Combine(dir, "nope.json"), builtin); } catch (FileNotFoundException) { threw = true; }
                check(threw, "A missing recipe file is reported");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }

            // Estimates
            var big = SetupEstimate.For("winget:Microsoft.VisualStudio.BuildTools", "Build Tools");
            var typical = SetupEstimate.For("winget:7zip.7zip", "7-Zip");
            var store = SetupEstimate.For("winget:9NKSQGP7F2NH", "WhatsApp");
            check(big.Bytes > 2000L * 1024 * 1024 && typical.Bytes == 100L * 1024 * 1024 && store.Bytes > typical.Bytes, "Estimates use known sizes for large installers and typical sizes otherwise");
            string text = SetupEstimate.Describe(new List<AppEstimate> { big, typical }, true);
            check(text.StartsWith("Estimate: about ") && text.Contains("GB to download") && text.Contains("typical sizes"), "The review estimate gives a time range and download size: " + text);
            typical.Measured = true;
            check(SetupEstimate.Describe(new List<AppEstimate> { big, typical }, true).Contains("1 of 2 sizes measured"), "Measured sizes are labelled");
            check(SetupEstimate.Describe(new List<AppEstimate>(), true) == "" && SetupEstimate.Size(5L * 1024 * 1024) == "5 MB" && SetupEstimate.Size(1536L * 1024 * 1024) == "1.5 GB", "Sizes format as MB or GB, and nothing selected means no estimate");
            string show = "Found 7-Zip [7zip.7zip]\r\nVersion: 25.01\r\nInstaller:\r\n  Installer Type: exe\r\n  Installer Url: https://www.7-zip.org/a/7z2501-x64.exe\r\n  Installer SHA256: abc\r\n";
            check(SetupEstimate.InstallerUrl(show) == "https://www.7-zip.org/a/7z2501-x64.exe" && SetupEstimate.InstallerUrl("Installer Url: http://insecure.example/x.exe") == null, "The installer URL is read from winget show output (https only)");

            // Portable export
            string list = "::BEGIN_PACKAGES\r\n::DISABLED vlc|VLC|\r\n::DISABLED git.install|Git|/NoGitLfs /Path:\"C:\\A & B\"\r\n::END_PACKAGES\r\n::BEGIN_WINGET\r\n::DISABLED 7zip.7zip|7-Zip|\r\n::DISABLED Mozilla.Firefox|Firefox|-ms\r\n::DISABLED 9NKSQGP7F2NH|WhatsApp|\r\n::END_WINGET\r\n::BEGIN_GITHUB\r\n::DISABLED owner/Tool|Tool|\r\n::END_GITHUB\r\n::BEGIN_CONFIG\r\n::END_CONFIG\r\n";
            bool listError = false; try { ListFile.Parse(list); } catch (InvalidDataException) { listError = true; }
            if (listError) list = list.Replace("/NoGitLfs /Path:\"C:\\A & B\"", "/NoGitLfs");
            var export = PortableExport.Build(list, new List<string> { "git.install", "winget:7zip.7zip", "winget:Mozilla.Firefox", "winget:9NKSQGP7F2NH", "github:owner/Tool" }, new DateTime(2026, 11, 27, 12, 0, 0));
            check(export.Winget.Count == 2 && export.Store.Count == 1 && export.Choco.Count == 1 && export.Skipped.Count == 1 && export.OptionsDropped.Count == 1 && !export.Choco.Contains("vlc"), "Only selected apps are exported, sorted by source; GitHub releases are skipped");
            check(export.WingetJson.Contains("\"PackageIdentifier\":\"7zip.7zip\"") && export.WingetJson.Contains("\"Name\":\"msstore\"") && export.WingetJson.Contains("winget-packages.schema.2.0.json"), "The winget file uses winget's import format with winget and msstore sources");
            check(export.ChocoConfig.Contains("<package id=\"git.install\" packageParameters=\"/NoGitLfs") && export.ChocoConfig.StartsWith("<?xml"), "The Chocolatey packages.config keeps package parameters");
            var back = SetupRecipe.Parse(export.WingetJson, list);
            check(back.Apps.Count == 3 && back.Apps.Contains("winget:9NKSQGP7F2NH") && !back.HasWindowsSelections, "The exported winget file imports back into Freshkit");
            check(PortableExport.Build(list, new List<string> { "github:owner/Tool" }, DateTime.Now).WingetJson == null, "Nothing exportable produces no files");

            // Recipe gaps
            var report = new AuditReport();
            report.Rows.Add(new AuditRow { Category = "Apps", Key = "vlc", Status = "Missing" });
            report.Rows.Add(new AuditRow { Category = "Apps", Key = "winget:7zip.7zip", Status = "Unknown" });
            report.Rows.Add(new AuditRow { Category = "Apps", Key = "git.install", Status = "Verified" });
            report.Rows.Add(new AuditRow { Category = "Windows", Key = "DisableBing", Status = "Different" });
            var missing = RecipeGaps.MissingApps(report);
            check(missing.Count == 1 && missing[0] == "vlc" && RecipeGaps.MissingApps(null).Count == 0, "Only apps reported Missing are selected from a recipe comparison; Unknown stays unticked");

            // Build Tools workloads
            const string buildTools = "Microsoft.VisualStudio.BuildTools";
            var workloads = AppOptions.Workloads("winget", buildTools);
            check(workloads.Count == 12 && workloads.Exists(w => w.Flag == "Microsoft.VisualStudio.Workload.NetCrossPlatBuildTools") && AppOptions.Workloads("winget", "Microsoft.VisualStudio.2022.BuildTools").Exists(w => w.Flag == "Microsoft.VisualStudio.Workload.XamarinBuildTools") && AppOptions.Workloads("choco", buildTools).Count == 0, "Build Tools 2026 and 2022 offer their own workload lists");
            PackageEntry entry = ListFile.Parse(builtin).Packages.Find(p => p.Id == buildTools);
            check(AppOptions.HasWorkload(entry.Parameters, "Microsoft.VisualStudio.Workload.VCTools") && AppOptions.HasToken(entry.Parameters, "--includeRecommended") && !AppOptions.HasWorkload(entry.Parameters, "Microsoft.VisualStudio.Workload.WebBuildTools"), "The catalog's Build Tools default selects the C++ workload with recommended components");
            string changed = AppOptions.WorkloadArguments(buildTools, entry.Parameters + " --add Microsoft.VisualStudio.Component.Git", new[] { "Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools", "Microsoft.VisualStudio.Workload.WebBuildTools" }, false);
            check(changed == "--quiet --wait --norestart --nocache --add Microsoft.VisualStudio.Component.Git --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --add Microsoft.VisualStudio.Workload.WebBuildTools", "Workload choices replace earlier workloads and keep silent switches and single components: " + changed);
            check(AppOptions.WorkloadArguments(buildTools, "", new string[0], true) == AppOptions.BuildToolsSilent && AppOptions.Validate("winget", buildTools, changed) == null, "No workload means only the silent switches, and generated arguments pass validation");
            check(AppOptions.Help("winget", buildTools).Contains("workloads"), "Build Tools help explains workloads");

            // Versions
            var btVersions = AppOptions.Versions("winget", "Microsoft.VisualStudio.2022.BuildTools");
            check(btVersions.Count == 3 && btVersions[0].PackageId == buildTools && btVersions[1].DisplayName == "Visual Studio Build Tools 2022" && AppOptions.Versions("choco", "python313").Count == 5 && AppOptions.Versions("winget", "7zip.7zip").Count == 0, "Build Tools and Python 3 offer version choices; other apps don't");
            check(AppOptions.SameVersionFamily("choco", "python3", "python312") && !AppOptions.SameVersionFamily("winget", "python3", "python312") && AppOptions.CanonicalVersionId("winget", "Microsoft.VisualStudio.2019.BuildTools") == buildTools && AppOptions.CanonicalVersionId("choco", "vlc") == "vlc", "Version families stay within one source and name their catalog default");
            string to2022 = AppOptions.SwitchBuildToolsVersion(buildTools, "Microsoft.VisualStudio.2022.BuildTools", AppOptions.BuildToolsSilent + " --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Workload.NetCrossPlatBuildTools --includeRecommended");
            check(to2022.Contains("--add Microsoft.VisualStudio.Workload.VCTools") && to2022.Contains("XamarinBuildTools") && !to2022.Contains("NetCrossPlat") && to2022.EndsWith("--includeRecommended"), "Switching Build Tools to 2022 keeps workloads and maps .NET MAUI to its 2022 ID: " + to2022);
            check(AppOptions.Workloads("winget", "Microsoft.VisualStudio.2019.BuildTools").Count == 0 && AppOptions.SwitchBuildToolsVersion(buildTools, "Microsoft.VisualStudio.2019.BuildTools", "x y") == "x y" && AppOptions.Help("winget", "Microsoft.VisualStudio.2019.BuildTools").Contains("2019"), "Build Tools 2019 keeps its arguments as free text (no verified workload list)");
            try
            {
                Engine.SessionList = builtin.Replace("::REQUIRES openrgb.install|pawnio", "::REQUIRES openrgb.install|pawnio\r\n::REQUIRES streamlink|python3");
                ListFile.SetPackageVersionOptions("choco", "python3", "python313", "", "Python 3.13");
                var switched = ListFile.Parse(Engine.SessionList).Packages.Find(p => p.Id == "python313");
                check(switched != null && switched.Name == "Python 3.13" && !ListFile.Parse(Engine.SessionList).Packages.Exists(p => p.Id == "python3") && Engine.SessionList.Contains("::REQUIRES streamlink|python313"), "Switching versions renames the entry and updates dependency keys");
                bool refused = false; try { ListFile.SetPackageVersionOptions("choco", "vlc", "python313", "", null); } catch (InvalidDataException) { refused = true; }
                check(refused, "Only versions of the same app can be switched");
            }
            finally { Engine.SessionList = null; }

            // Recipes from older catalogs keep newer built-in apps available
            string old = System.Text.RegularExpressions.Regex.Replace(builtin, @"(?m)^::DISABLED (Microsoft\.VisualStudio\.BuildTools|OpenRGB\.OpenRGBEffectsPlugin)\|.*\r?\n", "");
            old = System.Text.RegularExpressions.Regex.Replace(old, @"(?ms)^::BEGIN_REQUIRES.*?^::END_REQUIRES\r?\n", "");
            old = System.Text.RegularExpressions.Regex.Replace(old, @"(?m)^::PROFILE Starter: developer\|.*\r?\n", "");
            old = old.Replace("::DISABLED python3|Python 3|", "::DISABLED python313|Python 3.13|");
            int added;
            string merged = CatalogMerge.AddMissing(old, builtin, out added);
            var mergedList = ListFile.Parse(merged);
            var mergedTools = mergedList.Packages.Find(p => p.Id == buildTools);
            check(added == 2 && mergedTools != null && !mergedTools.Enabled && mergedTools.Category == "Developer" && mergedTools.Parameters.Contains("VCTools") && mergedList.Packages.Exists(p => p.Id == "OpenRGB.OpenRGBEffectsPlugin" && p.Category == "Gaming & devices"), "Applying an older recipe adds newer built-in apps, unticked, in their categories");
            check(!mergedList.Packages.Exists(p => p.Id == "python3") && mergedList.Packages.Exists(p => p.Id == "python313"), "A recipe's chosen version is not joined by the family's default version");
            check(AppDependencies.Parse(merged).ContainsKey("winget:OpenRGB.OpenRGBEffectsPlugin") && mergedList.Saved.Exists(s => s.Name == "Starter: developer"), "Missing dependencies and starter profiles are added too");
            int again; CatalogMerge.AddMissing(builtin, builtin, out again);
            check(again == 0, "The current catalog merges with nothing to add");
        }
    }
}
