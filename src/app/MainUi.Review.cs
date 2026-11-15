// Freshkit - readable setup review. Preparing a review never starts a job.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Freshkit
{
    internal sealed partial class MainUi
    {
        void InitReview()
        {
            Click("BtnReviewClose", delegate { CloseReview(); });
            Click("BtnReviewCancel", delegate { CloseReview(); });
            Click("BtnReviewApps", delegate { CloseReview(); Navigate("NavApps"); Get<TextBox>("SearchBox").Focus(); });
            Click("BtnReviewTweaks", delegate { CloseReview(); Navigate("NavTweaks"); });
            Click("BtnReviewKit", delegate { CloseReview(); Navigate("NavDrivers"); });
            Click("BtnReviewBackup", delegate { ChooseReviewBackup(); });
            Get<CheckBox>("ReviewRestore").Checked += delegate
            {
                if (Get<CheckBox>("ReviewRestore").Tag == null) ChooseReviewBackup();
            };
            foreach (string name in new[] { "ReviewTweaks", "ReviewKit" })
            {
                Get<CheckBox>(name).Checked += delegate { UpdateReviewStart(); };
                Get<CheckBox>(name).Unchecked += delegate { UpdateReviewStart(); };
            }
            Click("BtnReviewStart", delegate
            {
                if (running) return;
                JobSpec spec = BuildReviewedJob();
                if (spec == null) return;
                CloseReview();
                Navigate("NavProgress");
                RunJob(spec, null);
            });
        }

        void CloseReview()
        {
            Get<Grid>("ReviewSheet").Visibility = Visibility.Collapsed;
            UpdateModalSurfaces();
            Get<Button>("BtnInstall").Focus();
        }

        void UpdateModalSurfaces()
        {
            bool enabled = Get<Grid>("ReviewSheet").Visibility != Visibility.Visible && Get<Grid>("SearchSheet").Visibility != Visibility.Visible;
            Get<Border>("SidebarSurface").IsEnabled = enabled;
            Get<Grid>("ContentSurface").IsEnabled = enabled;
        }

        void ChooseReviewBackup()
        {
            var restore = Get<CheckBox>("ReviewRestore");
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Settings backup (*.zip)|*.zip", InitialDirectory = Engine.ExeDir };
            if (picker.ShowDialog(Window) == true)
            {
                restore.Tag = picker.FileName;
                Get<TextBlock>("ReviewBackupPath").Text = picker.FileName;
                restore.IsChecked = true;
            }
            else if (restore.Tag == null) restore.IsChecked = false;
        }

        void OpenInstallPlan()
        {
            if (running) return;
            int n = 0, installed = 0;
            var appText = new StringBuilder();
            foreach (AppItem app in apps) if (app.IsSelected)
            {
                n++; if (app.Installed) installed++;
                if (appText.Length > 0) appText.AppendLine().AppendLine();
                appText.Append(app.Name).Append(" · ").Append(app.SourceLabel);
                if (app.Installed) appText.Append(" · Installed");
                appText.AppendLine().Append(app.Subtitle);
            }
            int tweaks = debloat == null ? 0 : debloat.SelectedFeatures().Count;
            int removals = debloat == null ? 0 : debloat.SelectedApps().Count;
            bool edge = Get<CheckBox>("ForceEdgeSwitch").IsChecked == true;
            if (n == 0 && tweaks + removals == 0 && !edge)
            {
                Info("Nothing selected", "Pick apps or Windows tweaks before reviewing setup."); return;
            }

            Get<TextBlock>("ReviewSummary").Text = n + " apps · " + tweaks + " tweaks · " + (removals + (edge ? 1 : 0)) + " removals";
            Get<TextBlock>("ReviewAppsTitle").Text = "Apps · " + n + " selected";
            Get<TextBlock>("ReviewAppsDetail").Text = n > 0 ? appText.ToString() : "No apps selected. Only the enabled sections below will run.";
            Get<TextBlock>("ReviewInstalled").Text = installed > 0 ? installed + (installed == 1 ? " selected app was" : " selected apps were") + " detected as installed. Choose whether to update below." : "Installed status is based on Scan this PC; unmarked apps may already be installed.";
            Get<CheckBox>("ReviewUpgrade").IsChecked = true;
            Get<CheckBox>("ReviewDrivers").IsChecked = true;
            Get<Border>("ReviewDriverCard").Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            Get<CheckBox>("ReviewUpgrade").Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            Get<TextBlock>("ReviewInstalled").Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            Get<Expander>("ReviewAppList").IsExpanded = n <= 5;

            var details = new StringBuilder();
            if (debloat != null)
            {
                foreach (TweakToggle toggle in debloat.AllToggles) if (toggle.IsSelected) details.AppendLine("• " + toggle.Label);
                foreach (TweakChoice choice in debloat.AllChoices) if (choice.SelectedIndex > 0) details.AppendLine("• " + choice.Label + ": " + choice.Options[choice.SelectedIndex]);
                foreach (RemovalApp app in debloat.Apps) if (app.IsSelected) details.AppendLine("Remove: " + app.Name);
            }
            if (edge) details.AppendLine("Remove: Microsoft Edge (force removal; some Windows features rely on it)");
            bool hasTweaks = tweaks + removals > 0 || edge;
            Get<TextBlock>("ReviewTweaksDetail").Text = hasTweaks ? details.ToString().TrimEnd() : "No Windows tweaks or removals selected.";
            Get<CheckBox>("ReviewTweaks").IsEnabled = hasTweaks;
            Get<CheckBox>("ReviewTweaks").IsChecked = hasTweaks;
            Get<CheckBox>("ReviewRestorePoint").IsChecked = Get<CheckBox>("RestorePointSwitch").IsChecked;
            Get<CheckBox>("ReviewRestorePoint").Visibility = hasTweaks ? Visibility.Visible : Visibility.Collapsed;

            int kitDrivers = Engine.KitDriverCount(), kitWifi = Engine.KitWifiCount();
            Get<TextBlock>("ReviewKitDetail").Text = kitDrivers + " driver files · " + kitWifi + " saved Wi-Fi networks";
            Get<Border>("ReviewKitCard").Visibility = kitDrivers + kitWifi > 0 ? Visibility.Visible : Visibility.Collapsed;
            Get<CheckBox>("ReviewKit").IsChecked = kitDrivers + kitWifi > 0 && !online;

            string zip = n > 0 ? RestoreCandidate() : null;
            Get<Border>("ReviewRestoreCard").Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            Get<CheckBox>("ReviewRestore").Tag = zip;
            Get<CheckBox>("ReviewRestore").IsChecked = zip != null;
            Get<TextBlock>("ReviewBackupPath").Text = zip ?? "No backup selected. Choose a settings backup ZIP to restore after setup.";
            Get<CheckBox>("ReviewAllowMismatch").IsChecked = Get<CheckBox>("MismatchSwitch").IsChecked;
            Get<TextBlock>("ReviewValidation").Text = "";
            Get<ScrollViewer>("ReviewScroll").ScrollToTop();
            Get<Grid>("ReviewSheet").Visibility = Visibility.Visible;
            UpdateModalSurfaces();
            UpdateReviewStart();
            Get<Button>("BtnReviewCancel").Focus();
        }

        void UpdateReviewStart()
        {
            bool hasApps = Selection(false) != null;
            bool hasKit = Get<CheckBox>("ReviewKit").IsChecked == true && Engine.KitDriverCount() + Engine.KitWifiCount() > 0;
            bool hasTweaks = Get<CheckBox>("ReviewTweaks").IsEnabled && Get<CheckBox>("ReviewTweaks").IsChecked == true;
            int count = 0; foreach (AppItem app in apps) if (app.IsSelected) count++;
            int tweaks = hasTweaks && debloat != null ? debloat.SelectedFeatures().Count : 0;
            int removals = hasTweaks && debloat != null ? debloat.SelectedApps().Count + (Get<CheckBox>("ForceEdgeSwitch").IsChecked == true ? 1 : 0) : 0;
            Get<TextBlock>("ReviewSummary").Text = count + (count == 1 ? " app" : " apps") + " · " + tweaks + (tweaks == 1 ? " tweak" : " tweaks") + " · " + removals + (removals == 1 ? " removal" : " removals");
            Get<Button>("BtnReviewStart").IsEnabled = !running && (hasApps || hasKit || hasTweaks);
        }

        JobSpec BuildReviewedJob()
        {
            string selection = Selection(false);
            var spec = new JobSpec { Title = selection != null ? "Setting up this PC" : "Applying Windows tweaks" };
            if (selection != null)
            {
                spec.Action = "Install"; spec.Apps = selection; spec.TrackAsInstall = true;
                spec.SkipDrivers = Get<CheckBox>("ReviewDrivers").IsChecked != true;
                spec.Upgrade = Get<CheckBox>("ReviewUpgrade").IsChecked == true;
                if (Get<CheckBox>("ReviewRestore").IsChecked == true)
                {
                    string zip = Get<CheckBox>("ReviewRestore").Tag as string;
                    if (string.IsNullOrEmpty(zip) || !File.Exists(zip))
                    {
                        Get<TextBlock>("ReviewValidation").Text = "Choose an existing backup ZIP, or turn off settings restore."; return null;
                    }
                    spec.RestoreZip = zip;
                    spec.AllowMismatch = Get<CheckBox>("ReviewAllowMismatch").IsChecked == true;
                }
            }
            if (Get<CheckBox>("ReviewKit").IsChecked == true)
            {
                if (Engine.KitDriverCount() > 0) spec.DriverKit = Path.Combine(Engine.KitDir, "Drivers");
                if (Engine.KitWifiCount() > 0) spec.WifiKit = Path.Combine(Engine.KitDir, "WiFi");
            }
            if (Get<CheckBox>("ReviewTweaks").IsChecked == true && debloat != null)
                spec.DebloatArgs = debloat.BuildArguments(Get<CheckBox>("ReviewRestorePoint").IsChecked == true, Get<CheckBox>("ForceEdgeSwitch").IsChecked == true);
            if (spec.Action == null && spec.DebloatArgs == null && spec.DriverKit == null && spec.WifiKit == null)
            {
                Get<TextBlock>("ReviewValidation").Text = "Enable a section or go back and select apps to set up."; return null;
            }
            return spec;
        }
    }
}
