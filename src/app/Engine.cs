// Freshkit - extraction of embedded payloads, process helpers, system facts, persisted job state.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Freshkit
{
    internal static class Engine
    {
        public const string ListFileName = "Freshkit.lists.txt";
        public const string KitFolderName = "FreshkitKit";
        static readonly string[] PayloadNames = {
            "Start-Setup.ps1", "Install-ChocolateyApps.ps1", "Install-Chocolatey.Core.psm1", "Install-Chocolatey.apps.json",
            "Settings.ps1", "Settings.Core.psm1", "Settings.Profiles.json", "Drivers.Core.psm1", "Install-Run.Core.psm1", "Resume-Run.ps1", "Freshkit.Status.ps1" };
        static readonly string[] ScriptNames = { "Job.ps1", "Updates.ps1", "Schedule.ps1", "KitExport.ps1" };

        public static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }
        public static string ExeDir { get { return Path.GetDirectoryName(ExePath); } }
        public static string ExternalListPath { get { return Path.Combine(ExeDir, ListFileName); } }
        // User-chosen export/import location; never implicitly associated with the EXE.
        public static string KitDir = "";
        public static string SessionList;

        public static Stream Resource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }

        public static string ReadResourceText(string name)
        {
            using (Stream s = Resource(name))
            {
                if (s == null) throw new InvalidOperationException("Missing embedded resource: " + name);
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        static void WriteResource(string name, string target)
        {
            using (Stream s = Resource(name))
            {
                if (s == null) throw new InvalidOperationException("Missing embedded resource: " + name);
                using (FileStream f = File.Create(target)) s.CopyTo(f);
            }
        }

        public static string CurrentListText(out bool external)
        {
            external = SessionList != null;
            return SessionList ?? ReadResourceText(ListFileName);
        }

        public static string TempBase { get { return Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\"; } }

        static string NewTempDir(string prefix)
        {
            string root = Path.Combine(TempBase, prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        /// The engine gets its own FreshWindowsSetup-<guid> folder: the engine cleans it up itself after an install
        /// and refuses to if it contains anything but its own files.
        public static string ExtractEngine()
        {
            string root = NewTempDir("FreshWindowsSetup-");
            foreach (string n in PayloadNames) WriteResource("payload/" + n, Path.Combine(root, n));
            return root;
        }

        /// Freshkit's own scratch folder: runner, list snapshot and helper scripts.
        public static string ExtractWork()
        {
            string root = NewTempDir("Freshkit-");
            WriteResource("Runner.ps1", Path.Combine(root, "Runner.ps1"));
            foreach (string n in ScriptNames) WriteResource("scripts/" + n, Path.Combine(root, n));
            WriteResource("payload/Freshkit.Status.ps1", Path.Combine(root, "Freshkit.Status.ps1"));
            // Updates.ps1 and Schedule.ps1 reuse the engine's GitHub-release installer.
            WriteResource("payload/Install-Chocolatey.Core.psm1", Path.Combine(root, "Install-Chocolatey.Core.psm1"));
            bool external;
            File.WriteAllText(Path.Combine(root, "lists.txt"), CurrentListText(out external), new UTF8Encoding(false));
            return root;
        }

        public static string ExtractDebloat(string work)
        {
            string target = Path.Combine(work, "Win11Debloat");
            using (Stream s = Resource("win11debloat.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                string full = Path.GetFullPath(target) + "\\";
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(target, e.FullName.Replace('/', '\\')));
                    if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected path in embedded Win11Debloat archive.");
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (Stream es = e.Open()) using (FileStream fs = File.Create(dest)) es.CopyTo(fs);
                }
            }
            return target;
        }

        public static void Cleanup(string root)
        {
            if (string.IsNullOrEmpty(root)) return;
            try
            {
                string full = Path.GetFullPath(root);
                if (!full.StartsWith(TempBase, StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(Path.GetFileName(full), "^(FreshWindowsSetup|Freshkit)-[a-f0-9]{32}$")) return;
                if (Directory.Exists(full)) Directory.Delete(full, true);
            }
            catch { }
        }

        public static string PowerShellPath
        {
            get
            {
                string win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                string sysnative = Path.Combine(win, @"Sysnative\WindowsPowerShell\v1.0\powershell.exe");
                if (!Environment.Is64BitProcess && File.Exists(sysnative)) return sysnative;
                return Path.Combine(win, @"System32\WindowsPowerShell\v1.0\powershell.exe");
            }
        }

        public static string Q(string v) { return "\"" + (v ?? "").Replace("\"", "") + "\""; }

        public static string RunnerArguments(string work, string engine, string action, IDictionary<string, string> values, IEnumerable<string> switches, bool capture)
        {
            var sb = new StringBuilder("-NoProfile -ExecutionPolicy Bypass ");
            if (capture) sb.Append("-NonInteractive ");
            sb.Append("-File ").Append(Q(Path.Combine(work, "Runner.ps1")));
            sb.Append(" -Action ").Append(action);
            sb.Append(" -EngineDir ").Append(Q(engine));
            sb.Append(" -ListPath ").Append(Q(Path.Combine(work, "lists.txt")));
            sb.Append(" -SelfPath ").Append(Q(ExePath));
            if (values != null)
                foreach (var kv in values)
                    if (!string.IsNullOrEmpty(kv.Value)) sb.Append(" -").Append(kv.Key).Append(' ').Append(Q(kv.Value));
            if (switches != null) foreach (string s in switches) sb.Append(" -").Append(s);
            if (capture) sb.Append(" -Capture");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- per-PC data (%LOCALAPPDATA%\Freshkit)
        public static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Freshkit"); } }
        public static string LogsDir { get { return Path.Combine(DataDir, "logs"); } }
        public static string RunsDir { get { return Path.Combine(DataDir, "runs"); } }
        public static string JobStatePath { get { return Path.Combine(DataDir, "job.json"); } }
        public static string NewLogFile(string prefix)
        {
            Directory.CreateDirectory(LogsDir);
            return Path.Combine(LogsDir, prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        }

        public static void DeleteFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        public static bool HasLogs()
        {
            try
            {
                return (Directory.Exists(LogsDir) && Directory.GetFiles(LogsDir).Length > 0) ||
                       (Directory.Exists(RunsDir) && Directory.GetFiles(RunsDir).Length > 0);
            }
            catch { return false; }
        }

        /// Removes empty log/report folders, and %LOCALAPPDATA%\Freshkit itself when nothing is left in it.
        public static void TidyDataDir()
        {
            try
            {
                foreach (string d in new[] { LogsDir, RunsDir })
                    if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d);
                if (Directory.Exists(DataDir) && Directory.GetFileSystemEntries(DataDir).Length == 0) Directory.Delete(DataDir);
            }
            catch { }
        }

        /// Deletes all logs and reports (never while an install is being tracked).
        public static void DeleteAllData()
        {
            foreach (string d in new[] { LogsDir, RunsDir })
                try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            TidyDataDir();
        }

        /// After a successful install: remove that install's logs and reports, so nothing is left on the PC.
        public static void DeleteInstallTraces(DateTime startedUtc, string jobLog)
        {
            DeleteFile(jobLog);
            DeleteFile(Path.Combine(LogsDir, "Win11Debloat.log"));
            try
            {
                if (Directory.Exists(LogsDir))
                    foreach (FileInfo f in new DirectoryInfo(LogsDir).GetFiles("resume-*.log"))
                        if (f.LastWriteTimeUtc >= startedUtc.AddMinutes(-1)) DeleteFile(f.FullName);
                if (Directory.Exists(RunsDir))
                    foreach (FileInfo f in new DirectoryInfo(RunsDir).GetFiles("*.json"))
                        if (f.LastWriteTimeUtc >= startedUtc.AddMinutes(-1)) DeleteFile(f.FullName);
            }
            catch { }
            TidyDataDir();
        }

        public static string ResumeRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FreshWindowsSetupResume"); } }

        /// Saved (unfinished) engine runs, newest first; null if the folder can't be read.
        public static List<string> PendingRuns()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(ResumeRoot)) return list;
                var dirs = new List<DirectoryInfo>(new DirectoryInfo(ResumeRoot).GetDirectories());
                dirs.Sort(delegate (DirectoryInfo a, DirectoryInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                foreach (DirectoryInfo d in dirs) if (Regex.IsMatch(d.Name, "^[a-f0-9]{32}$")) list.Add(d.Name);
                return list;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- system facts
        public static int WindowsBuild()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int b; return int.TryParse(k.GetValue("CurrentBuild") as string, out b) ? b : 0;
                }
            }
            catch { return 0; }
        }

        public static string WindowsSummary()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string product = (k.GetValue("ProductName") as string) ?? "Windows";
                    string build = (k.GetValue("CurrentBuild") as string) ?? "";
                    string display = (k.GetValue("DisplayVersion") as string) ?? "";
                    int b;
                    if (int.TryParse(build, out b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
                    return product + (display.Length > 0 ? " " + display : "") + (build.Length > 0 ? "  ·  build " + build : "");
                }
            }
            catch { return "Windows"; }
        }

        public static string ChocolateyRoot
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("ChocolateyInstall", EnvironmentVariableTarget.Machine);
                if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey");
                return root;
            }
        }
        public static string ChocoExe { get { return Path.Combine(ChocolateyRoot, "bin", "choco.exe"); } }

        public static string ChocolateyVersion()
        {
            try
            {
                string exe = Path.Combine(ChocolateyRoot, "choco.exe");
                if (!File.Exists(exe)) return null;
                var v = FileVersionInfo.GetVersionInfo(exe);
                string ver = v.ProductVersion ?? v.FileVersion ?? "";
                int plus = ver.IndexOf('+'); if (plus > 0) ver = ver.Substring(0, plus);
                return ver.Length > 0 ? ver : "installed";
            }
            catch { return null; }
        }

        public static string WingetExe()
        {
            try
            {
                string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
                if (File.Exists(alias)) return alias;
            }
            catch { }
            return null;
        }

        public static bool HasInternet()
        {
            try
            {
                if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) return false;
                System.Net.IPAddress[] a = System.Net.Dns.GetHostAddresses("community.chocolatey.org");
                return a.Length > 0;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- offline kit
        public static int KitDriverCount()
        {
            if (string.IsNullOrEmpty(KitDir)) return 0;
            try { string d = Path.Combine(KitDir, "Drivers"); return Directory.Exists(d) ? Directory.GetFiles(d, "*.inf", SearchOption.AllDirectories).Length : 0; }
            catch { return 0; }
        }
        public static int KitWifiCount()
        {
            if (string.IsNullOrEmpty(KitDir)) return 0;
            try { string d = Path.Combine(KitDir, "WiFi"); return Directory.Exists(d) ? Directory.GetFiles(d, "*.xml").Length : 0; }
            catch { return 0; }
        }

        // ---------------------------------------------------------------- reopen after sign-in
        const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
        public static void SetReopenAfterRestart()
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunOnceKey)) k.SetValue("Freshkit", "\"" + ExePath + "\" --progress"); }
            catch { }
        }
        public static void ClearReopenAfterRestart()
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunOnceKey, true)) if (k != null) k.DeleteValue("Freshkit", false); }
            catch { }
        }

        // ---------------------------------------------------------------- JSON helpers
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static object ReadJsonFile(string path)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    string text;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs, Encoding.UTF8)) text = r.ReadToEnd();
                    return Json.DeserializeObject(text.TrimStart('\uFEFF'));
                }
                catch (FileNotFoundException) { return null; }
                catch (DirectoryNotFoundException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
                catch { Thread.Sleep(150); }
            }
            return null;
        }
    }

    /// Tiny accessors for JavaScriptSerializer's Dictionary/object[] output.
    internal static class J
    {
        public static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
        public static object Get(object o, string key)
        {
            var d = D(o); object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }
        public static string S(object o, string key) { object v = Get(o, key); return v == null ? "" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture); }
        public static bool B(object o, string key) { object v = Get(o, key); return v is bool && (bool)v; }
        public static int I(object o, string key, int fallback)
        {
            object v = Get(o, key);
            if (v == null) return fallback;
            try { return Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return fallback; }
        }
        public static List<object> A(object o, string key) { return List(Get(o, key)); }
        public static List<object> List(object v)
        {
            var list = new List<object>();
            if (v == null) return list;
            var arr = v as object[];
            if (arr != null) { list.AddRange(arr); return list; }
            var al = v as System.Collections.ArrayList;
            if (al != null) { foreach (object x in al) list.Add(x); return list; }
            list.Add(v);
            return list;
        }
    }

    /// Follows a PowerShell transcript file and emits new lines (header/footer blocks removed).
    internal sealed class LogTail
    {
        readonly string path;
        long position;
        bool inHeader;
        readonly StringBuilder partial = new StringBuilder();
        Decoder decoder = new UTF8Encoding(false).GetDecoder();
        public LogTail(string path) { this.path = path; }
        public string Path { get { return path; } }

        public void Pump(Action<string> emit)
        {
            try
            {
                if (!File.Exists(path)) return;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < position) { position = 0; decoder = new UTF8Encoding(false).GetDecoder(); }
                    fs.Seek(position, SeekOrigin.Begin);
                    var buffer = new byte[65536];
                    int n;
                    while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        int start = 0;
                        if (position == 0 && n >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF) start = 3;
                        var chars = new char[decoder.GetCharCount(buffer, start, n - start)];
                        decoder.GetChars(buffer, start, n - start, chars, 0);
                        position += n;
                        partial.Append(chars);
                    }
                }
                string text = partial.ToString();
                int last = text.LastIndexOf('\n');
                if (last < 0) return;
                partial.Clear();
                partial.Append(text.Substring(last + 1));
                foreach (string raw in text.Substring(0, last).Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Trim() == "**********************") { inHeader = !inHeader; continue; }
                    if (inHeader) continue;
                    if (line.StartsWith("Transcript started, output file is") || line.StartsWith("Transcript stopped, output file is")) continue;
                    emit(line);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static class Proc
    {
        public const int UacDeclined = -2;

        /// Runs a console program and returns its output (UTF-8).
        public static int Capture(string exe, string args, out string output, int timeoutMs)
        {
            var psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.Append(e.Data).Append('\n'); };
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.Append(e.Data).Append('\n'); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } output = sb.ToString(); return -1; }
                p.WaitForExit();
                lock (sb) output = sb.ToString();
                return p.ExitCode;
            }
        }

        /// Runs a process with output streamed line by line.
        public static int Stream(ProcessStartInfo psi, Action<string> line, Action<Process> started)
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) line(e.Data); };
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) line(e.Data); };
                p.Start();
                if (started != null) started(p);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        /// Runs a PowerShell script elevated (one UAC prompt, hidden window) and follows its transcript.
        public static int Elevated(string script, string args, string logFile, Action<string> line, Action<Process> started)
        {
            var psi = new ProcessStartInfo(Engine.PowerShellPath, "-NoProfile -ExecutionPolicy Bypass -File " + Engine.Q(script) + " " + args);
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.WorkingDirectory = Environment.SystemDirectory;
            Process p;
            try { p = Process.Start(psi); }
            catch (Win32Exception ex) { if (ex.NativeErrorCode == 1223) return UacDeclined; throw; }
            using (p)
            {
                if (started != null) started(p);
                var tail = logFile != null ? new LogTail(logFile) : null;
                while (!p.WaitForExit(400)) if (tail != null) tail.Pump(line);
                Thread.Sleep(300);
                if (tail != null) tail.Pump(line);
                return p.ExitCode;
            }
        }
    }

    /// Install job remembered across restarts (%LOCALAPPDATA%\Freshkit\job.json).
    internal sealed class JobState
    {
        public string StartedUtc = "", Stage = "running", LogFile = "", Kind = "install", RestoreZip = "";
        public bool AllowMismatch;
        public int WorkerPid;

        public bool WorkerAlive
        {
            get
            {
                if (WorkerPid == 0) return false;
                try { using (Process p = Process.GetProcessById(WorkerPid)) return !p.HasExited && p.ProcessName.Equals("powershell", StringComparison.OrdinalIgnoreCase) && p.StartTime.ToUniversalTime() >= Started.AddSeconds(-5); }
                catch { return false; }
            }
        }

        public DateTime Started
        {
            get { DateTime d; return DateTime.TryParse(StartedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out d) ? d.ToUniversalTime() : DateTime.MinValue; }
        }

        public static JobState Load()
        {
            object o = Engine.ReadJsonFile(Engine.JobStatePath);
            if (o == null) return null;
            var s = new JobState();
            s.StartedUtc = J.S(o, "StartedUtc"); s.Stage = J.S(o, "Stage"); s.LogFile = J.S(o, "LogFile");
            s.Kind = J.S(o, "Kind"); s.RestoreZip = J.S(o, "RestoreZip"); s.AllowMismatch = J.B(o, "AllowMismatch");
            s.WorkerPid = J.I(o, "WorkerPid", 0);
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Engine.DataDir);
            var d = new Dictionary<string, object>();
            d["StartedUtc"] = StartedUtc; d["Stage"] = Stage; d["LogFile"] = LogFile; d["Kind"] = Kind;
            d["RestoreZip"] = RestoreZip; d["AllowMismatch"] = AllowMismatch;
            d["WorkerPid"] = WorkerPid;
            File.WriteAllText(Engine.JobStatePath, Engine.Json.Serialize(d), new UTF8Encoding(false));
        }

        public static void Clear() { try { File.Delete(Engine.JobStatePath); } catch { } }
    }
}
