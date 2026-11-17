using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Freshkit
{
    public sealed class SetupStage
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
    }
    internal sealed class SetupRecord
    {
        public string StartedUtc;
        public string EngineRunId = "";
        public JobSpec Recipe;
        public JobSpec VerificationJob;
        public List<SetupStage> Steps;
    }
    internal static class SetupProgress
    {
        public static bool VerifyAfterExit(JobSpec spec, int exitCode, bool engineActive, bool engineReportAvailable)
        {
            return spec.Action != "CancelResume" && exitCode != 3010 && exitCode != Proc.UacDeclined &&
                !engineActive && (!spec.TrackAsInstall || !engineReportAvailable);
        }

        public static SetupRecord Begin(JobSpec spec, SetupRecord previous)
        {
            bool retry = spec.Title == "Retrying setup" && previous != null;
            var original = retry ? previous.VerificationJob ?? previous.Recipe : spec;
            var record = new SetupRecord { StartedUtc = DateTime.UtcNow.ToString("o"), Recipe = spec, Steps = Create(spec),
                VerificationJob = Engine.Json.Deserialize<JobSpec>(Engine.Json.Serialize(original)) };
            if (retry && previous.Steps != null) foreach (var step in record.Steps)
            {
                var old = previous.Steps.Find(s => s.Id == step.Id);
                if (old != null && old.Status == "Done") { step.Status = old.Status; step.Detail = old.Detail; }
            }
            return record;
        }

        public static string PathName { get { return Path.Combine(Engine.LogsDir, "setup-steps.json"); } }
        static bool Selected(JobSpec s, string id)
        {
            switch (id)
            {
                case "OfflineDrivers": return !string.IsNullOrEmpty(s.DriverKit);
                case "Wifi": return !string.IsNullOrEmpty(s.WifiKit);
                case "Tweaks": return s.DebloatArgs != null && s.DebloatArgs.Count > 0;
                case "Bootstrap": return s.Action != null && s.Action != "CancelResume";
                case "Drivers": return (s.Action == "Install" && !s.SkipDrivers) || s.Action == "Drivers" || s.Action == "Resume";
                case "Apps": return s.Action == "Install" || s.Action == "Resume";
                case "Settings": return !string.IsNullOrEmpty(s.RestoreZip);
                default: return false;
            }
        }
        public static List<SetupStage> Create(JobSpec spec)
        {
            var list = new List<SetupStage>();
            string[] ids = { "OfflineDrivers", "Wifi", "Tweaks", "Bootstrap", "Drivers", "Apps", "Settings" };
            string[] names = { "Offline drivers", "Wi-Fi networks", "Windows tweaks", "Chocolatey setup", "Hardware drivers", "Apps and runtimes", "Restore settings" };
            for (int i = 0; i < ids.Length; i++) list.Add(new SetupStage { Id = ids[i], Name = names[i], Status = Selected(spec, ids[i]) ? "Pending" : "Skipped", Detail = Selected(spec, ids[i]) ? "Waiting" : "Not selected" });
            return list;
        }
        public static SetupRecord Load()
        {
            object raw = Engine.ReadJsonFile(PathName);
            if (raw == null) return null;
            try { return Engine.Json.Deserialize<SetupRecord>(Engine.Json.Serialize(raw)); } catch { return null; }
        }
        public static void Save(SetupRecord record)
        {
            Directory.CreateDirectory(Engine.LogsDir);
            string temporary = PathName + ".partial";
            File.WriteAllText(temporary, Engine.Json.Serialize(record), new UTF8Encoding(false));
            if (File.Exists(PathName)) File.Replace(temporary, PathName, null); else File.Move(temporary, PathName);
        }
        public static void Set(SetupRecord record, string id, string status, string detail)
        {
            if (record == null || record.Steps == null) return;
            SetupStage stage = record.Steps.Find(s => s.Id == id);
            if (stage != null) { stage.Status = status; stage.Detail = detail; }
        }
        public static bool HasFailures(SetupRecord record)
        {
            return record != null && record.Steps != null && record.Steps.Exists(s => s.Status == "Failed");
        }
        public static void InterruptRestore(SetupRecord record)
        {
            Set(record, "Settings", "Failed", "Settings restore was interrupted. Retry to finish restoring the remaining settings.");
        }
        public static JobSpec Retry(JobSpec recipe, List<SetupStage> steps, RunSnapshot report, string expectedRunId = null)
        {
            var retry = Engine.Json.Deserialize<JobSpec>(Engine.Json.Serialize(recipe));
            retry.Title = "Retrying setup";
            Func<string, bool> needed = id => steps.Exists(s => s.Id == id && (s.Status == "Failed" || s.Status == "Blocked" || s.Status == "Pending" || s.Status == "Running"));
            if (!needed("OfflineDrivers")) retry.DriverKit = null;
            if (!needed("Wifi")) retry.WifiKit = null;
            if (!needed("Tweaks")) retry.DebloatArgs = null;
            if (!needed("Settings")) retry.RestoreZip = null;
            bool apps = needed("Apps"), drivers = needed("Drivers");
            retry.Action = apps ? "Install" : drivers ? "Drivers" : needed("Bootstrap") ? "Bootstrap" : null;
            retry.SkipDrivers = !drivers;
            if (drivers && report != null && !string.IsNullOrEmpty(expectedRunId) && report.RunId == expectedRunId)
            {
                var ids = new List<string>();
                foreach (RunRow row in report.Rows) if (row.Kind == "Driver" && (row.Outcome == "Failed" || row.Outcome == "Pending")) ids.Add(row.Package);
                if (ids.Count > 0) retry.DriverIds = string.Join(",", ids.ToArray());
            }
            if (apps && report != null && !string.IsNullOrEmpty(expectedRunId) && report.RunId == expectedRunId)
            {
                var ids = new List<string>();
                bool custom;
                var packages = ListFile.Parse(recipe.ListText ?? Engine.CurrentListText(out custom)).Packages;
                var approved = new HashSet<string>((recipe.Apps ?? "").Split(','), StringComparer.OrdinalIgnoreCase);
                foreach (RunRow row in report.Rows) if (row.Kind != "Driver" && (row.Outcome == "Failed" || row.Outcome == "Pending"))
                    foreach (PackageEntry p in packages)
                    {
                        string key = Data.KeyFor(p.Source, p.Id);
                        if (approved.Contains(key) && Data.EngineKeyFor(p.Source, p.Id) == row.Package) ids.Add(key);
                    }
                if (ids.Count > 0) retry.Apps = string.Join(",", ids.ToArray());
            }
            retry.TrackAsInstall = retry.Action == "Install" || retry.Action == "Drivers";
            retry.RunId = null;
            return retry;
        }
    }
}
