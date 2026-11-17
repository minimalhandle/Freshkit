using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Freshkit
{
    internal sealed partial class MainUi
    {
        bool tweaksScanned, tweakScanActive;
        int tweakStateEpoch;

        bool IsTweakScanCurrent(int epoch) { return epoch == tweakStateEpoch && !running; }

        void OnTweakSectionClick(object sender, RoutedEventArgs e)
        {
            if (running) return;
            var button = e.OriginalSource as Button; var category = button == null ? null : button.DataContext as TweakCategory;
            if (category == null || (button.Tag as string != "tweak-select-all" && button.Tag as string != "tweak-clear")) return;
            debloat.SelectCategory(category, button.Tag as string == "tweak-select-all", true);
            RefreshTweakCounts();
        }

        void OpenTweakQuickSelect()
        {
            var menu = new ContextMenu { PlacementTarget = Get<Button>("BtnTweakQuickSelect"), Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var privacy = new MenuItem { Header = "Privacy and suggested content", Tag = "PrivacySuggestedContent" };
            var ai = new MenuItem { Header = "AI features", Tag = "AI" };
            foreach (var item in new[] { privacy, ai })
            {
                var preset = item; preset.IsEnabled = !running;
                preset.Click += delegate { ApplyTweakQuickSelect((string)preset.Tag); };
                menu.Items.Add(preset);
            }
            menu.IsOpen = true;
        }

        void ApplyTweakQuickSelect(string id)
        {
            if (running || debloat == null) return;
            Get<TextBox>("TweakSearch").Text = "";
            debloat.SelectCategoryById(id, true); RefreshTweakCounts();
        }

        void RefreshTweakState()
        {
            if (debloat == null || tweakScanActive || running) return;
            tweaksScanned = true; tweakScanActive = true;
            int epoch = tweakStateEpoch;
            Get<Button>("BtnTweakRefresh").IsEnabled = false;
            Get<TextBlock>("TweakScanStatus").Text = "Checking current settings and installed apps…";
            new Thread(delegate()
            {
                string work = null; object snapshot = null; var errors = new List<string>();
                var winget = new List<string>(); bool wingetComplete = false;
                try
                {
                    work = Engine.ExtractWork(); string vendor = Engine.ExtractDebloat(work); string output;
                    int code = Proc.Capture(Engine.PowerShellPath, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Engine.Q(Path.Combine(work, "TweakState.ps1")) + " -DebloatRoot " + Engine.Q(vendor), out output, 45000);
                    if (code != 0) throw new IOException("The current-settings scan could not finish.");
                    snapshot = Engine.Json.DeserializeObject(output.Trim().TrimStart('\uFEFF'));
                    if (J.Get(snapshot, "Features") == null) throw new IOException("The current-settings scan returned incomplete data.");
                    foreach (var error in J.A(snapshot, "Errors")) errors.Add(Convert.ToString(error));
                }
                catch (Exception ex) { errors.Add(ex.Message); }
                finally { Engine.Cleanup(work); }
                try
                {
                    string executable = Engine.WingetExe(), output;
                    if (executable == null) throw new IOException("winget inventory is unavailable.");
                    int code = Proc.Capture(executable, "list --accept-source-agreements --disable-interactivity", out output, 25000);
                    var rows = Sources.ParseTable(output);
                    if (code == 0 && rows.Count > 0)
                    {
                        foreach (var row in rows) { string id; if (row.TryGetValue("Id", out id) && !id.Contains("…") && !id.EndsWith("...", StringComparison.Ordinal)) winget.Add(id); }
                        wingetComplete = true;
                    }
                    else throw new IOException("winget inventory could not be read.");
                }
                catch (Exception ex) { errors.Add(ex.Message); }
                if (!Window.Dispatcher.HasShutdownStarted) Ui(delegate
                {
                    if (!IsTweakScanCurrent(epoch))
                    {
                        tweakScanActive = false; tweaksScanned = false;
                        Get<Button>("BtnTweakRefresh").IsEnabled = !running;
                        Get<TextBlock>("TweakScanStatus").Text = "Settings changed during detection; a fresh check is needed.";
                        if (!running) RefreshTweakState();
                        return;
                    }
                    var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var features = J.Get(snapshot, "Features") as Dictionary<string, object>;
                    if (features != null) foreach (var pair in features) if (pair.Value is string) states[pair.Key] = (string)pair.Value;
                    var appx = new List<string>(); foreach (var name in J.A(snapshot, "Appx")) appx.Add(Convert.ToString(name));
                    CompleteTweakDetection(states, appx, J.B(snapshot, "AppxComplete"), winget, wingetComplete, errors);
                });
            }) { IsBackground = true }.Start();
        }

        void CompleteTweakDetection(IDictionary<string, string> states, List<string> appx, bool appxComplete, List<string> winget, bool wingetComplete, List<string> errors)
        {
            tweakScanActive = false;
            Get<Button>("BtnTweakRefresh").IsEnabled = !running;
            if (running) { tweaksScanned = false; Get<TextBlock>("TweakScanStatus").Text = "Detection deferred until the current job finishes."; return; }
            bool previous = applyingProfile; applyingProfile = true;
            try { debloat.ApplyDetection(states); debloat.DetectInstalledApps(appx, appxComplete, winget, wingetComplete); FilterTweaks(); RefreshTweakCounts(); if (Get<Grid>("ReviewSheet").Visibility == Visibility.Visible) RefreshReviewTweaks(); }
            finally { applyingProfile = previous; }
            int unknown = 0; foreach (var app in debloat.Apps) if (!app.Installed.HasValue) unknown++;
            Get<TextBlock>("TweakScanStatus").Text = "Current state checked. Choose changes for the next setup; settings already applied are skipped." + (unknown > 0 ? " " + unknown + " app states unknown; those remain visible." : "") + (errors.Count > 0 ? " Some checks unavailable." : "");
            Get<TextBlock>("TweakScanStatus").ToolTip = errors.Count == 0 ? "Read-only inspection of the embedded Win11Debloat definitions. Unsupported states are Unknown." : string.Join("\n", errors.ToArray());
        }
    }
}
