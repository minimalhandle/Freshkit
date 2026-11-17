using System;
using System.Collections.Generic;
using System.Windows;

namespace Freshkit
{
    internal static class TweakTests
    {
        public static void Run(Action<bool, string> check)
        {
            var d = new Debloat(); var ai = new TweakCategory { Id = "AI", Name = "AI" }; var other = new TweakCategory { Id = "System", Name = "System" };
            var one = new TweakToggle { FeatureId = "One", Label = "One" }; var hidden = new TweakToggle { FeatureId = "Hidden", Visibility = Visibility.Collapsed };
            var choice = new TweakChoice { Options = new List<string> { "Don't change", "Disable", "Enable" }, Values = new List<string[]> { new[] { "Disable", "AlsoDisable" }, new[] { "Enable" } } };
            ai.Toggles.Add(one); ai.Toggles.Add(hidden); ai.Choices.Add(choice); other.Toggles.Add(new TweakToggle { FeatureId = "Other" }); d.Categories.Add(ai); d.Categories.Add(other);
            d.SelectCategory(ai, true, true);
            check(one.IsSelected && !hidden.IsSelected && choice.SelectedIndex == 1 && !other.Toggles[0].IsSelected, "Section Select all uses visible controls and only one mutually exclusive option");
            d.SelectCategory(ai, false, true);
            check(!one.IsSelected && choice.SelectedIndex == 0, "Section Clear returns its choices to Don't change");
            d.SelectCategoryById("AI", true);
            check(one.IsSelected && hidden.IsSelected && choice.SelectedIndex == 1 && !other.Toggles[0].IsSelected, "Quick select uses the stable vendor category and preserves unrelated settings");
            var fresh = new Debloat(); var group = new TweakCategory(); var toggle = new TweakToggle { FeatureId = "Applied" }; group.Toggles.Add(toggle); fresh.Categories.Add(group);
            fresh.ApplyDetection(new Dictionary<string, string> { { "Applied", "Applied" } });
            check(!toggle.IsSelected && toggle.StateText == "Applied", "Detection reports current state without selecting planned work");
            toggle.IsSelected = true;
            check(fresh.SelectedFeatures().Contains("Applied") && fresh.PendingFeatures().Count == 0 && fresh.BuildArguments(false, false) == null, "Already-applied selected intent remains portable but produces no redundant job");
            fresh.ApplyDetection(new Dictionary<string, string> { { "Applied", "Unknown" } });
            check(fresh.PendingFeatures().Contains("Applied") && fresh.BuildArguments(false, false).Contains("-Applied"), "An unverifiable desired setting remains eligible for application");
            fresh.ApplyDetection(new Dictionary<string, string> { { "Applied", "Applied" } });
            fresh.InvalidateDetection();
            check(toggle.IsSelected && toggle.StateText == "Unknown" && fresh.PendingFeatures().Contains("Applied"), "A system-changing job invalidates old detection without discarding desired settings");
            toggle.IsSelected = false;
            fresh.ApplyDetection(new Dictionary<string, string> { { "Applied", "Applied" } });
            check(!toggle.IsSelected && toggle.StateText == "Applied", "Detection refresh never replaces an explicit user selection");
            fresh.ApplyDetection(new Dictionary<string, string>());
            check(toggle.StateText == "Unknown", "Missing detection results are explicitly unknown");
            d.ApplyDetection(new Dictionary<string, string> { { "Disable", "Applied" }, { "AlsoDisable", "Applied" }, { "Enable", "Not applied" } });
            check(choice.SelectedIndex == 1 && choice.CurrentIndex == 1 && !d.PendingFeatures().Contains("Disable"), "A verified current multi-feature option is omitted from pending changes");
            choice.SelectedIndex = 2;
            check(d.PendingFeatures().Contains("Enable") && choice.CurrentIndex == 1, "Choosing a different option keeps current state distinct from planned intent");
            var installed = new RemovalApp { AppId = "Known", Name = "Known", Installed = true, IsSelected = true };
            var absent = new RemovalApp { AppId = "Absent", Name = "Absent", Installed = false, IsSelected = true };
            var unknown = new RemovalApp { AppId = "Unknown", Name = "Unknown" };
            fresh.Apps.Add(installed); fresh.Apps.Add(absent); fresh.Apps.Add(unknown); fresh.FilterRemoval("", true);
            check(installed.Visibility == Visibility.Visible && absent.Visibility == Visibility.Collapsed && unknown.Visibility == Visibility.Visible && absent.IsSelected, "Installed-only removal filtering preserves selections and exposes uncertain scan results");
            var appxOnly = new RemovalApp { AppId = "Same.Id", Name = "Appx", RemovalMethod = "Appx" };
            var win32Only = new RemovalApp { AppId = "Desktop.Id", Name = "Desktop", RemovalMethod = "WinGet" };
            fresh.Apps.Add(appxOnly); fresh.Apps.Add(win32Only);
            fresh.DetectInstalledApps(new List<string> { "Desktop.Id" }, true, new List<string> { "Same.Id" }, true);
            check(appxOnly.Installed == false && win32Only.Installed == false, "Installed-removal detection never confuses Appx and desktop package identities");
            var catalog = Debloat.Load();
            var edge = catalog.Apps.Find(a => a.AppIds.Contains("Microsoft.Edge"));
            check(edge != null && edge.AppIds.Contains("XPFFTQ037JWMHS") && edge.AppId != "System.Object[]", "Vendor removable-app aliases remain exact package IDs");
            edge.IsSelected = true;
            check(catalog.SelectedApps().Contains("Microsoft.Edge") && catalog.SelectedApps().Contains("XPFFTQ037JWMHS"), "Selecting an aliased removal entry includes its defined identifiers");
        }
    }
}
