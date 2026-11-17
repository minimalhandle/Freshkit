using System;
using System.Collections.Generic;
using System.Text;

namespace Freshkit
{
    internal sealed class AuditTarget
    {
        public SetupRecipe Recipe;
        public bool CheckDevices;
        public List<SetupStage> Stages;

        public static AuditTarget FromJob(JobSpec spec)
        {
            var recipe = new SetupRecipe { ListText = spec.ListText, HasWindowsSelections = true };
            if ((spec.Action == "Install" || spec.Action == "Resume") && !string.IsNullOrEmpty(spec.Apps))
                recipe.Apps.AddRange(spec.Apps.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            if (spec.DebloatArgs != null)
                for (int i = 0; i < spec.DebloatArgs.Count; i++)
                {
                    string arg = spec.DebloatArgs[i];
                    if (arg == "-Apps" && i + 1 < spec.DebloatArgs.Count) { recipe.Remove.AddRange(spec.DebloatArgs[++i].Split(',')); continue; }
                    if (arg == "-ForceRemoveEdge") { recipe.ForceEdge = true; continue; }
                    if (arg == "-CreateRestorePoint" || arg == "-RemoveApps") continue;
                    if (arg.StartsWith("-", StringComparison.Ordinal)) recipe.Tweaks.Add(arg.Substring(1));
                }
            return new AuditTarget { Recipe = recipe, CheckDevices = (spec.Action == "Install" && !spec.SkipDrivers) || spec.Action == "Drivers" || !string.IsNullOrEmpty(spec.DriverKit) };
        }
    }

    internal sealed class AuditDevice { public string Name, Id; public int Code; }
    internal sealed class AuditSnapshot
    {
        public DateTime CheckedUtc = DateTime.UtcNow;
        public InstalledSnapshot Registry;
        public bool WingetComplete, ChocoComplete, AppxComplete, DevicesComplete;
        public bool? RestartPending;
        public readonly Dictionary<string, string> Choco = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Winget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Features = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Appx = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<AuditDevice> Devices = new List<AuditDevice>();
        public readonly Dictionary<string, BackupAvailability> Backups = new Dictionary<string, BackupAvailability>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Errors = new List<string>();
    }
    internal sealed class AuditRow
    {
        public string Category { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public bool Issue { get { return Status != "Verified"; } }
    }
    internal sealed class AuditReport
    {
        public DateTime CheckedUtc;
        public readonly List<AuditRow> Rows = new List<AuditRow>();
        public string Summary
        {
            get
            {
                int verified = 0, attention = 0, unknown = 0;
                foreach (var row in Rows) if (row.Status == "Verified") verified++; else if (row.Status == "Unknown") unknown++; else attention++;
                return verified + " verified · " + attention + " need attention · " + unknown + " unknown";
            }
        }
    }

    internal static class SetupAudit
    {
        static void Add(AuditReport report, string category, string key, string name, string status, string detail)
        { report.Rows.Add(new AuditRow { Category = category, Key = key, Name = name, Status = status, Detail = detail }); }

