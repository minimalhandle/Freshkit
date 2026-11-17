using System;
using System.Collections.Generic;
using System.IO;
namespace Freshkit
{
    internal static class AuditTests
    {
        static int checks;
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        static AuditSnapshot Facts()
        {
            return new AuditSnapshot { Registry = new InstalledSnapshot(new InstalledRecord[0], new Dictionary<string, string>()), WingetComplete = true, ChocoComplete = true, AppxComplete = true, DevicesComplete = true, RestartPending = false };
        }
        static AuditTarget Target()
        {
            return new AuditTarget { Recipe = new SetupRecipe { ListText = "::BEGIN_PACKAGES\n::PACKAGE tool|Tool\n::END_PACKAGES\n::BEGIN_GITHUB\n::GITHUB owner/tool|GitHub Tool\n::END_GITHUB\n::BEGIN_WINGET\n::WINGET Example.Tool|Example Tool|\n::END_WINGET\n", Apps = new List<string> { "tool", "github:owner/tool", "winget:Example.Tool" } } };
        }
        [STAThread] static void Main()
        {
            var target = Target(); var facts = Facts();
            facts.Choco["tool"] = "1.2"; facts.Winget["Example.Tool"] = "3.0";
            var report = SetupAudit.Evaluate(target, facts, null);
            Check(report.Rows.Find(x => x.Key == "tool").Status == "Verified" && report.Rows.Find(x => x.Key == "tool").Detail.Contains("1.2"), "Exact package presence retains installed version evidence");
            Check(report.Rows.Find(x => x.Key == "github:owner/tool").Status == "Missing", "Complete registry inventory can report an absent GitHub repository");
            facts.Registry = new InstalledSnapshot(new[] { new InstalledRecord { RegistryPath = "LocalMachine|Registry64|fixture", Name = "GitHub Tool", Version = "1.0", PackageId = "other" } }, new Dictionary<string, string>());
            Check(SetupAudit.Evaluate(target, facts, null).Rows.Find(x => x.Key == "github:owner/tool").Status == "Unknown", "A same-name GitHub app without repository identity is not verified or missing");
            facts.Registry = new InstalledSnapshot(new[] { new InstalledRecord { RegistryPath = "LocalMachine|Registry64|partial", Name = "Example Tool", Version = "2.0", PackageId = "unrelated" } }, new Dictionary<string, string>(), false);
            facts.Winget.Clear(); facts.WingetComplete = false;
            Check(SetupAudit.Evaluate(target, facts, null).Rows.Find(x => x.Key == "winget:Example.Tool").Status == "Unknown", "Partial registry inventory cannot establish uniqueness of a display-name match");
            facts.Registry = new InstalledSnapshot(new InstalledRecord[0], new Dictionary<string, string>(), false); facts.WingetComplete = false; facts.ChocoComplete = false; facts.Choco.Clear(); facts.Winget.Clear();
            Check(SetupAudit.Evaluate(target, facts, null).Rows.TrueForAll(x => x.Status == "Unknown"), "Incomplete app inventories cannot claim apps are absent");
            facts = Facts(); target = Target(); target.Recipe.Apps.Clear(); target.CheckDevices = true;
            facts.Devices.Add(new AuditDevice { Name = "Network controller", Id = "PCI fixture", Code = 28 }); facts.RestartPending = true;
            report = SetupAudit.Evaluate(target, facts, null);
            Check(report.Rows.Exists(x => x.Category == "Devices" && x.Status == "Attention" && x.Detail.Contains("28")), "Present device problem codes remain actionable");
            Check(report.Rows.Exists(x => x.Key == "restart" && x.Status == "Attention"), "Pending restart is not hidden behind installer success");
            facts.Devices.Clear(); facts.DevicesComplete = false;
            Check(SetupAudit.Evaluate(target, facts, null).Rows.Exists(x => x.Key == "devices" && x.Status == "Unknown"), "Failed device inventory is not reported as healthy");
            var definitions = Debloat.Load(); var toggle = new List<TweakToggle>(definitions.AllToggles)[0];
            target.CheckDevices = false; target.Recipe.Tweaks.Add(toggle.FeatureId); facts.Features[toggle.FeatureId] = "Not applied";
            Check(SetupAudit.Evaluate(target, facts, definitions).Rows.Find(x => x.Key == toggle.FeatureId).Status == "Different", "Recipe desired setting is compared with actual detection, not UI selection");
            facts.Features[toggle.FeatureId] = "Unknown";
            Check(SetupAudit.Evaluate(target, facts, definitions).Rows.Find(x => x.Key == toggle.FeatureId).Status == "Unknown", "Unverifiable tweak effects remain unknown");
            var removal = definitions.Apps.Find(x => x.RemovalMethod == "Appx"); target.Recipe.Remove.Add(removal.AppId); facts.Appx.Add(removal.AppIds[removal.AppIds.Count - 1]);
            Check(SetupAudit.Evaluate(target, facts, definitions).Rows.Find(x => x.Key == "remove:" + removal.AppId).Status == "Different", "Removal comparison detects exact reviewed Appx aliases");
            facts.Appx.Clear(); facts.AppxComplete = false;
            Check(SetupAudit.Evaluate(target, facts, definitions).Rows.Find(x => x.Key == "remove:" + removal.AppId).Status == "Unknown", "Incomplete removal inventory is not a successful removal");
            var aliasedRemoval = definitions.Apps.Find(x => x.RemovalMethod == "Appx" && x.AppIds.Count > 1);
            if (aliasedRemoval != null)
            {
                string alias = aliasedRemoval.AppIds[aliasedRemoval.AppIds.Count - 1].ToUpperInvariant();
                target.Recipe.Remove.Clear(); target.Recipe.Remove.Add(alias); facts.Appx.Add(alias);
                Check(SetupAudit.Evaluate(target, facts, definitions).Rows.Find(x => x.Key == "remove:" + alias).Status == "Different", "Recipe removal aliases retain case-insensitive exact package matching");
            }
            target.Recipe.HasWindowsSelections = false;
            Check(SetupAudit.Evaluate(target, facts, definitions).Rows.Count == 0, "Winget exports do not introduce Windows or backup comparison targets");
            var job = new JobSpec { Action = "Install", Apps = "tool", ListText = Target().Recipe.ListText, SkipDrivers = true, DebloatArgs = new List<string> { "-" + toggle.FeatureId, "-RemoveApps", "-Apps", removal.AppId, "-CreateRestorePoint" } };
            var frozen = AuditTarget.FromJob(job);
            job.Apps = "github:owner/tool";
            Check(frozen.Recipe.Apps.Count == 1 && frozen.Recipe.Apps[0] == "tool" && frozen.Recipe.Tweaks.Contains(toggle.FeatureId) && frozen.Recipe.Remove.Contains(removal.AppId), "Job verification captures the executed selection and parses actual tweak/removal switches");
            Check(!frozen.CheckDevices && !frozen.Recipe.Tweaks.Contains("CreateRestorePoint"), "Non-state setup switches are not mistaken for desired Windows settings");
            var original = new JobSpec { Action = "Install", Title = "Setup", Apps = "tool,winget:Example.Tool", ListText = Target().Recipe.ListText };
            var record = SetupProgress.Begin(original, null);
            original.Apps = "tool";
            var retry = new JobSpec { Action = "Install", Title = "Retrying setup", Apps = "tool", ListText = original.ListText };
            var retried = SetupProgress.Begin(retry, record);
            Check(retried.VerificationJob.Apps == "tool,winget:Example.Tool", "Retry verification retains an immutable original app target");
            var recovered = Engine.Json.Deserialize<SetupRecord>(Engine.Json.Serialize(retried));
            Check(recovered.VerificationJob.Apps == "tool,winget:Example.Tool", "Original verification target survives restart snapshot serialization");
            Check(SetupProgress.VerifyAfterExit(new JobSpec { Action = "Install", TrackAsInstall = true }, 1, false, false), "Terminal setup failure before engine report still requests post-setup verification");
            Check(!SetupProgress.VerifyAfterExit(new JobSpec { Action = "Install", TrackAsInstall = true }, 3010, false, false) && !SetupProgress.VerifyAfterExit(new JobSpec { Action = "Install", TrackAsInstall = true }, Proc.UacDeclined, false, false), "Awaiting restart and declined elevation must not trigger completion verification");
            Check(!SetupProgress.VerifyAfterExit(new JobSpec { Action = "Install", TrackAsInstall = true }, 0, false, true) && !SetupProgress.VerifyAfterExit(new JobSpec { Action = "Install", TrackAsInstall = true }, 0, true, false), "Report-owned completion and active engine runs avoid duplicate or premature verification");
            var truncated = AuditScan.ParseWinget(0, "Name        Id                Version\n-------------------------------------\nExample     Example.Tool…     1.0\n");
            Check(!truncated.Complete && truncated.Versions.Count == 0, "Truncated winget identities cannot establish absence or package presence");
            Check(!AuditScan.ParseWinget(-1, "").Complete && !AuditScan.ParseWinget(0, "localized or unexpected output").Complete, "Timeout and unrecognized inventory output remain incomplete");
            string folder = Path.Combine(Path.GetTempPath(), "Freshkit-audit-test-" + Guid.NewGuid().ToString("N"));
            string previous = Engine.SessionList;
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "Recipe")); File.WriteAllText(Path.Combine(folder, "Recipe", "settings.json"), "fixture");
                var own = new SetupRecipe { ListText = Target().Recipe.ListText + "::BEGIN_CONFIG\n::CONFIG git|Recipe Git|APPDATA|Recipe/settings.json\n::END_CONFIG\n", Backups = new List<string> { "git" } };
                Engine.SessionList = Target().Recipe.ListText + "::BEGIN_CONFIG\n::CONFIG git|Session Git|APPDATA|Missing/settings.json\n::END_CONFIG\n";
                var availability = AuditScan.CheckBackups(own, new Dictionary<string, string> { { "APPDATA", folder } }, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                Check(availability["git"].Status == "Settings found" && Engine.SessionList.Contains("Session Git"), "Backup comparison uses the recipe's rules without changing the active session catalog");
                var unavailable = AuditScan.CheckBackups(own, new Dictionary<string, string> { { "APPDATA", Path.Combine(folder, "Missing") } }, new HashSet<string>());
                Check(unavailable["git"].Status == "Nothing to back up", "Recipe profiles with absent locations report no available settings");
            }
            finally { Engine.SessionList = previous; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            Console.WriteLine("PASS " + checks + " audit checks");
        }
    }
}
