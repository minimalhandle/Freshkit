// Freshkit - live install progress from the engine's state.json, and the final reports it leaves behind.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Media;

namespace Freshkit
{
    public sealed class RunRow
    {
        public string Name { get; set; }
        public string Package { get; set; }
        public string Detail { get; set; }
        public string StatusText { get; set; }
        public string Kind { get; set; }
        public string Glyph { get; set; }
        public Brush GlyphBrush { get; set; }
        public string Outcome { get; set; }   // Installed | Failed | Skipped | Pending | Running
    }

    internal sealed class RunSnapshot
    {
        public string RunId = "", Status = "", Failure = "", Current = "";
        public bool IsReport;
        public DateTime When;
        public readonly List<RunRow> Rows = new List<RunRow>();
        public int Ok, Bad, Skip, Queue;
        public int Done { get { return Ok + Bad + Skip; } }
        public int Total { get { return Done + Queue; } }
    }

    internal static class Runs
    {
        static Brush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        static readonly Brush OkBrush = B("#34D399"), BadBrush = B("#F87171"), SkipBrush = B("#9AA3AF"), RunBrush = B("#2DD4BF"), QueueBrush = B("#6B7280");

        static string Outcome(string status)
        {
            switch (status)
            {
                case "PackageInstalled": case "RebootRequired": case "RecoveredInstalled": case "Installed": return "Installed";
                case "Failed": case "UnverifiedDriverInstall": return "Failed";
                case "Pending": case "DeferredForReboot": return "Pending";
                case "Cancelled": return "Skipped";
                default: return "Skipped";
            }
        }

        static RunRow Row(string name, string package, string outcome, string statusText, string detail, string kind)
        {
            var r = new RunRow { Name = name.Length > 0 ? name : package, Package = package, Outcome = outcome, Detail = detail, Kind = kind, StatusText = statusText };
            switch (outcome)
            {
                case "Installed": r.Glyph = ""; r.GlyphBrush = OkBrush; break;
                case "Failed": r.Glyph = ""; r.GlyphBrush = BadBrush; break;
                case "Running": r.Glyph = ""; r.GlyphBrush = RunBrush; break;
                case "Pending": r.Glyph = ""; r.GlyphBrush = QueueBrush; break;
                default: r.Glyph = ""; r.GlyphBrush = SkipBrush; break;
            }
            return r;
        }

        static string Friendly(string status, string outcome)
        {
            switch (status)
            {
                case "PackageInstalled": case "Installed": return "Installed";
                case "RebootRequired": return "Installed · restart";
                case "RecoveredInstalled": return "Installed (after restart)";
                case "AlreadyInstalled": return "Already installed";
                case "NotSelected": return "Not selected";
                case "NotApplicable": return "Not needed";
                case "UnverifiedDriverInstall": return "Unverified";
                case "DeferredForReboot": return "After restart";
                default: return outcome == "Skipped" ? (status.Length > 0 ? status : "Skipped") : outcome;
            }
        }

        static string KindOf(string package, bool driver)
        {
            if (driver) return "Driver";
            if (package.StartsWith("winget-")) return "winget";
            if (package.StartsWith("github-")) return "GitHub";
            if (package.StartsWith("vcredist") || package.StartsWith("dotnet")) return "Runtime";
            return "Chocolatey";
        }

        static void Count(RunSnapshot s, RunRow r)
        {
            if (r.Outcome == "Installed") s.Ok++;
            else if (r.Outcome == "Failed") s.Bad++;
            else if (r.Outcome == "Pending" || r.Outcome == "Running") s.Queue++;
            else s.Skip++;
        }