        public static AuditReport Evaluate(AuditTarget target, AuditSnapshot facts, Debloat definitions)
        {
            var report = new AuditReport { CheckedUtc = facts.CheckedUtc };
            var recipe = target.Recipe;
            var selected = new HashSet<string>(recipe.Apps, StringComparer.OrdinalIgnoreCase);
            foreach (var package in ListFile.Parse(recipe.ListText).Packages)
            {
                string key = Data.KeyFor(package.Source, package.Id);
                if (!selected.Contains(key)) continue;
                string version;
                if (package.Source == "choco" && facts.Choco.TryGetValue(package.Id, out version))
                { Add(report, "Apps", key, package.Name, "Verified", "Chocolatey package registered · " + version + " · " + package.Id); continue; }
                if (package.Source == "winget" && facts.Winget.TryGetValue(package.Id, out version))
                { Add(report, "Apps", key, package.Name, "Verified", "winget package detected · " + version + " · " + package.Id); continue; }
                var match = facts.Registry == null ? null : facts.Registry.Resolve(package.Source, package.Id, package.Name);
                bool store = package.IsWinget && Data.IsStoreId(package.Id);
                if (match != null && match.Confirmed && (facts.Registry.Complete || match.Reason != "Unique installed display name") && (!store || match.Reason == "Exact package identity"))
                { Add(report, "Apps", key, package.Name, "Verified", match.Reason + " · " + match.Record.Version + " · " + package.Id); continue; }
                bool uncertain = match != null && (match.Record != null || match.Reason.StartsWith("Multiple", StringComparison.Ordinal));
                bool complete = facts.Registry != null && facts.Registry.Complete && (package.IsGitHub || (package.IsWinget ? facts.WingetComplete : facts.ChocoComplete));
                Add(report, "Apps", key, package.Name, uncertain || !complete || store ? "Unknown" : "Missing",
                    uncertain ? match.Reason + ". Confirm the app identity before taking action. " + package.Id : store ? "This Store product was not identified by its exact local package identity. Its presence is unknown. " + package.Id : !complete ? "Required installed-app inventory was incomplete. " + package.Id : "Not found in the completed local inventories. Review setup to install it. " + package.Id);
            }
            if (recipe.HasWindowsSelections)
            {
                var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (definitions != null)
                {
                    foreach (var toggle in definitions.AllToggles) labels[toggle.FeatureId] = toggle.Label;
                    foreach (var choice in definitions.AllChoices)
                        for (int i = 0; i < choice.Values.Count; i++) foreach (string id in choice.Values[i]) labels[id] = choice.Label + ": " + choice.Options[i + 1];
                }
                foreach (string id in recipe.Tweaks)
                {
                    string state, label; labels.TryGetValue(id, out label);
                    if (!facts.Features.TryGetValue(id, out state)) state = "Unknown";
                    string status = state == "Applied" ? "Verified" : state == "Not applied" ? "Different" : "Unknown";
                    Add(report, "Windows", id, label ?? id, status, status == "Verified" ? "Current registry state matches this desired setting." : status == "Different" ? "Current state differs from the recipe. Review Windows tweaks to apply it." : "The complete effects of this setting could not be verified.");
                }
                foreach (string id in recipe.Remove)
                {
                    RemovalApp removal = definitions == null ? null : definitions.Apps.Find(x => x.AppId.Equals(id, StringComparison.OrdinalIgnoreCase) || x.AppIds.Exists(alias => alias.Equals(id, StringComparison.OrdinalIgnoreCase)));
                    bool complete = removal != null && (removal.RemovalMethod == "Appx" ? facts.AppxComplete : facts.WingetComplete);
                    bool present = false;
                    if (removal != null) foreach (string alias in removal.AppIds)
                        if (removal.RemovalMethod == "Appx" ? facts.Appx.Contains(alias) : facts.Winget.ContainsKey(alias)) present = true;
                    Add(report, "Windows", "remove:" + id, "Remove " + (removal == null ? id : removal.Name), present ? "Different" : complete ? "Verified" : "Unknown",
                        present ? "An exact matching app is still installed for this user." : complete ? "No matching app found in the completed current-user inventory." : "Removal definition or required app inventory is unavailable.");
                }
                if (recipe.ForceEdge) Add(report, "Windows", "force-edge", "Force Microsoft Edge removal", "Unknown", "Package inventory cannot prove all effects of forced Edge removal.");
                foreach (string id in recipe.Backups)
                {
                    BackupAvailability backup;
                    facts.Backups.TryGetValue(id, out backup);
                    string status = backup == null || backup.Status == "Unable to check" ? "Unknown" : backup.Status == "Settings found" ? "Verified" : "Attention";
                    Add(report, "Backup settings", "backup:" + id, id, status, backup == null ? "The recipe's backup profile could not be checked." : backup.Status + " · " + backup.Detail);
                }
            }
            if (target.CheckDevices)
            {
                foreach (var device in facts.Devices)
                    Add(report, "Devices", device.Id, device.Name, "Attention", "Windows device problem code " + device.Code + (device.Code == 28 ? " · driver not installed" : device.Code == 22 ? " · device disabled; this does not necessarily indicate an installer failure" : " · check Device Manager") + ". " + device.Id);
                if (!facts.DevicesComplete) Add(report, "Devices", "devices", "Device health", "Unknown", "Present-device inventory was incomplete. Open Device Manager to review hardware.");
                else if (facts.Devices.Count == 0) Add(report, "Devices", "devices", "Device health", "Verified", "No present devices report a Windows problem code. This does not prove the newest driver is installed.");
            }
            if (target.CheckDevices || target.Stages != null)
                Add(report, "Setup", "restart", "Pending restart", facts.RestartPending == false ? "Verified" : facts.RestartPending == true ? "Attention" : "Unknown", facts.RestartPending == true ? "Windows reports a pending restart. Restart when ready, then recheck." : facts.RestartPending == false ? "No standard servicing or file-rename restart signals found." : "Restart signals could not all be read.");
            if (target.Stages != null) foreach (var stage in target.Stages)
                if (stage.Status != "Done" && stage.Status != "Skipped") Add(report, "Setup", "stage:" + stage.Id, stage.Name, "Attention", stage.Status + " · " + stage.Detail);
            foreach (string error in facts.Errors) Add(report, "Scan", "error:" + report.Rows.Count, "Incomplete check", "Unknown", error);
            return report;
        }
    }
}
