using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Freshkit
{
    internal sealed partial class MainUi
    {
        PreflightSnapshot reviewPreflight;
        bool reviewPreflightBusy, reviewInitializing;
        int reviewPreflightGeneration;
        bool ReviewPreflightReady
        {
            get { return !reviewPreflightBusy && reviewPreflight != null && DateTime.UtcNow - reviewPreflight.CheckedUtc < TimeSpan.FromMinutes(2); }
        }

        void InitPreflight()
        {
            Click("BtnReviewRecheck", delegate { RefreshReviewPreflight(); });
            Click("BtnReviewStorage", delegate
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true }); }
                catch (Exception ex) { Info("Storage settings unavailable", ex.Message); }
            });
            Get<CheckBox>("ReviewRestore").Checked += delegate { ReviewRestoreChanged(); };
            Get<CheckBox>("ReviewRestore").Unchecked += delegate { UpdateReviewStart(); };
        }

        void ReviewRestoreChanged()
        {
            if (!reviewInitializing && Get<CheckBox>("ReviewRestore").Tag != null && Get<Grid>("ReviewSheet").Visibility == Visibility.Visible && Window.IsVisible) RefreshReviewPreflight();
            else UpdateReviewStart();
        }

        PreflightRequest ReviewPreflightRequest()
        {
            var request = new PreflightRequest();
            bool hasApps = false;
            foreach (AppItem app in apps) if (app.IsSelected)
            {
                hasApps = true;
                request.Sources.Add(app.SourceLabel);
                if (app.IsWinget) request.NeedsWinget = true;
            }
            // All app jobs verify Chocolatey during bootstrap, even winget-only jobs.
            if (hasApps) request.Sources.Add("Chocolatey");
            if (hasApps && Get<CheckBox>("ReviewRestore").IsChecked == true)
            {
                request.RestoreZip = Get<CheckBox>("ReviewRestore").Tag as string;
                if (string.IsNullOrEmpty(request.RestoreZip)) request.RestoreError = "Choose a settings backup ZIP.";
                try { bool custom; request.RestoreProfiles.AddRange(BackupCatalog.Load(Engine.CurrentListText(out custom))); }
                catch (Exception ex) { request.RestoreError = ex.Message; }
            }
            return request;
        }

        int BeginReviewPreflight()
        {
            reviewPreflightGeneration++;
            reviewPreflight = null; reviewPreflightBusy = true;
            UpdateReviewStart();
            return reviewPreflightGeneration;
        }

        void CompleteReviewPreflight(int generation, PreflightSnapshot report)
        {
            if (generation != reviewPreflightGeneration || Get<Grid>("ReviewSheet").Visibility != Visibility.Visible) return;
            reviewPreflight = report; reviewPreflightBusy = false;
            UpdateReviewStart();
        }

        void RefreshReviewPreflight()
        {
            if (reviewInitializing || Get<Grid>("ReviewSheet").Visibility != Visibility.Visible) return;
            var request = ReviewPreflightRequest();
            int generation = BeginReviewPreflight();
            new Thread(delegate()
            {
                PreflightSnapshot report;
                try { report = Preflight.Scan(request); }
                catch { report = new PreflightSnapshot(); }
                if (Window.Dispatcher.HasShutdownStarted) return;
                Ui(delegate { CompleteReviewPreflight(generation, report); });
            }) { IsBackground = true }.Start();
        }

        void RenderReviewPreflight()
        {
            var issues = Get<StackPanel>("ReviewPreflightIssues"); issues.Children.Clear();
            var passed = Get<TextBlock>("ReviewPreflightPassed"); passed.Text = "";
            var summary = Get<TextBlock>("ReviewPreflightSummary");
            Get<Button>("BtnReviewRecheck").IsEnabled = !reviewPreflightBusy;
            Get<Expander>("ReviewPreflightReady").Visibility = Visibility.Collapsed;
            Get<Button>("BtnReviewStorage").Visibility = Visibility.Collapsed;
            if (reviewPreflightBusy) { summary.Text = "Checking free space, restart signals, required sources and settings restore…"; return; }
            if (!ReviewPreflightReady) { summary.Text = "Readiness checks need refreshing. Select Recheck before starting."; return; }
            var results = Preflight.Evaluate(reviewPreflight, ReviewPreflightRequest());
            int warnings = 0, ready = 0;
            foreach (var check in results)
            {
                if (check.Status == "Ready")
                {
                    ready++; passed.Text += (passed.Text.Length > 0 ? "\n\n" : "") + check.Title + " · " + check.Detail;
                    continue;
                }
                warnings++;
                issues.Children.Add(new TextBlock { Text = check.Title + " · " + check.Status, FontWeight = FontWeights.SemiBold, Foreground = Frozen("#FBBF24"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 4) });
                issues.Children.Add(new TextBlock { Text = check.Detail, Foreground = Frozen("#A7B0BF"), TextWrapping = TextWrapping.Wrap });
                if (check.Title == "Free space") Get<Button>("BtnReviewStorage").Visibility = Visibility.Visible;
            }
            summary.Text = warnings == 0 ? "Checks complete · no issues detected" : warnings + (warnings == 1 ? " check needs" : " checks need") + " attention. You can continue with these warnings.";
            summary.Text += " · " + reviewPreflight.CheckedUtc.ToLocalTime().ToString("HH:mm:ss");
            var readyExpander = Get<Expander>("ReviewPreflightReady");
            readyExpander.Header = ready + " passed checks";
            readyExpander.Visibility = ready > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
