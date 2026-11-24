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
        AuditTarget postAuditTarget;
        AuditReport postAuditReport;
        bool postAuditBusy;
        int postAuditGeneration;

        void InitAudit()
        {
            Act("BtnCompareSetup", delegate { CompareCurrentSetup(); });
            Act("BtnVerifySetup", delegate { if (postAuditTarget != null && !postAuditBusy) RefreshPostAudit(postAuditTarget); });
            Act("BtnViewVerification", delegate
            {
                if (postAuditTarget != null && postAuditReport != null && !running)
                    BuildAuditWindow(postAuditTarget, postAuditReport, "Post-setup verification", true).ShowDialog();
            });
            RenderPostAudit();
        }

        void CompareCurrentSetup()
        {
            if (running) return;
            // Copy desired intent; neither inspection nor filtering applies a recipe.
            var current = CaptureRecipe();
            var recipe = SetupRecipe.Parse(current.Serialize(), current.ListText);
            BuildAuditWindow(new AuditTarget { Recipe = recipe }, null, "Setup comparison", false).ShowDialog();
        }

        void CompareRecipeFile()
        {
            if (running) return;
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Freshkit recipe or winget export (*.json)|*.json", CheckFileExists = true };
            if (picker.ShowDialog(Window) != true) return;
            try
            {
                if (new FileInfo(picker.FileName).Length > 4 * 1024 * 1024) throw new InvalidDataException("The recipe exceeds 4 MB.");
                bool custom;
                var recipe = SetupRecipe.Parse(File.ReadAllText(picker.FileName), Engine.CurrentListText(out custom));
                BuildAuditWindow(new AuditTarget { Recipe = recipe }, null, "Setup comparison", false).ShowDialog();
            }
            catch (Exception ex) { Info("Couldn't compare recipe", ex.Message); }
        }

        void InvalidateAuditResults()
        {
            postAuditGeneration++; postAuditBusy = false; postAuditReport = null;
            RenderPostAudit();
        }

        int BeginPostAudit(AuditTarget target)
        {
            postAuditGeneration++; postAuditTarget = target; postAuditReport = null; postAuditBusy = true;
            RenderPostAudit();
            return postAuditGeneration;
        }

        void CompletePostAudit(int generation, int epoch, AuditReport report)
        {
            if (generation != postAuditGeneration || epoch != tweakStateEpoch || running) return;
            postAuditReport = report; postAuditBusy = false;
            RenderPostAudit();
        }

        void RenderPostAudit()
        {
            Get<TextBlock>("VerificationSummary").Text = postAuditBusy ? "Post-setup verification · checking apps and Windows device problems…" : postAuditReport != null ? "Post-setup verification · " + postAuditReport.Summary + " · " + postAuditReport.CheckedUtc.ToLocalTime().ToString("HH:mm:ss") : postAuditTarget != null ? "Verification needs a fresh check after changes to this PC." : "Verification runs after setup finishes. Results are kept for this session.";
            Get<Button>("BtnVerifySetup").Visibility = postAuditTarget != null ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("BtnViewVerification").Visibility = postAuditReport != null ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("BtnVerifySetup").IsEnabled = !running && !postAuditBusy && postAuditTarget != null;
            Get<Button>("BtnViewVerification").IsEnabled = !running && !postAuditBusy && postAuditReport != null;
        }

        void ScanAudit(AuditTarget target, Action<AuditReport> done)
        {
            new Thread(delegate()
            {
                AuditReport report;
                try
                {
                    Debloat definitions = Debloat.Load();
                    report = SetupAudit.Evaluate(target, AuditScan.Scan(target, definitions), definitions);
                }
                catch (Exception ex)
                {
                    report = new AuditReport { CheckedUtc = DateTime.UtcNow };
                    report.Rows.Add(new AuditRow { Category = "Scan", Key = "scan-failed", Name = "Read-only scan incomplete", Status = "Unknown", Detail = ex.Message + " Recheck when the PC is ready." });
                }
                if (!Window.Dispatcher.HasShutdownStarted) Ui(delegate { done(report); });
            }) { IsBackground = true }.Start();
        }

        void RefreshPostAudit(AuditTarget target)
        {
            if (running || jobRunning || restoreRunning) return;
            int generation = BeginPostAudit(target), epoch = tweakStateEpoch;
            ScanAudit(target, report => CompletePostAudit(generation, epoch, report));
        }

        void StartPostSetupVerification(SetupRecord record)
        {
            if (record == null) return;
            ApplyPendingSteamLibraries(record);
            var job = record.VerificationJob ?? record.Recipe;
            if (job == null || string.IsNullOrEmpty(job.ListText) || (job.Action != "Install" && job.Action != "Drivers" && string.IsNullOrEmpty(job.DriverKit) && (job.DebloatArgs == null || job.DebloatArgs.Count == 0))) return;
            var target = AuditTarget.FromJob(job);
            // Device health is useful after any full app setup, including when drivers were skipped.
            if (job.Action == "Install") target.CheckDevices = true;
            target.Stages = new List<SetupStage>();
            if (record.Steps != null) foreach (var step in record.Steps)
                target.Stages.Add(new SetupStage { Id = step.Id, Name = step.Name, Status = step.Status, Detail = step.Detail });
            RefreshPostAudit(target);
        }

        // Construction alone is read-only. Loaded and explicit Recheck start background inventory.
        Window BuildAuditWindow(AuditTarget target, AuditReport initial, string title, bool postSetup)
        {
            var window = new Window { Title = title, Width = 920, Height = 700, MinWidth = 620, MinHeight = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Frozen("#13161C"), Foreground = Frozen("#E8EAED"), FontSize = 13, FontFamily = Window.FontFamily };
            if (Window.IsVisible || new System.Windows.Interop.WindowInteropHelper(Window).Handle != IntPtr.Zero) window.Owner = Window;
            var dock = new DockPanel { Margin = new Thickness(24) }; window.Content = dock;
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
            var recheck = new Button { Name = "AuditRecheck", Content = "Recheck", Style = StyleOf("Secondary"), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) }; footer.Children.Add(recheck);
            var close = new Button { Name = "AuditClose", Content = "Close", Style = StyleOf("Primary"), IsCancel = true, Padding = new Thickness(16, 8, 16, 8) }; footer.Children.Add(close); close.Click += delegate { window.Close(); };
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
            header.Children.Add(new TextBlock { Text = title, FontSize = 25, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = postSetup ? "Read-only checks against this setup's original selections. Installer results remain separate; verification never starts repairs." : "Compare desired setup against this PC. This does not apply the recipe, change selections or start installation.", Foreground = Frozen("#A7B0BF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 6) });
            header.Children.Add(new TextBlock { Text = "App detection confirms local registration, not successful launch, latest release or installer options. Unknown means the evidence is incomplete.", Foreground = Frozen("#A7B0BF"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 12) });
            var status = new TextBlock { Name = "AuditSummary", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; header.Children.Add(status);
            var issuesOnly = new CheckBox { Name = "AuditIssuesOnly", Content = "Only show items needing attention or confirmation", IsChecked = true, Style = StyleOf("Switch"), Margin = new Thickness(0, 0, 0, 12) }; header.Children.Add(issuesOnly);
            var rows = new StackPanel { Name = "AuditRows" }; dock.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            bool closed = false, busy = false; int generation = 0;
            AuditReport shown = initial;
            Action render = delegate
            {
                if (closed) return;
                rows.Children.Clear(); recheck.IsEnabled = !busy && !running;
                status.Text = busy ? "Checking installed apps, desired settings and requested inventories…" : shown == null ? "No completed scan. Select Recheck." : shown.Summary + " · " + shown.CheckedUtc.ToLocalTime().ToString("g");
                if (busy || shown == null) return;
                foreach (var row in shown.Rows)
                {
                    if (issuesOnly.IsChecked == true && !row.Issue) continue;
                    var panel = new StackPanel();
                    panel.Children.Add(new TextBlock { Text = row.Name + " · " + row.Status, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = row.Status == "Verified" ? Frozen("#34D399") : row.Status == "Unknown" ? Frozen("#A7B0BF") : Frozen("#FBBF24") });
                    panel.Children.Add(new TextBlock { Text = row.Category + " · " + row.Detail, TextWrapping = TextWrapping.Wrap, Foreground = Frozen("#A7B0BF"), Margin = new Thickness(0, 6, 0, 0) });
                    rows.Children.Add(new Border { Tag = row, Child = panel, Background = Frozen("#1B2029"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) });
                }
                if (rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = shown.Rows.Count == 0 ? "This selection has no comparable app, Windows or backup settings." : "No items need attention. Clear the filter to see all verified checks.", TextWrapping = TextWrapping.Wrap, Foreground = Frozen("#A7B0BF") });
            };
            Action scan = delegate
            {
                if (closed || busy || running) return;
                int request = ++generation, epoch = tweakStateEpoch;
                busy = true; shown = null; render();
                ScanAudit(target, report =>
                {
                    if (closed || request != generation) return;
                    busy = false;
                    if (epoch != tweakStateEpoch || running) { shown = null; render(); status.Text = "This PC changed during the scan. Recheck after setup finishes."; return; }
                    shown = report;
                    if (postSetup && postAuditTarget == target && !postAuditBusy) { postAuditReport = report; RenderPostAudit(); }
                    render();
                });
            };
            issuesOnly.Checked += delegate { render(); }; issuesOnly.Unchecked += delegate { render(); };
            recheck.Click += delegate { scan(); };
            window.Loaded += delegate { if (initial == null) scan(); };
            window.Closed += delegate { closed = true; generation++; };
            render(); return window;
        }
    }
}
