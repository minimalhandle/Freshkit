// Freshkit - Tweaks, Drivers & offline kit, Progress, Updates and Settings backup pages.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Freshkit
{
    internal sealed partial class MainUi
    {
        Debloat debloat;
        readonly ObservableCollection<RemovalApp> removal = new ObservableCollection<RemovalApp>();
        readonly ObservableCollection<RunRow> runItems = new ObservableCollection<RunRow>();
        readonly ObservableCollection<SetupStage> setupItems = new ObservableCollection<SetupStage>();
        SetupRecord sessionSetup;
        readonly ObservableCollection<UpdateItem> updates = new ObservableCollection<UpdateItem>();
        readonly ObservableCollection<ProfileItem> settingsProfiles = new ObservableCollection<ProfileItem>();
        RunSnapshot lastSnapshot, sessionSummary;
        string runSignature = "";
        bool restoreRunning, updatesChecked;
        int settingsScanVersion;

        void RefreshSettingsAvailability()
        {
            int version = ++settingsScanVersion;
            bool custom;
            List<BackupProfile> profiles;
            try { profiles = BackupCatalog.Load(Engine.CurrentListText(out custom)); }
            catch (Exception ex) { Log("Couldn't check backup settings: " + ex.Message); return; }
            new Thread(delegate ()
            {
                var roots = BackupCatalog.Roots(); var processes = BackupCatalog.RunningProcesses();
                foreach (BackupProfile p in profiles)
                {
                    BackupAvailability found = BackupCatalog.Discover(p, roots, processes);
                    string id = p.Id;
                    Ui(delegate
                    {
                        if (version != settingsScanVersion) return;
                        foreach (ProfileItem row in settingsProfiles) if (row.Id == id) { row.StatusText = found.Status; row.Detail = found.Detail; break; }
                    });
                }
            }) { IsBackground = true }.Start();
        }

        bool ChooseKitFolder(string description)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = description, SelectedPath = Engine.KitDir, ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(new WindowHandle(new System.Windows.Interop.WindowInteropHelper(Window).Handle)) != System.Windows.Forms.DialogResult.OK) return false;
                Engine.KitDir = dialog.SelectedPath; RefreshKit(); return true;
            }
        }
        sealed class WindowHandle : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public WindowHandle(IntPtr handle) { Handle = handle; }
        }

        // ================================================================ Tweaks
        void InitTweaks()
        {
            try { using (var logo = Engine.Resource("freshkit-logo.png")) { var bitmap = System.Windows.Media.Imaging.BitmapFrame.Create(logo, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad); bitmap.Freeze(); Window.Resources["BrandMark"] = bitmap; } } catch { }
            try { debloat = Debloat.Load(); }
            catch (Exception ex)
            {
                var t = Get<TextBlock>("TweaksUnavailable");
                t.Text = "The built-in Win11Debloat data couldn't be loaded: " + ex.Message;
                t.Visibility = Visibility.Visible;
                Get<StackPanel>("RemovalSection").Visibility = Visibility.Collapsed;
                Get<Button>("BtnApplyTweaks").IsEnabled = false;
                return;
            }
            var cats = new ObservableCollection<TweakCategory>(debloat.Categories);
            foreach (TweakCategory c in debloat.Categories)
            {
                TweakCategory cat = c;
                foreach (TweakToggle t in c.Toggles) t.Changed = delegate { cat.Refresh(); RefreshTweakCounts(); };
                foreach (TweakChoice ch in c.Choices) ch.Changed = delegate { cat.Refresh(); RefreshTweakCounts(); };
            }
            foreach (RemovalApp a in debloat.Apps) { a.Changed = delegate { RefreshTweakCounts(); }; removal.Add(a); }
            Get<ItemsControl>("TweakGroups").ItemsSource = cats;
            Get<ItemsControl>("TweakGroups").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnTweakSectionClick));
            Click("BtnTweakQuickSelect", delegate { OpenTweakQuickSelect(); });
            Click("BtnTweakRefresh", delegate { RefreshTweakState(); });
            Get<CheckBox>("OnlyInstalledRemovals").Click += delegate { FilterTweaks(); RefreshTweakCounts(); };
            Get<ItemsControl>("RemovalList").ItemsSource = removal;
            Get<ItemsControl>("RemovalPresets").ItemsSource = debloat.Presets;
            Get<ItemsControl>("RemovalPresets").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(delegate (object s, RoutedEventArgs e)
            {
                var b = e.OriginalSource as Button;
                var p = b == null ? null : b.DataContext as RemovalPreset;
                if (p == null) return;
                var set = new HashSet<string>(p.AppIds, StringComparer.OrdinalIgnoreCase);
                foreach (RemovalApp a in debloat.Apps) if (set.Contains(a.AppId) || a.AppIds.Exists(set.Contains)) a.IsSelected = true;
            }));
            Get<TextBox>("TweakSearch").TextChanged += delegate { FilterTweaks(); };
            Click("BtnTweakRecommended", delegate
            {
                debloat.ApplyFeatures(debloat.RecommendedFeatures);
                foreach (RemovalApp a in debloat.Apps) a.IsSelected = a.DefaultOn;
                Get<CheckBox>("RestorePointSwitch").IsChecked = debloat.RecommendRestorePoint;
                RefreshTweakCounts();
            });
            Click("BtnTweakClear", delegate
            {
                debloat.ApplyFeatures(new string[0]); debloat.ApplyApps(new string[0]);
                Get<CheckBox>("ForceEdgeSwitch").IsChecked = false;
                RefreshTweakCounts();
            });
            Click("BtnRemovalRecommended", delegate { foreach (RemovalApp a in debloat.Apps) a.IsSelected = a.DefaultOn; });
            Click("BtnRemovalNone", delegate { foreach (RemovalApp a in debloat.Apps) a.IsSelected = false; });
            Get<CheckBox>("ForceEdgeSwitch").Click += delegate { RefreshTweakCounts(); };
            Act("BtnApplyTweaks", delegate
            {
                List<string> args = debloat.BuildArguments(Get<CheckBox>("RestorePointSwitch").IsChecked == true, Get<CheckBox>("ForceEdgeSwitch").IsChecked == true);
                if (args == null) { Info("No changes to apply", debloat.SelectedFeatures().Count > 0 ? "The selected settings are already applied on this PC. Select a different setting or an app removal to make a change." : "Choose changes for the next setup or pick apps to remove first."); return; }
                int f = debloat.PendingFeatures().Count, a = debloat.SelectedApps().Count;
                Confirm("Apply Windows tweaks now?", f + " tweak" + (f == 1 ? "" : "s") + " and " + a + " app removal" + (a == 1 ? "" : "s") + " via Win11Debloat. Windows asks for administrator permission; File Explorer restarts at the end. Some changes need a sign-out or restart to show." +
                    (Get<CheckBox>("RestorePointSwitch").IsChecked == true ? " A restore point is created first." : ""), "Apply", delegate
                    {
                        RunJob(new JobSpec { Title = "Applying Windows tweaks", DebloatArgs = args }, null);
                    });
            });
            Act("BtnDebloatGui", delegate { OpenDebloatGui(); });
            RefreshTweakCounts();
        }

        void RefreshTweakCounts()
        {
            if (debloat == null) return;
            int selected = debloat.SelectedFeatures().Count, f = debloat.PendingFeatures().Count, a = debloat.SelectedApps().Count;
            bool edge = Get<CheckBox>("ForceEdgeSwitch").IsChecked == true;
            foreach (TweakCategory c in debloat.Categories) c.Refresh();
            int hidden = 0; foreach (var item in debloat.Apps) if (item.IsSelected && item.Visibility != Visibility.Visible) hidden++;
            Get<TextBlock>("RemovalCount").Text = a + " of " + debloat.Apps.Count + " selected" + (hidden > 0 ? " (" + hidden + " hidden)" : "");
            Get<TextBlock>("TweakCount").Text = f + " tweak" + (f == 1 ? "" : "s") + " to apply · " + (selected - f) + " already current · " + (a + (edge ? 1 : 0)) + " app" + (a + (edge ? 1 : 0) == 1 ? "" : "s") + " to remove";
            Get<TextBlock>("TweakCount").ToolTip = selected + " settings selected for this session. Only pending changes run. Win11Debloat " + debloat.Version;
            Get<Button>("BtnApplyTweaks").IsEnabled = !running && f + a + (edge ? 1 : 0) > 0;
            UpdateCount();
        }

        void FilterTweaks()
        {
            string q = Get<TextBox>("TweakSearch").Text.Trim();
            Func<string, bool> m = s => q.Length == 0 || (s ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (TweakCategory c in debloat.Categories)
            {
                bool any = false, catMatch = m(c.Name);
                foreach (TweakToggle t in c.Toggles) { bool v = catMatch || m(t.Label) || m(t.Tooltip); t.Visibility = v ? Visibility.Visible : Visibility.Collapsed; any |= v; }
                foreach (TweakChoice t in c.Choices) { bool v = catMatch || m(t.Label) || m(t.Tooltip); t.Visibility = v ? Visibility.Visible : Visibility.Collapsed; any |= v; }
                c.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            }
            debloat.FilterRemoval(q, Get<CheckBox>("OnlyInstalledRemovals").IsChecked == true);
        }

        void OpenDebloatGui()
        {
            if (running) return;
            SetRunning(true, "Win11Debloat is open");
            Log("Opening Win11Debloat's own window (approve the administrator prompt). Freshkit waits until you close it.");
            string w11dData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win11Debloat");
            bool existed = Directory.Exists(w11dData);
            new Thread(delegate ()
            {
                string work = null; int rc = 1;
                try
                {
                    work = Engine.ExtractWork();
                    string root = Engine.ExtractDebloat(work);
                    rc = Proc.Elevated(Path.Combine(root, "Win11Debloat.ps1"), "", null, Log, null);
                }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
                finally { Engine.Cleanup(work); }
                // Leave no Win11Debloat data behind unless the user already had some.
                if (!existed) try { if (Directory.Exists(w11dData)) Directory.Delete(w11dData, true); } catch (Exception ex) { Log("Couldn't remove " + w11dData + ": " + ex.Message); }
                Ui(delegate { Finished("Win11Debloat", rc); });
            }) { IsBackground = true }.Start();
        }

        // ================================================================ Drivers & offline kit
        void InitDrivers()
        {
            InitDriverPreview();
            Act("BtnDriverPlan", delegate { OpenDriverPreview(); });
            Act("BtnDrivers", delegate
            {
                OpenDriverPreview(delegate
                {
                    Confirm("Install the previewed driver packages?", "Freshkit asks for administrator permission and rechecks hardware before installing matching chipset and display packages. Your PC may restart on its own. Sign back in to continue.", "Install drivers",
                        delegate { Navigate("NavProgress"); RunJob(new JobSpec { Title = "Installing drivers", Action = "Drivers", TrackAsInstall = true }, null); });
                });
            });
            Act("BtnKitCreate", delegate
            {
                if (!ChooseKitFolder("Choose a folder for this optional driver export. Freshkit itself needs only its EXE.")) return;
                bool all = Get<ComboBox>("KitModeBox").SelectedIndex == 1, wifi = Get<CheckBox>("KitWifiSwitch").IsChecked == true;
                Confirm("Create the offline kit?", "Saves " + (all ? "all third-party drivers" : "network drivers") + (wifi ? " and saved Wi-Fi networks (with their passwords, in plain text)" : "") +
                    " to " + Engine.KitDir + ". Windows asks for administrator permission.", "Create kit", delegate
                    {
                        RunScript("Creating offline kit", "KitExport.ps1", "-KitDir " + Engine.Q(Engine.KitDir) + " -Mode " + (all ? "All" : "Network") + (wifi ? " -Wifi" : ""), delegate { RefreshKit(); });
                    });
            });
            Act("BtnKitInstall", delegate
            {
                int d = Engine.KitDriverCount(), w = Engine.KitWifiCount();
                if (d + w == 0) { Info("No offline kit", "Create one on a working PC first. It's saved next to Freshkit.exe."); return; }
                Confirm("Install from the offline kit?", "Installs " + d + " driver file" + (d == 1 ? "" : "s") + (w > 0 ? " and adds " + w + " Wi-Fi network" + (w == 1 ? "" : "s") : "") + ". Windows asks for administrator permission.", "Install", delegate
                {
                    RunJob(new JobSpec { Title = "Installing from offline kit", DriverKit = d > 0 ? Path.Combine(Engine.KitDir, "Drivers") : null, WifiKit = w > 0 ? Path.Combine(Engine.KitDir, "WiFi") : null },
                        delegate { new Thread(delegate () { bool on = Engine.HasInternet(); Ui(delegate { online = on; Get<TextBlock>("SysNet").Text = on ? "Online" : "No internet connection"; }); }) { IsBackground = true }.Start(); });
                });
            });
            Click("BtnKitOpen", delegate { ChooseKitFolder("Choose an existing offline kit folder to use in this session."); });
        }

        void RefreshKit()
        {
            int d = Engine.KitDriverCount(), w = Engine.KitWifiCount();
            Get<TextBlock>("KitStatus").Text = d + w > 0 ? "Kit found" : "No kit found";
            Get<TextBlock>("KitGlyph").Text = d + w > 0 ? "" : "";
            Get<TextBlock>("KitDetail").Text = d + w > 0
                ? d + " driver file" + (d == 1 ? "" : "s") + (w > 0 ? " and " + w + " Wi-Fi network" + (w == 1 ? "" : "s") : "") + " in " + Engine.KitDir + "." + (online ? "" : " This PC is offline: install from the kit to get connected.")
                : "No kit selected. Choose an existing export, or create one in a folder you choose. The EXE works without a kit.";
            Get<Button>("BtnKitInstall").IsEnabled = d + w > 0 && !running;
        }

        /// Runs one of the elevated helper scripts with its transcript streamed into the activity log.
        void RunScript(string label, string script, string args, Action<int> done)
        {
            if (running) return;
            SetRunning(true, label);
            ShowLog(true);
            Log("");
            Log("━━ " + label + "  ·  " + DateTime.Now.ToString("HH:mm:ss"));
            Log("Waiting for administrator permission...");
            string logFile = Engine.NewLogFile(Path.GetFileNameWithoutExtension(script).ToLowerInvariant());
            new Thread(delegate ()
            {
                string work = null; int rc = 1;
                try
                {
                    work = Engine.ExtractWork();
                    rc = Proc.Elevated(Path.Combine(work, script), args + " -LogFile " + Engine.Q(logFile), logFile, Log, delegate (Process p) { current = p; });
                }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
                finally { current = null; Engine.Cleanup(work); }
                Ui(delegate
                {
                    if (rc == 0 || rc == Proc.UacDeclined) { Engine.DeleteFile(logFile); Engine.TidyDataDir(); }
                    else Log("Log kept for troubleshooting: " + logFile + " (delete it on the Progress page).");
                    Finished(label, rc);
                    if (done != null) done(rc);
                });
            }) { IsBackground = true }.Start();
        }

        // ================================================================ Progress
        void InitProgress()
        {
            Get<ItemsControl>("RunItems").ItemsSource = runItems;
            Get<ItemsControl>("SetupSteps").ItemsSource = setupItems;
            Act("BtnRetryFailed", delegate { RetryFailed(); });
            Act("BtnResume", delegate
            {
                List<string> ids = Engine.PendingRuns();
                RunJob(new JobSpec { Title = "Continuing saved install", Action = "Resume", RunId = ids != null && ids.Count > 0 ? ids[0] : null }, null);
            });
            Act("BtnCancelResume", delegate
            {
                Confirm("Cancel the saved install?", "The remaining steps and the sign-in continuation task are removed. Apps already installed are kept.", "Cancel install", delegate
                {
                    List<string> ids = Engine.PendingRuns();
                    RunJob(new JobSpec { Title = "Cancelling saved install", Action = "CancelResume", RunId = ids != null && ids.Count > 0 ? ids[0] : null }, delegate (int rc)
                    {
                        if (rc == 0) { JobState.Clear(); Engine.ClearReopenAfterRestart(); }
                    });
                });
            });
            Click("BtnDeleteLogs", delegate
            {
                JobState job = JobState.Load();
                if (running || (job != null && job.Stage != "done")) { Info("An install is in progress", "Logs can be deleted once it has finished."); return; }
                Confirm("Delete Freshkit's logs from this PC?", "Removes " + Engine.DataDir + " (logs and install reports).", "Delete", delegate
                {
                    Engine.DeleteAllData();
                    sessionSetup = null; sessionSummary = null;
                    runSignature = "";
                    PollProgress();
                    Log("Deleted Freshkit's logs and reports from this PC.");
                });
            });
            Click("BtnOpenLogs", delegate { try { if (Directory.Exists(Engine.LogsDir)) Process.Start("explorer.exe", "\"" + Engine.LogsDir + "\""); } catch { } });
        }

        void PollProgress()
        {
            sessionSetup = SetupProgress.Load() ?? sessionSetup;
            RunSnapshot active = Runs.ReadActive();
            JobState job = JobState.Load();
            bool tracking = job != null && job.Stage == "running";

            // Finish a tracked install once the engine has written its final report.
            if (active == null && tracking && !jobRunning && !restoreRunning && !running && !job.WorkerAlive)
            {
                RunSnapshot report = sessionSetup != null && !string.IsNullOrEmpty(sessionSetup.EngineRunId) ? Runs.ReadLatestReport(job.Started.AddSeconds(-5), sessionSetup.EngineRunId) : null;
                if (report != null) { CompleteJob(job, report); }
                else if (DateTime.UtcNow - job.Started > TimeSpan.FromMinutes(3))
                {
                    JobState.Clear(); Engine.ClearReopenAfterRestart(); tracking = false;
                    if (sessionSetup != null)
                    {
                        SetupStage stopped = sessionSetup.Steps.Find(s => s.Status == "Running" || s.Status == "Pending");
                        if (stopped != null) { stopped.Status = "Failed"; stopped.Detail = "The setup process stopped before finishing. Review the log, then retry."; }
                        foreach (SetupStage step in sessionSetup.Steps) if (step.Status == "Pending") { step.Status = "Blocked"; step.Detail = "Waiting for retry"; }
                        SetupProgress.Save(sessionSetup);
                    }
                    Log("The install stopped before the engine finished. Open the log folder (Progress page) to see why.");
                }
            }

            bool busy = active != null || jobRunning || tracking || restoreRunning;
            var banner = Get<Border>("ResumeBanner");
            banner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Get<TextBlock>("ResumeBannerTitle").Text = active != null && active.Status == "AwaitingReboot" ? "Waiting for restart" : restoreRunning ? "Restoring settings" : "Install in progress";
            Get<RadioButton>("NavProgress").Content = busy ? "Progress  •" : "Progress";

            if (Get<FrameworkElement>("PageProgress").Visibility != Visibility.Visible && !busy) return;
            RunSnapshot shown = active ?? (sessionSetup == null ? Runs.ReadLatestReport(DateTime.MinValue) : string.IsNullOrEmpty(sessionSetup.EngineRunId) ? null : Runs.ReadLatestReport(DateTime.MinValue, sessionSetup.EngineRunId)) ?? (sessionSetup == null ? sessionSummary : null);
            ShowSnapshot(shown, active != null, busy);
            ShowSetupSteps(busy);
            bool logs = Engine.HasLogs();
            Get<Button>("BtnDeleteLogs").Visibility = !busy && !running && logs ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("BtnOpenLogs").Visibility = logs ? Visibility.Visible : Visibility.Collapsed;
        }

        void RecoverInterruptedRestore()
        {
            sessionSetup = SetupProgress.Load();
            JobState job = JobState.Load();
            bool interrupted = job != null && job.Stage == "restoring";
            if (!interrupted && sessionSetup != null && sessionSetup.Recipe != null && !sessionSetup.Recipe.TrackAsInstall)
                interrupted = sessionSetup.Steps.Exists(s => s.Id == "Settings" && s.Status == "Running");
            if (!interrupted) return;
            if (sessionSetup != null) { SetupProgress.InterruptRestore(sessionSetup); SetupProgress.Save(sessionSetup); }
            JobState.Clear(); Engine.ClearReopenAfterRestart();
        }

        void ShowSetupSteps(bool busy)
        {
            setupItems.Clear();
            if (sessionSetup != null && sessionSetup.Steps != null)
                foreach (SetupStage s in sessionSetup.Steps) setupItems.Add(s);
            bool failed = SetupProgress.HasFailures(sessionSetup);
            if (!busy && failed)
            {
                Get<TextBlock>("RunTitle").Text = "Setup needs attention";
                SetupStage step = sessionSetup.Steps.Find(s => s.Status == "Failed");
                Get<TextBlock>("RunDetail").Text = step.Name + ": " + step.Detail;
                Get<Button>("BtnRetryFailed").Visibility = Visibility.Visible;
            }
            else if (!busy && sessionSetup != null && lastSnapshot == null)
            {
                Get<TextBlock>("RunTitle").Text = "Setup finished";
                Get<TextBlock>("RunDetail").Text = "The selected stages are complete.";
            }
        }

        void ShowSnapshot(RunSnapshot s, bool live, bool busy)
        {
            var title = Get<TextBlock>("RunTitle"); var detail = Get<TextBlock>("RunDetail"); var glyph = Get<TextBlock>("RunGlyph"); var bar = Get<ProgressBar>("RunBar");
            lastSnapshot = s;
            bar.IsIndeterminate = busy && !live;
            if (s == null || (!live && busy))
            {
                if (busy) { title.Text = restoreRunning ? "Restoring app settings" : "Getting ready"; detail.Text = restoreRunning ? "Putting your saved settings back." : "Tweaks, Chocolatey and driver checks run first. Details are in the Activity log below."; glyph.Text = ""; }
                else { title.Text = "Nothing running"; detail.Text = "Start an install from Apps, and its progress shows here."; glyph.Text = ""; bar.Value = 0; }
                if (s == null) { Chips(null); if (runItems.Count > 0) runItems.Clear(); runSignature = ""; SetProgressButtons(false, false); return; }
            }
            if (live)
            {
                switch (s.Status)
                {
                    case "AwaitingReboot": title.Text = "Waiting for a restart"; detail.Text = "Your PC restarts to continue. Sign back in to the same account and it carries on automatically."; glyph.Text = ""; break;
                    case "NeedsReview": title.Text = "Install stopped"; detail.Text = s.Failure; glyph.Text = ""; break;
                    case "Ready": title.Text = "Starting"; detail.Text = s.Done + " of " + s.Total + " done"; glyph.Text = ""; break;
                    default: title.Text = s.Current.Length > 0 ? "Installing " + s.Current : "Installing"; detail.Text = s.Done + " of " + s.Total + " done"; glyph.Text = ""; break;
                }
            }
            else if (!busy)
            {
                if (s.Status == "Completed") { title.Text = s.Bad > 0 ? "Finished, " + s.Bad + " problem" + (s.Bad == 1 ? "" : "s") : "All done"; glyph.Text = s.Bad > 0 ? "" : ""; }
                else if (s.Status == "Cancelled") { title.Text = "Install cancelled"; glyph.Text = ""; }
                else { title.Text = "Install stopped"; glyph.Text = ""; }
                detail.Text = "Last install: " + s.When.ToString("g", CultureInfo.CurrentCulture) + (s.Failure.Length > 0 ? "  ·  " + s.Failure : "") + (s.Bad > 0 ? "  ·  Retry failed tries the failed ones again." : "");
            }
            bar.Value = s.Total > 0 ? (double)s.Done / s.Total : 0;
            Chips(s);
            var sig = new StringBuilder();
            foreach (RunRow r in s.Rows) sig.Append(r.Package).Append(r.Outcome).Append(r.StatusText).Append('|');
            if (sig.ToString() != runSignature)
            {
                runSignature = sig.ToString();
                runItems.Clear();
                foreach (RunRow r in s.Rows) runItems.Add(r);
            }
            SetProgressButtons(!live && !busy && s.Bad > 0, live && !jobRunning);
        }

        void Chips(RunSnapshot s)
        {
            Action<string, string, int> set = delegate (string chip, string text, int n)
            {
                Get<Border>(chip).Visibility = s != null && n > 0 ? Visibility.Visible : Visibility.Collapsed;
                Get<TextBlock>(chip + "Text").Text = text;
            };
            set("ChipOk", (s == null ? 0 : s.Ok) + " installed", s == null ? 0 : s.Ok);
            set("ChipBad", (s == null ? 0 : s.Bad) + " failed", s == null ? 0 : s.Bad);
            set("ChipSkip", (s == null ? 0 : s.Skip) + " skipped / already there", s == null ? 0 : s.Skip);
            set("ChipQueue", (s == null ? 0 : s.Queue) + " to go", s == null ? 0 : s.Queue);
        }

        void SetProgressButtons(bool retry, bool saved)
        {
            Get<Button>("BtnRetryFailed").Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("BtnResume").Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("BtnCancelResume").Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        }

        void CompleteJob(JobState job, RunSnapshot report)
        {
            bool restoreOk = true;
            Action finish = delegate
            {
                JobState.Clear();
                Engine.ClearReopenAfterRestart();
                sessionSummary = report;
                if (report.Status == "Completed" && report.Bad == 0 && restoreOk && !SetupProgress.HasFailures(sessionSetup)) { Engine.DeleteInstallTraces(job.Started, job.LogFile); Engine.DeleteFile(SetupProgress.PathName); Engine.DeleteFile(Path.Combine(Engine.LogsDir, "settings-result.json")); Engine.TidyDataDir(); }
                else Log("Logs kept for troubleshooting in " + Engine.LogsDir + " (Delete logs on the Progress page removes them).");
                SetStatus("Setup finished: " + report.Ok + " installed, " + report.Bad + " failed, " + report.Skip + " skipped.", report.Bad > 0 || !restoreOk || SetupProgress.HasFailures(sessionSetup) ? DotWarn : DotOk);
                Navigate("NavProgress");
                PollProgress();
                StartPostSetupVerification(sessionSetup);
            };
            if (job.RestoreZip.Length > 0 && File.Exists(job.RestoreZip))
            {
                job.Stage = "restoring"; try { job.Save(); } catch { }
                var recipe = sessionSetup == null ? new JobSpec { RestoreZip = job.RestoreZip, AllowMismatch = job.AllowMismatch } : sessionSetup.Recipe;
                RestoreSetupSettings(recipe, delegate (bool ok) { restoreOk = ok; finish(); });
            }
            else
            {
                if (job.RestoreZip.Length > 0) { restoreOk = false; SetupProgress.Set(sessionSetup, "Settings", "Failed", "Backup ZIP was not found: " + job.RestoreZip); if (sessionSetup != null) SetupProgress.Save(sessionSetup); Log("Settings backup not found any more: " + job.RestoreZip); }
                finish();
            }
        }

        void RetryFailed()
        {
            if (SetupProgress.HasFailures(sessionSetup) && sessionSetup.Recipe != null)
            {
                var retry = SetupProgress.Retry(sessionSetup.Recipe, sessionSetup.Steps, lastSnapshot, sessionSetup.EngineRunId);
                Confirm("Retry incomplete setup steps?", "Completed stages are kept. Failed stages and any blocked remaining work will be retried.", "Retry", delegate { RunJob(retry, null); });
                return;
            }
            if (lastSnapshot == null) return;
            var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool driverFailed = false;
            foreach (RunRow r in lastSnapshot.Rows) if (r.Outcome == "Failed") { if (r.Kind == "Driver") driverFailed = true; else failed.Add(r.Package); }
            int n = 0;
            applyingProfile = true;
            foreach (AppItem a in apps) { bool f = failed.Contains(a.EnginePackageId); a.IsSelected = f; if (f) n++; }
            applyingProfile = false;
            UpdateCount();
            if (n == 0 && !driverFailed) { Info("Nothing to retry", "The failed items aren't in the current list."); return; }
            if (n == 0) { Navigate("NavDrivers"); Info("Driver install failed", "Use Install drivers on this page to try the drivers again."); return; }
            Navigate("NavApps");
            Log("Selected " + n + " failed app" + (n == 1 ? "" : "s") + " to retry.");
            OpenInstallPlan();
        }

        void RestoreSetupSettings(JobSpec spec, Action<bool> done)
        {
            restoreRunning = true;
            SetupProgress.Set(sessionSetup, "Settings", "Running", "Restoring saved app settings");
            if (sessionSetup != null) SetupProgress.Save(sessionSetup);
            var sw = new List<string>();
            if (spec.AllowMismatch) sw.Add("AllowVersionMismatch");
            // Retry uses the original session's settings rules, even after a restart.
            string profiles = spec.RestoreProfiles != null && spec.RestoreProfiles.Count > 0 ? string.Join(",", spec.RestoreProfiles.ToArray()) : "ALL";
            Engine.DeleteFile(Path.Combine(Engine.LogsDir, "settings-result.json"));
            RunRunner("Restoring app settings", "Restore", Vals("ArchivePath", spec.RestoreZip, "Profiles", profiles), sw, delegate (int rc)
            {
                restoreRunning = false;
                sessionSetup = SetupProgress.Load() ?? sessionSetup;
                SetupProgress.Set(sessionSetup, "Settings", rc == 0 ? "Done" : "Failed", rc == 0 ? "Saved settings restored" : rc == 2 ? "Some apps were skipped. Close running apps or review version compatibility, then retry." : "Restore failed. Check the backup path and activity log.");
                object result = Engine.ReadJsonFile(Path.Combine(Engine.LogsDir, "settings-result.json"));
                var failedIds = new List<string>();
                foreach (object row in J.A(result, "Results")) if (J.S(row, "Status") != "Restored" && J.S(row, "Status") != "NoSavedSettings") failedIds.Add(J.S(row, "Id"));
                if (sessionSetup != null)
                {
                    if (failedIds.Count > 0) sessionSetup.Recipe.RestoreProfiles = failedIds;
                    SetupProgress.Save(sessionSetup);
                    if (rc == 0 && !spec.TrackAsInstall) { Engine.DeleteFile(SetupProgress.PathName); Engine.DeleteFile(Path.Combine(Engine.LogsDir, "settings-result.json")); Engine.TidyDataDir(); }
                }
                RefreshSettingsAvailability(); done(rc == 0);
            }, spec.ListText);
        }

        // ================================================================ Updates
        void InitUpdates()
        {
            Get<ItemsControl>("UpdateList").ItemsSource = updates;
            Act("BtnCheckUpdates", delegate { CheckUpdates(null); });
            Act("BtnUpdateAll", delegate
            {
                if (!updatesChecked) { CheckUpdates(delegate { if (updates.Count > 0) UpdateItems(new List<UpdateItem>(updates)); }); return; }
                if (updates.Count == 0) { Info(updateCheckComplete ? "Everything is up to date" : "Update check incomplete", Get<TextBlock>("UpdatesStatus").Text); return; }
                UpdateItems(new List<UpdateItem>(updates));
            });
            Act("BtnUpdateSelected", delegate
            {
                var sel = new List<UpdateItem>();
                foreach (UpdateItem u in updates) if (u.IsSelected) sel.Add(u);
                if (sel.Count == 0) { Info("Nothing selected", "Tick the updates you want."); return; }
                UpdateItems(sel);
            });
            var day = Get<ComboBox>("ScheduleDay");
            foreach (string d in new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }) day.Items.Add(d);
            day.SelectedIndex = 6;
            var time = Get<ComboBox>("ScheduleTime");
            foreach (string t in new[] { "00:00", "03:00", "06:00", "09:00", "12:00", "15:00", "18:00", "21:00" }) time.Items.Add(t);
            time.SelectedIndex = 6;
            Act("BtnScheduleOn", delegate
            {
                string d = (string)day.SelectedItem, t = (string)time.SelectedItem;
                RunScript("Turning on weekly updates", "Schedule.ps1", "-Day " + d + " -Time " + t + StoreGitHubArgs(), delegate { RefreshSchedule(); });
            });
            Act("BtnScheduleOff", delegate { RunScript("Turning off weekly updates", "Schedule.ps1", "-Disable", delegate { RefreshSchedule(); }); });
            Act("BtnBootstrap", delegate { RunJob(new JobSpec { Title = "Installing / updating Chocolatey", Action = "Bootstrap" }, null); });
            Act("BtnList", delegate { RunRunner("Install order for all apps", "List", Vals("Apps", AllKeys()), null, null); });
        }

        bool updateCheckComplete;

        void CheckUpdates(Action then)
        {
            Get<Button>("BtnCheckUpdates").IsEnabled = false;
            Get<TextBlock>("UpdatesStatus").Text = "Checking Chocolatey, winget, the Microsoft Store and GitHub for updates (this can take a minute)...";
            // Store and GitHub apps in this session's list (catalog plus additions).
            var storeApps = new List<AppItem>(); var gitHubApps = new List<AppItem>();
            foreach (AppItem a in apps) { if (a.IsStore) storeApps.Add(a); else if (a.IsGitHub) gitHubApps.Add(a); }
            SetStatus("Checking for updates...", DotBusy);
            new Thread(delegate ()
            {
                var check = UpdateCheck.Run(new Dictionary<string, Func<List<UpdateItem>>> {
                    { "Chocolatey", Sources.ChocoUpdates }, { "winget", Sources.WingetUpdates },
                    { "Microsoft Store", delegate { return Sources.StoreUpdates(storeApps); } },
                    { "GitHub", delegate { return Sources.GitHubUpdates(gitHubApps); } } });
                var choco = check.Items.FindAll(u => u.Source == "choco");
                var winget = check.Items.FindAll(u => u.Source == "winget");
                var store = check.Items.FindAll(u => u.Source == "store");
                var github = check.Items.FindAll(u => u.Source == "github");
                // Apps Chocolatey manages also show up in winget; keep the Chocolatey entry only.
                var chocoNames = new List<string>();
                foreach (UpdateItem c in choco) chocoNames.Add(Data.Norm(System.Text.RegularExpressions.Regex.Replace(c.Id, @"\.(install|portable)$", "")));
                var merged = new List<UpdateItem>(choco);
                foreach (UpdateItem w in winget)
                {
                    string n = Data.Norm(w.Name); bool dup = false;
                    foreach (string c in chocoNames) if (c.Length >= 3 && (n == c || (c.Length >= 4 && n.StartsWith(c)))) dup = true;
                    if (!dup) merged.Add(w);
                }
                merged.AddRange(store);
                merged.AddRange(github);
                Ui(delegate
                {
                    Get<Button>("BtnCheckUpdates").IsEnabled = !running;
                    updates.Clear();
                    foreach (UpdateItem u in merged) updates.Add(u);
                    updatesChecked = true;
                    updateCheckComplete = check.Complete;
                    check.Items.Clear(); check.Items.AddRange(merged);
                    Get<TextBlock>("UpdatesStatus").Text = check.Status;
                    Get<Button>("BtnUpdateSelected").Visibility = merged.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    SetStatus(check.Complete ? "Update check: " + merged.Count + " available." : "Update check incomplete.", !check.Complete || merged.Count > 0 ? DotWarn : DotOk);
                    if (then != null) then();
                });
            }) { IsBackground = true }.Start();
        }

        void UpdateItems(List<UpdateItem> items)
        {
            string args = UpdateArguments(items);
            Confirm("Update " + items.Count + " app" + (items.Count == 1 ? "?" : "s?"), "Windows asks for administrator permission. Close the apps being updated first.", "Update", delegate
            {
                RunScript("Updating apps", "Updates.ps1", args, delegate { CheckUpdates(null); });
            });
        }

        /// Updates.ps1 arguments: Chocolatey and winget IDs, Store product IDs and GitHub owner/repo|name pairs.
        static string UpdateArguments(List<UpdateItem> items)
        {
            var choco = new List<string>(); var winget = new List<string>(); var store = new List<string>(); var github = new List<string>();
            foreach (UpdateItem u in items)
            {
                if (u.Source == "github") github.Add(u.Id + "|" + System.Text.RegularExpressions.Regex.Replace(u.Name, "[|;\"']", " "));
                else if (u.Source == "store") store.Add(u.Id);
                else (u.Source == "winget" ? winget : choco).Add(u.Id);
            }
            var args = new StringBuilder();
            if (choco.Count > 0) args.Append("-Choco ").Append(Engine.Q(string.Join(",", choco.ToArray()))).Append(' ');
            if (winget.Count > 0) args.Append("-Winget ").Append(Engine.Q(string.Join(",", winget.ToArray()))).Append(' ');
            if (store.Count > 0) args.Append("-Store ").Append(Engine.Q(string.Join(",", store.ToArray()))).Append(' ');
            if (github.Count > 0) args.Append("-GitHub ").Append(Engine.Q(string.Join(";", github.ToArray())));
            return args.ToString().Trim();
        }

        /// Store and GitHub apps from this session's list, for the weekly task (it updates only those already installed).
        string StoreGitHubArgs()
        {
            var store = new List<string>(); var github = new List<string>();
            foreach (AppItem a in apps)
            {
                if (a.IsStore) store.Add(a.Id);
                else if (a.IsGitHub) github.Add(a.Id + "|" + System.Text.RegularExpressions.Regex.Replace(a.Name, "[|;\"']", " "));
            }
            return (store.Count > 0 ? " -Store " + Engine.Q(string.Join(",", store.ToArray())) : "") + (github.Count > 0 ? " -GitHub " + Engine.Q(string.Join(";", github.ToArray())) : "");
        }

        void RefreshSchedule()
        {
            new Thread(delegate ()
            {
                string output;
                string cmd = "$t=Get-ScheduledTask -TaskPath '\\Freshkit\\' -TaskName 'Weekly updates' -ErrorAction SilentlyContinue; if($t){$i=$t|Get-ScheduledTaskInfo -ErrorAction SilentlyContinue;$tr=@($t.Triggers)[0];'ON|'+$(if($i -and $i.NextRunTime){$i.NextRunTime.ToString('ddd d MMM HH:mm')}else{''})+'|'+$tr.StartBoundary+'|'+$tr.DaysOfWeek}else{'OFF'}";
                try { Proc.Capture(Engine.PowerShellPath, "-NoProfile -NonInteractive -Command \"" + cmd.Replace("\"", "\\\"") + "\"", out output, 30000); }
                catch { output = ""; }
                Ui(delegate
                {
                    string line = "";
                    foreach (string l in output.Split('\n')) if (l.StartsWith("ON|") || l.StartsWith("OFF")) line = l.Trim();
                    var status = Get<TextBlock>("ScheduleStatus");
                    if (line.StartsWith("ON|"))
                    {
                        string[] p = line.Split('|');
                        string days = DaysText(p.Length > 3 ? p[3] : ""), at = p.Length > 2 && p[2].Length >= 16 ? p[2].Substring(11, 5) : "";
                        status.Text = "On: every " + days + (at.Length > 0 ? " at " + at : "") + (p[1].Length > 0 ? ". Next run " + p[1] + "." : ".") + " Runs when you're signed in, and catches up if the PC was off.";
                        int di = Get<ComboBox>("ScheduleDay").Items.IndexOf(days); if (di >= 0) Get<ComboBox>("ScheduleDay").SelectedIndex = di;
                        int ti = Get<ComboBox>("ScheduleTime").Items.IndexOf(at); if (ti >= 0) Get<ComboBox>("ScheduleTime").SelectedIndex = ti;
                    }
                    else status.Text = "Off. Turn it on to update Chocolatey and winget apps every week, even without Freshkit (it doesn't need the USB stick).";
                });
            }) { IsBackground = true }.Start();
        }

        static string DaysText(string mask)
        {
            int m;
            if (!int.TryParse(mask, out m) || m == 0) return mask;
            string[] names = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
            var list = new List<string>();
            for (int i = 0; i < 7; i++) if ((m & (1 << i)) != 0) list.Add(names[i]);
            return string.Join(", ", list.ToArray());
        }

        // ================================================================ Settings backup
        void InitSettings()
        {
            Get<ItemsControl>("ProfileList").ItemsSource = settingsProfiles;
            Click("BtnProfAll", delegate { foreach (ProfileItem p in settingsProfiles) p.IsSelected = true; });
            Click("BtnProfNone", delegate { foreach (ProfileItem p in settingsProfiles) p.IsSelected = false; });
            Act("BtnSettingsList", delegate { RunRunner("Settings locations", "SettingsList", null, null, null); });
            Click("BtnRefreshSettings", delegate { RefreshSettingsAvailability(); });
            Act("BtnBackup", delegate { Backup(false); });
            Act("BtnBackupPlan", delegate { Backup(true); });
            Click("BtnBrowse", delegate { BrowseArchive(); });
            Act("BtnRestore", delegate { Restore("Restore"); });
            Act("BtnRestorePlan", delegate { Restore("RestorePlan"); });
            Act("BtnInspect", delegate { Restore("Inspect"); });
        }

        string SelectedSettingsProfiles()
        {
            var ids = new List<string>();
            foreach (ProfileItem p in settingsProfiles) if (p.IsSelected) ids.Add(p.Id);
            if (ids.Count == 0) { Info("Nothing selected", "Pick at least one app to include."); return null; }
            return ids.Count == settingsProfiles.Count ? "ALL" : string.Join(",", ids.ToArray());
        }

        void Backup(bool preview)
        {
            string prof = SelectedSettingsProfiles();
            if (prof == null) return;
            string path;
            if (preview) path = Path.Combine(Engine.ExeDir, "settings-backup-preview.zip");
            else
            {
                var d = new Microsoft.Win32.SaveFileDialog { Filter = "ZIP archive (*.zip)|*.zip", FileName = "settings-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", InitialDirectory = Engine.ExeDir, OverwritePrompt = true };
                if (d.ShowDialog(Window) != true) return;
                path = d.FileName;
                Get<TextBox>("ArchiveBox").Text = path;
            }
            RunRunner(preview ? "Previewing settings backup" : "Backing up settings", preview ? "BackupPlan" : "Backup", Vals("ArchivePath", path, "Profiles", prof), null, null);
        }

        bool BrowseArchive()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "ZIP archive (*.zip)|*.zip", InitialDirectory = Engine.ExeDir };
            if (d.ShowDialog(Window) != true) return false;
            Get<TextBox>("ArchiveBox").Text = d.FileName;
            return true;
        }

        void Restore(string action)
        {
            var box = Get<TextBox>("ArchiveBox");
            string path = box.Text.Trim().Trim('"');
            if (path.Length == 0 || !File.Exists(path)) { if (!BrowseArchive()) return; path = box.Text.Trim(); }
            var v = Vals("ArchivePath", path);
            var sw = new List<string>();
            if (Get<CheckBox>("MismatchSwitch").IsChecked == true) sw.Add("AllowVersionMismatch");
            if (action == "Inspect") { RunRunner("Inspecting backup ZIP", action, v, sw, null); return; }
            string prof = SelectedSettingsProfiles();
            if (prof == null) return;
            v["Profiles"] = prof;
            if (action == "RestorePlan") { RunRunner("Previewing settings restore", action, v, sw, null); return; }
            Confirm("Restore settings?", "Close the selected apps first. Your current settings are saved to a safety ZIP before anything is overwritten.", "Restore",
                delegate { RunRunner("Restoring settings", action, v, sw, null); });
        }
    }
}
