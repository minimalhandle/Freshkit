// Freshkit - starter-profile banner, portable exports, setup estimates and selecting a recipe's missing apps.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Freshkit
{
    internal sealed partial class MainUi
    {
        List<AppEstimate> reviewEstimates = new List<AppEstimate>();
        int sizeCheckGeneration;
        bool sizeCheckBusy;

        void InitExtras()
        {
            Click("BtnStarterDismiss", delegate { starterDismissed = true; UpdateCount(); });
            Click("BtnReviewSizes", delegate { CheckDownloadSizes(); });
        }

        // ---------------------------------------------------------------- starter profiles while nothing is ticked
        void BuildStarterBanner()
        {
            var panel = Get<WrapPanel>("StarterButtons");
            panel.Children.Clear();
            foreach (SavedProfile p in savedProfiles)
            {
                if (!p.Name.StartsWith("Starter: ", StringComparison.OrdinalIgnoreCase)) continue;
                string name = p.Name;
                var b = new Button { Content = name.Substring(9), Style = StyleOf("Secondary"), Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(14, 6, 14, 6), Tag = name, ToolTip = p.Apps.Count + " apps: " + name };
                b.Click += delegate { ApplyStarter(name); };
                panel.Children.Add(b);
            }
        }

        void ApplyStarter(string name)
        {
            var box = Get<ComboBox>("ProfileBox");
            int index = box.Items.IndexOf(name);
            if (index > 0) box.SelectedIndex = index;
        }

        void UpdateStarterBanner(int selected)
        {
            bool show = selected == 0 && !starterDismissed && Get<WrapPanel>("StarterButtons").Children.Count > 0;
            Get<Border>("StarterBanner").Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------------------------------------------------------- winget import / Chocolatey packages.config
        void ExportPortable()
        {
            if (running) return;
            var keys = new List<string>(); foreach (AppItem a in apps) if (a.IsSelected) keys.Add(a.Key);
            if (keys.Count == 0) { Info("Nothing selected", "Tick the apps to export first."); return; }
            bool custom;
            PortableExport export;
            try { export = PortableExport.Build(Engine.CurrentListText(out custom), keys, DateTime.Now); }
            catch (Exception ex) { Info("Couldn't export", ex.Message); return; }
            if (export.WingetJson == null && export.ChocoConfig == null) { Info("Nothing to export", "The selected apps are GitHub releases, which winget and Chocolatey can't install. Export a Freshkit recipe instead."); return; }
            var picker = new Microsoft.Win32.SaveFileDialog { Filter = "winget import file (*.winget.json)|*.winget.json", FileName = "apps.winget.json", OverwritePrompt = true, Title = "Export for winget / Chocolatey" };
            if (picker.ShowDialog(Window) != true) return;
            try
            {
                var written = new List<string>();
                string folder = Path.GetDirectoryName(picker.FileName), stem = Path.GetFileName(picker.FileName);
                if (stem.EndsWith(".winget.json", StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - 12);
                else if (stem.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - 5);
                if (export.WingetJson != null)
                {
                    string path = Path.Combine(folder, stem + ".winget.json");
                    File.WriteAllText(path, export.WingetJson, new UTF8Encoding(false));
                    written.Add(Path.GetFileName(path) + " · " + (export.Winget.Count + export.Store.Count) + " apps · winget import -i \"" + Path.GetFileName(path) + "\"");
                }
                if (export.ChocoConfig != null)
                {
                    string path = Path.Combine(folder, stem + ".packages.config");
                    File.WriteAllText(path, export.ChocoConfig, new UTF8Encoding(false));
                    written.Add(Path.GetFileName(path) + " · " + export.Choco.Count + " apps · choco install \"" + Path.GetFileName(path) + "\" -y");
                }
                var notes = new StringBuilder(string.Join("\n", written.ToArray()));
                if (export.Skipped.Count > 0) notes.Append("\n\nNot included (no winget or Chocolatey package): ").Append(string.Join(", ", export.Skipped.ToArray()));
                if (export.OptionsDropped.Count > 0) notes.Append("\n\nwinget import can't carry installer options, so these install with winget's defaults: ").Append(string.Join(", ", export.OptionsDropped.ToArray()));
                Log("Exported " + keys.Count + " selected apps for winget / Chocolatey.");
                Info("Exported", notes.ToString());
            }
            catch (Exception ex) { Info("Couldn't export", ex.Message); }
        }

        // ---------------------------------------------------------------- setup review estimate
        void RefreshReviewEstimate()
        {
            sizeCheckGeneration++; sizeCheckBusy = false;
            reviewEstimates = new List<AppEstimate>();
            foreach (AppItem a in apps) if (a.IsSelected) reviewEstimates.Add(SetupEstimate.For(a.Key, a.Name));
            bool anyWinget = reviewEstimates.Exists(e => e.Key.StartsWith("winget:", StringComparison.OrdinalIgnoreCase) && !Data.IsStoreId(e.Key.Substring(7)));
            Get<DockPanel>("ReviewEstimatePanel").Visibility = reviewEstimates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("BtnReviewSizes").Visibility = anyWinget ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("BtnReviewSizes").IsEnabled = true;
            Get<TextBlock>("ReviewEstimate").Text = SetupEstimate.Describe(reviewEstimates, File.Exists(Engine.ChocoExe));
        }

        void CheckDownloadSizes()
        {
            if (sizeCheckBusy || running) return;
            string winget = Engine.WingetExe();
            if (string.IsNullOrEmpty(winget)) { Get<TextBlock>("ReviewEstimate").Text += " winget isn't available, so sizes can't be checked."; return; }
            var targets = reviewEstimates.FindAll(e => e.Key.StartsWith("winget:", StringComparison.OrdinalIgnoreCase) && !Data.IsStoreId(e.Key.Substring(7)) && !e.Measured);
            if (targets.Count > 40) targets = targets.GetRange(0, 40);
            int generation = ++sizeCheckGeneration; sizeCheckBusy = true;
            var button = Get<Button>("BtnReviewSizes"); button.IsEnabled = false;
            Get<TextBlock>("ReviewEstimate").Text = "Checking download sizes for " + targets.Count + " winget app" + (targets.Count == 1 ? "" : "s") + "…";
            bool chocoReady = File.Exists(Engine.ChocoExe);
            new Thread(delegate()
            {
                var measured = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var target in targets)
                {
                    if (generation != sizeCheckGeneration) return;
                    long bytes = MeasureWingetDownload(winget, target.Key.Substring(7));
                    if (bytes > 0) measured[target.Key] = bytes;
                }
                Ui(delegate
                {
                    if (generation != sizeCheckGeneration) return;
                    sizeCheckBusy = false;
                    foreach (var e in reviewEstimates) { long b; if (measured.TryGetValue(e.Key, out b)) { e.Bytes = b; e.Measured = true; } }
                    Get<TextBlock>("ReviewEstimate").Text = SetupEstimate.Describe(reviewEstimates, chocoReady) + (measured.Count < targets.Count ? " " + (targets.Count - measured.Count) + " winget size" + (targets.Count - measured.Count == 1 ? " wasn't" : "s weren't") + " reported by the server." : "");
                    Get<Button>("BtnReviewSizes").IsEnabled = true;
                });
            }) { IsBackground = true }.Start();
        }

        /// Read-only: "winget show" for the installer URL, then a HEAD request for its size. 0 when unknown.
        static long MeasureWingetDownload(string winget, string id)
        {
            try
            {
                string output;
                if (Proc.Capture(winget, "show --id " + Engine.Q(id) + " --exact --source winget --accept-source-agreements --disable-interactivity", out output, 30000) != 0) return 0;
                string url = SetupEstimate.InstallerUrl(output);
                if (url == null) return 0;
                Data.EnsureModernTls();
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "HEAD"; request.Timeout = 15000; request.AllowAutoRedirect = true; request.UserAgent = "Freshkit";
                using (var response = (HttpWebResponse)request.GetResponse()) return response.ContentLength > 0 ? response.ContentLength : 0;
            }
            catch { return 0; }
        }

        // ---------------------------------------------------------------- recipe comparison: select what's missing
        void SelectMissingFromRecipe(AuditTarget target, AuditReport report, Window owner)
        {
            var missing = RecipeGaps.MissingApps(report);
            if (missing.Count == 0 || running) return;
            var recipe = new SetupRecipe { ListText = target.Recipe.ListText, Apps = missing, HasWindowsSelections = false };
            try { ApplyRecipe(recipe); }
            catch (Exception ex) { Info("Couldn't select apps", ex.Message); return; }
            Log("Selected " + missing.Count + " app" + (missing.Count == 1 ? "" : "s") + " from the recipe that weren't found on this PC. Apps with unknown status were left unticked.");
            if (owner != null) owner.Close();
            Navigate("NavApps");
        }
    }
}
