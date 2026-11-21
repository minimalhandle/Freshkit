// Freshkit - main window: shared plumbing (navigation, dialogs, activity log, running jobs).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Freshkit
{
    internal sealed class JobSpec
    {
        public string Title = "Working";
        public string Action;              // runner action (Install, Drivers, Bootstrap, Resume, CancelResume) or null
        public string Apps;                // selection keys
        public bool Upgrade, SkipDrivers, TrackAsInstall;
        public string DriverKit, WifiKit, RunId;
        public string DriverIds;
        public List<string> DebloatArgs;   // Win11Debloat switches, or null
        public string RestoreZip;
        public bool AllowMismatch;
        public string ListText;
        public List<string> RestoreProfiles;
    }

    internal sealed partial class MainUi
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public readonly Window Window;
        readonly List<UIElement> actionControls = new List<UIElement>();
        readonly string[] pages = { "PageApps", "PageTweaks", "PageDrivers", "PageProgress", "PageUpdates", "PageSettings" };
        readonly string[] navs = { "NavApps", "NavTweaks", "NavDrivers", "NavProgress", "NavUpdates", "NavSettings" };
        bool running, allowClose, logOpen, jobRunning;
        Process current;
        Action dialogOk;
        DispatcherTimer timer;

        static readonly Brush DotIdle = Frozen("#4B5563"), DotBusy = Frozen("#2DD4BF"), DotOk = Frozen("#34D399"), DotWarn = Frozen("#FBBF24"), DotBad = Frozen("#F87171");
        static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

        public MainUi(bool openProgress)
        {
            Window = (Window)XamlReader.Parse(Engine.ReadResourceText("MainWindow.xaml"));
            try { using (Stream s = Engine.Resource("app.ico")) Window.Icon = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); } catch { }
            Window.SourceInitialized += delegate { DarkTitleBar(); };
            Window.Closing += OnClosing;
            Window.Closed += delegate { if (searchCancellation != null) searchCancellation.Cancel(); HideFinishNotice(); };

            for (int i = 0; i < navs.Length; i++)
            {
                string page = pages[i];
                Get<RadioButton>(navs[i]).Checked += delegate { ShowPage(page); };
            }
            Get<Border>("ResumeBanner").MouseLeftButtonUp += delegate { Get<RadioButton>("NavProgress").IsChecked = true; };

            // Activity drawer + dialogs
            Click("BtnCopyLog", delegate { var t = Get<TextBox>("LogBox").Text; if (t.Length > 0) Clipboard.SetText(t); });
            Click("BtnClearLog", delegate { Get<TextBox>("LogBox").Clear(); });
            Click("BtnToggleLog", delegate { ShowLog(!logOpen); });
            Click("DlgOk", delegate { HideDialog(); var a = dialogOk; dialogOk = null; if (a != null) a(); });
            Click("DlgCancel", delegate { HideDialog(); dialogOk = null; });
            Window.PreviewKeyDown += delegate (object s, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key != System.Windows.Input.Key.Escape) return;
                if (Get<Grid>("Overlay").Visibility == Visibility.Visible) { HideDialog(); dialogOk = null; e.Handled = true; }
                else if (Get<Grid>("PickSheet").Visibility == Visibility.Visible) { Get<Grid>("PickSheet").Visibility = Visibility.Collapsed; e.Handled = true; }
                else if (Get<Grid>("ReviewSheet").Visibility == Visibility.Visible) { CloseReview(); e.Handled = true; }
                else if (Get<Grid>("SearchSheet").Visibility == Visibility.Visible) { CloseSearch(); e.Handled = true; }
            };

            InitApps();
            InitTweaks();
            InitDrivers();
            InitProgress();
            InitUpdates();
            InitSettings();

            LoadList(false);
            RefreshSystem();
            RecoverInterruptedRestore();

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += delegate { PollProgress(); };
            timer.Start();
            Window.Loaded += delegate
            {
                Engine.TidyDataDir();
                PollProgress();
                JobState job = JobState.Load();
                if (job != null && job.Stage == "running") Engine.SetReopenAfterRestart();   // keep reopening until it finishes
                if (openProgress || (job != null && job.Stage == "running")) Get<RadioButton>("NavProgress").IsChecked = true;
                new Thread(delegate () { bool online = Engine.HasInternet(); Ui(delegate { Get<TextBlock>("SysNet").Text = online ? "Online" : "No internet connection"; RefreshKit(); }); }) { IsBackground = true }.Start();
            };
        }

        // ---------------------------------------------------------------- helpers
        T Get<T>(string name) where T : class
        {
            var o = Window.FindName(name) as T;
            if (o == null) throw new InvalidOperationException("UI element missing: " + name);
            return o;
        }
        void Click(string name, RoutedEventHandler h) { Get<Button>(name).Click += h; }
        void Act(string name, RoutedEventHandler h) { var b = Get<Button>(name); b.Click += h; actionControls.Add(b); }
        Style StyleOf(string key) { return (Style)Window.FindResource(key); }
        void Ui(Action a) { if (Window.Dispatcher.CheckAccess()) a(); else Window.Dispatcher.BeginInvoke(a); }
        static Dictionary<string, string> Vals(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        void ShowPage(string page)
        {
            foreach (string p in pages) Get<FrameworkElement>(p).Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
            if (page == "PageProgress") PollProgress();
            if (page == "PageDrivers") RefreshKit();
            if (page == "PageTweaks" && !tweaksScanned) RefreshTweakState();
            if (page == "PageUpdates") RefreshSchedule();
            if (page == "PageSettings") RefreshSettingsAvailability();
        }

        void Navigate(string nav) { Get<RadioButton>(nav).IsChecked = true; }

        void DarkTitleBar()
        {
            try
            {
                IntPtr h = new WindowInteropHelper(Window).Handle;
                int on = 1; DwmSetWindowAttribute(h, 20, ref on, 4);
                int caption = 0x001C1613; DwmSetWindowAttribute(h, 35, ref caption, 4);
                int border = 0x00352C27; DwmSetWindowAttribute(h, 34, ref border, 4);
            }
            catch { }
        }

        // ---------------------------------------------------------------- dialog
        void ShowDialog(string title, string text, string ok, string cancel, Action onOk, params UIElement[] extras)
        {
            Get<TextBlock>("DlgTitle").Text = title;
            var t = Get<TextBlock>("DlgText");
            t.Text = text ?? "";
            t.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            var extra = Get<StackPanel>("DlgExtra");
            extra.Children.Clear();
            foreach (UIElement e in extras) extra.Children.Add(e);
            extra.Visibility = extras.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("DlgOk").Content = ok;
            var c = Get<Button>("DlgCancel");
            c.Visibility = cancel == null ? Visibility.Collapsed : Visibility.Visible;
            if (cancel != null) c.Content = cancel;
            dialogOk = onOk;
            Get<Grid>("Overlay").Visibility = Visibility.Visible;
            Get<Button>("DlgOk").Focus();
        }
        void HideDialog() { Get<Grid>("Overlay").Visibility = Visibility.Collapsed; }
        void Confirm(string title, string text, string ok, Action onOk, params UIElement[] extras) { ShowDialog(title, text, ok, "Cancel", onOk, extras); }
        void Info(string title, string text) { ShowDialog(title, text, "OK", null, null); }

        CheckBox Switch(string text, bool on)
        {
            return new CheckBox { Style = StyleOf("Switch"), Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 }, IsChecked = on, Margin = new Thickness(0, 0, 0, 12) };
        }

        TextBlock Note(string text)
        {
            return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Window.FindResource("Subtle"), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
        }

        // ---------------------------------------------------------------- activity log
        void ShowLog(bool open)
        {
            logOpen = open;
            Get<TextBox>("LogBox").Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            Get<TextBlock>("ToggleLogGlyph").Text = open ? "" : "";
        }

        void Log(string line)
        {
            if (!Window.Dispatcher.CheckAccess()) { Window.Dispatcher.BeginInvoke(new Action(delegate { Log(line); })); return; }
            var box = Get<TextBox>("LogBox");
            if (box.LineCount > 6000) box.Text = box.Text.Substring(box.Text.Length / 2);
            box.AppendText(line + "\r\n");
            box.ScrollToEnd();
            if (line.StartsWith("==> ") && running) Get<TextBlock>("StatusText").Text = line.Substring(4) + "...";
        }

        void SetStatus(string text, Brush dot) { Get<TextBlock>("StatusText").Text = text; Get<System.Windows.Shapes.Ellipse>("StatusDot").Fill = dot; }

        void SetRunning(bool value, string label)
        {
            if (value) { tweakStateEpoch++; if (debloat != null) debloat.InvalidateDetection(); InvalidateAuditResults(); }
            running = value;
            foreach (UIElement c in actionControls) c.IsEnabled = !value;
            if (!value) { if (debloat != null) RefreshTweakCounts(); else UpdateCount(); }
            Get<ProgressBar>("Busy").Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value) SetStatus(label + "...", DotBusy);
        }

        void Finished(string label, int rc)
        {
            string meaning; Brush dot;
            if (rc == 0) { meaning = "done"; dot = DotOk; }
            else if (rc == 3010) { meaning = "restarting to continue"; dot = DotWarn; }
            else if (rc == 2) { meaning = "done, some items skipped"; dot = DotWarn; }
            else if (rc == Proc.UacDeclined) { meaning = "cancelled at the administrator prompt"; dot = DotWarn; }
            else { meaning = "finished with errors"; dot = DotBad; }
            Log("━━ " + label + ": " + meaning + (rc == Proc.UacDeclined ? "" : " (exit code " + rc + ")"));
            SetRunning(false, null);
            SetStatus(label + ": " + meaning + (rc == Proc.UacDeclined ? "" : " (exit code " + rc + ")"), dot);
            RefreshSystem();
            if (Get<Grid>("PageTweaks").Visibility == Visibility.Visible) RefreshTweakState();
        }

        // ---------------------------------------------------------------- run the engine runner (no elevation, output captured)
        void RunRunner(string label, string action, Dictionary<string, string> values, List<string> switches, Action<int> done, string listOverride = null)
        {
            if (running) return;
            bool custom;
            string listSnapshot = listOverride ?? Engine.CurrentListText(out custom);
            var sw = switches ?? new List<string>();
            sw.Add("IncludeDisabled");
            SetRunning(true, label);
            ShowLog(true);
            Log("");
            Log("━━ " + label + "  ·  " + DateTime.Now.ToString("HH:mm:ss"));
            new Thread(delegate ()
            {
                string work = null, engine = null; int rc = 1;
                try
                {
                    work = Engine.ExtractWork(); engine = Engine.ExtractEngine();
                    File.WriteAllText(Path.Combine(work, "lists.txt"), listSnapshot, new UTF8Encoding(false));
                    var psi = new ProcessStartInfo(Engine.PowerShellPath, Engine.RunnerArguments(work, engine, action, values, sw, true));
                    rc = Proc.Stream(psi, Log, delegate (Process p) { current = p; });
                }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
                finally { current = null; Engine.Cleanup(work); Engine.Cleanup(engine); }
                Ui(delegate { Finished(label, rc); if (done != null) done(rc); });
            }) { IsBackground = true }.Start();
        }

        // ---------------------------------------------------------------- the elevated job (one UAC prompt)
        void RunJob(JobSpec spec, Action<int> done)
        {
            if (running) return;
            List<string> pending = Engine.PendingRuns();
            JobState existingJob = JobState.Load();
            if (spec.Action != "Resume" && spec.Action != "CancelResume" && ((pending != null && pending.Count > 0) || (existingJob != null && existingJob.WorkerAlive)))
            { Info("A setup is already in progress", "Continue or cancel the saved setup on Progress before starting another job."); return; }
            SetRunning(true, spec.Title);
            ShowLog(true);
            Log("");
            Log("━━ " + spec.Title + "  ·  " + DateTime.Now.ToString("HH:mm:ss"));
            Log("Waiting for administrator permission...");
            string logFile = Engine.NewLogFile("job");
            bool customList;
            if (spec.ListText == null) spec.ListText = Engine.CurrentListText(out customList);
            if (spec.Action != "Resume" && spec.Action != "CancelResume")
            {
                var previous = SetupProgress.Load() ?? sessionSetup;
                sessionSetup = SetupProgress.Begin(spec, previous);
                SetupProgress.Save(sessionSetup);
            }
            Navigate("NavProgress");
            if (spec.TrackAsInstall)
            {
                var state = new JobState { StartedUtc = DateTime.UtcNow.ToString("o"), Stage = "running", LogFile = logFile, Kind = spec.Action ?? "", RestoreZip = spec.RestoreZip ?? "", AllowMismatch = spec.AllowMismatch };
                try { state.Save(); } catch (Exception ex) { Log("Couldn't save job state: " + ex.Message); }
                Engine.SetReopenAfterRestart();
            }
            jobRunning = true;
            new Thread(delegate ()
            {
                string work = null, engine = null; int rc = 1;
                try
                {
                    work = Engine.ExtractWork();
                    File.WriteAllText(Path.Combine(work, "lists.txt"), spec.ListText, new UTF8Encoding(false));
                    var job = new Dictionary<string, object>();
                    using (var identity = WindowsIdentity.GetCurrent()) job["ExpectedUserSid"] = identity.User.Value;
                    job["LogDir"] = Engine.LogsDir;
                    job["LogFile"] = logFile;
                    job["SelfPath"] = Engine.ExePath;
                    job["StagePath"] = SetupProgress.PathName;
                    if (spec.Action != null)
                    {
                        engine = Engine.ExtractEngine();
                        job["Action"] = spec.Action; job["EngineDir"] = engine; job["RunnerPath"] = Path.Combine(work, "Runner.ps1");
                        job["ListPath"] = Path.Combine(work, "lists.txt");
                        if (!string.IsNullOrEmpty(spec.Apps)) job["Apps"] = spec.Apps;
                        if (spec.Upgrade) job["Upgrade"] = true;
                        if (spec.SkipDrivers) job["SkipDrivers"] = true;
                        if (!string.IsNullOrEmpty(spec.RunId)) job["RunId"] = spec.RunId;
                        if (!string.IsNullOrEmpty(spec.DriverIds)) job["DriverIds"] = spec.DriverIds;
                    }
                    if (!string.IsNullOrEmpty(spec.DriverKit)) job["DriverKit"] = spec.DriverKit;
                    if (!string.IsNullOrEmpty(spec.WifiKit)) job["WifiKit"] = spec.WifiKit;
                    if (spec.DebloatArgs != null && spec.DebloatArgs.Count > 0)
                    {
                        job["DebloatRoot"] = Engine.ExtractDebloat(work);
                        job["DebloatArgs"] = spec.DebloatArgs.ToArray();
                    }
                    string jobFile = Path.Combine(work, "job.json");
                    File.WriteAllText(jobFile, Engine.Json.Serialize(job), new UTF8Encoding(false));
                    rc = Proc.Elevated(Path.Combine(work, "Job.ps1"), "-JobFile " + Engine.Q(jobFile), logFile, Log, delegate (Process p)
                    {
                        current = p;
                        if (spec.TrackAsInstall) { JobState state = JobState.Load(); if (state != null) { state.WorkerPid = p.Id; state.Save(); } }
                    });
                }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
                finally { current = null; Engine.Cleanup(work); Engine.Cleanup(engine); }
                Ui(delegate
                {
                    jobRunning = false;
                    sessionSetup = SetupProgress.Load() ?? sessionSetup;
                    if (rc != 0 && rc != 3010 && rc != Proc.UacDeclined && !SetupProgress.HasFailures(sessionSetup))
                    {
                        SetupStage step = sessionSetup == null ? null : sessionSetup.Steps.Find(s => s.Status == "Running" || s.Status == "Pending");
                        if (step != null) { step.Status = "Failed"; step.Detail = "Setup stopped (exit code " + rc + "). Open the activity log for details."; }
                        if (sessionSetup != null) { foreach (SetupStage s in sessionSetup.Steps) if (s.Status == "Pending") { s.Status = "Blocked"; s.Detail = "Waiting for retry"; } SetupProgress.Save(sessionSetup); }
                    }
                    if (spec.TrackAsInstall && rc == Proc.UacDeclined) { JobState.Clear(); Engine.ClearReopenAfterRestart(); }
                    bool engineActive = spec.TrackAsInstall && rc != 3010 && Runs.ReadActive() != null;
                    bool engineReportAvailable = spec.TrackAsInstall && sessionSetup != null && !string.IsNullOrEmpty(sessionSetup.EngineRunId) && Runs.ReadLatestReport(DateTime.MinValue, sessionSetup.EngineRunId) != null;
                    bool verifyAfterExit = SetupProgress.VerifyAfterExit(spec, rc, engineActive, engineReportAvailable);
                    if (spec.TrackAsInstall && rc != 3010 && !engineActive && !engineReportAvailable)
                    { JobState.Clear(); Engine.ClearReopenAfterRestart(); }
                    // Leave nothing on the PC after a successful (or cancelled) job; installs clean up when they complete.
                    if (rc == Proc.UacDeclined || (rc == 0 && !spec.TrackAsInstall && string.IsNullOrEmpty(spec.RestoreZip)))
                    {
                        Engine.DeleteFile(logFile);
                        if (spec.DebloatArgs != null) Engine.DeleteFile(Path.Combine(Engine.LogsDir, "Win11Debloat.log"));
                        Engine.DeleteFile(SetupProgress.PathName);
                        if (rc == Proc.UacDeclined) sessionSetup = null;
                        Engine.TidyDataDir();
                    }
                    else if (rc != 0 && rc != 3010) Log("Log kept for troubleshooting: " + logFile + " (delete it on the Progress page).");
                    Finished(spec.Title, rc);
                    // Tracked installs that produced (or are still producing) an engine report notify from CompleteJob.
                    bool reportFollows = spec.TrackAsInstall && rc != 3010 && (engineActive || engineReportAvailable);
                    string noticeTitle, noticeText; bool noticeWarning;
                    Action notify = delegate { if (spec.Action != "CancelResume" && !reportFollows && SetupSignals.JobNotice(spec.Title, rc, out noticeTitle, out noticeText, out noticeWarning)) NotifyFinished(noticeTitle, noticeText, noticeWarning || SetupProgress.HasFailures(sessionSetup)); };
                    if (rc == 0 && !spec.TrackAsInstall && !string.IsNullOrEmpty(spec.RestoreZip)) RestoreSetupSettings(spec, delegate { PollProgress(); notify(); StartPostSetupVerification(sessionSetup); });
                    else { notify(); if (verifyAfterExit) StartPostSetupVerification(sessionSetup); }
                    if (done != null) done(rc);
                    PollProgress();
                });
            }) { IsBackground = true }.Start();
        }

        void RefreshSystem()
        {
            var windows = SystemFacts.ReadWindows();
            Get<TextBlock>("SysWindows").Text = windows.Edition;
            Get<TextBlock>("SysBuild").Text = windows.Build;
            Get<TextBlock>("SysArchitecture").Text = Environment.Is64BitOperatingSystem ? "64-bit Windows" : "32-bit Windows";
            Get<TextBlock>("SysDisk").Text = SystemFacts.SystemDrive();
            Get<TextBlock>("SysWinget").Text = Engine.WingetExe() == null ? "winget not available yet" : "winget available";
            string choco = Engine.ChocolateyVersion();
            Get<TextBlock>("SysChoco").Text = choco == null ? "Chocolatey not installed yet" : "Chocolatey " + choco;
            Get<TextBlock>("ChocoStatus").Text = choco == null ? "Chocolatey: not installed yet" : "Chocolatey " + choco;
            Get<TextBlock>("ChocoDetail").Text = choco == null
                ? "Installed automatically when you install apps or drivers. You can also set it up on its own."
                : "Updated to the latest stable release automatically before each install.";
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!running || allowClose) return;
            e.Cancel = true;
            ShowDialog("A step is still running",
                jobRunning ? "The install keeps running in the background and continues after restarts. Freshkit will reopen to show progress when you sign back in." :
                             "If you close Freshkit now, it stops waiting and deletes its temporary files.",
                "Close anyway", "Keep open",
                delegate
                {
                    allowClose = true;
                    try { if (!jobRunning && current != null && !current.HasExited) current.Kill(); } catch { }
                    Window.Close();
                });
        }
    }
}