        /// The newest unfinished engine run, read live.
        public static RunSnapshot ReadActive()
        {
            List<string> ids = Engine.PendingRuns();
            if (ids == null) return null;
            foreach (string id in ids)
            {
                object st = Engine.ReadJsonFile(Path.Combine(Engine.ResumeRoot, id, "state.json"));
                if (st == null) continue;
                var s = new RunSnapshot { RunId = id, Status = J.S(st, "Status"), Failure = J.S(st, "Failure") };
                DateTime.TryParse(J.S(st, "UpdatedUtc"), null, DateTimeStyles.RoundtripKind, out s.When);
                object inflight = J.Get(st, "InFlight");
                string inflightId = J.S(inflight, "PackageId");
                foreach (string phase in new[] { "Driver", "App" })
                {
                    bool driver = phase == "Driver";
                    foreach (object r in J.A(st, phase + "Results"))
                    {
                        string pkg = J.S(r, "PackageId"), status = J.S(r, "Status");
                        if (driver && status == "NotSelected") continue;
                        string outcome = Outcome(status);
                        var row = Row(J.S(r, "Name"), pkg, outcome, Friendly(status, outcome), Detail(r), KindOf(pkg, driver));
                        s.Rows.Add(row); Count(s, row);
                    }
                    foreach (object q in J.A(st, phase + "Queue"))
                    {
                        string pkg = driver ? J.S(q, "Id") : J.S(q, "packageId");
                        string name = driver ? J.S(q, "Name") : J.S(q, "name");
                        bool running = pkg == inflightId && s.Status == "Running";
                        var row = Row(name, pkg, running ? "Running" : "Pending", running ? "Installing..." : (s.Status == "AwaitingReboot" ? "After restart" : "Queued"),
                            running ? "In progress" : "", KindOf(pkg, driver));
                        if (running) s.Current = row.Name;
                        s.Rows.Add(row); Count(s, row);
                    }
                }
                // Running item first, then queued, then finished (newest results last stay readable).
                s.Rows.Sort(delegate (RunRow a, RunRow b) { return Rank(a).CompareTo(Rank(b)); });
                return s;
            }
            return null;
        }

        static int Rank(RunRow r) { return r.Outcome == "Running" ? 0 : r.Outcome == "Failed" ? 1 : r.Outcome == "Pending" ? 3 : 2; }

        static string Detail(object r)
        {
            string version = J.S(r, "Version"), detail = J.S(r, "Detail");
            if (detail.StartsWith("Chocolatey returned success") || detail.StartsWith("Installed with winget")) detail = "";
            return (version.Length > 0 && version != "Not recorded" ? "v" + version + (detail.Length > 0 ? "  ·  " : "") : "") + detail;
        }

        /// The newest final report written by the engine (optionally only if newer than a time).
        public static RunSnapshot ReadLatestReport(DateTime notBeforeUtc, string runId = null)
        {
            try
            {
                if (!Directory.Exists(Engine.RunsDir)) return null;
                FileInfo newest = null;
                foreach (FileInfo f in new DirectoryInfo(Engine.RunsDir).GetFiles("*.json"))
                    if ((runId == null || f.Name.Equals(runId + ".json", StringComparison.OrdinalIgnoreCase)) && (newest == null || f.LastWriteTimeUtc > newest.LastWriteTimeUtc)) newest = f;
                if (newest == null || newest.LastWriteTimeUtc < notBeforeUtc) return null;
                object rep = Engine.ReadJsonFile(newest.FullName);
                if (rep == null) return null;
                var s = new RunSnapshot { RunId = J.S(rep, "RunId"), Status = J.S(rep, "Status"), Failure = J.S(rep, "Failure"), IsReport = true, When = newest.LastWriteTime };
                foreach (object r in J.A(rep, "Rows"))
                {
                    string type = J.S(r, "Type"), pkg = J.S(r, "Package"), status = J.S(r, "Status"), outcome = J.S(r, "Outcome");
                    if (type == "Driver" && status == "NotSelected") continue;
                    if (outcome == "Cancelled") outcome = "Skipped";
                    var row = Row(J.S(r, "Name"), pkg, outcome, Friendly(status, outcome), Detail(r), type == "Driver" ? "Driver" : KindOf(pkg, false));
                    s.Rows.Add(row); Count(s, row);
                }
                s.Rows.Sort(delegate (RunRow a, RunRow b) { return Rank(a).CompareTo(Rank(b)); });
                return s;
            }
            catch { return null; }
        }
    }
}
