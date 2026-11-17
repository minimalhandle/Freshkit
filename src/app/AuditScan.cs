using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Freshkit
{
    internal sealed class AuditPackages
    {
        public bool Complete;
        public string Error;
        public readonly Dictionary<string, string> Versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
    internal static class AuditScan
    {
        internal static Dictionary<string, BackupAvailability> CheckBackups(SetupRecipe recipe, Dictionary<string, string> roots, HashSet<string> processes)
        {
            var result = new Dictionary<string, BackupAvailability>(StringComparer.OrdinalIgnoreCase);
            var selected = new HashSet<string>(recipe.Backups, StringComparer.OrdinalIgnoreCase);
            foreach (var profile in BackupCatalog.Load(recipe.ListText))
                if (selected.Contains(profile.Id)) result[profile.Id] = BackupCatalog.Discover(profile, roots, processes);
            return result;
        }

        internal static AuditPackages ParseWinget(int code, string output)
        {
            var result = new AuditPackages();
            if (code != 0 || !Regex.IsMatch(output, @"(?m)\bName\s+Id\s+Version\b")) { result.Error = "winget installed-app inventory is unavailable or its table format is unsupported."; return result; }
            result.Complete = true;
            foreach (var row in Sources.ParseTable(output))
            {
                string id, version;
                if (!row.TryGetValue("Id", out id) || string.IsNullOrWhiteSpace(id)) { result.Complete = false; continue; }
                if (id.Contains("…") || id.EndsWith("...", StringComparison.Ordinal)) { result.Complete = false; continue; }
                row.TryGetValue("Version", out version); result.Versions[id] = version ?? "Unknown";
            }
            if (!result.Complete) result.Error = "winget truncated some package identities. Unmatched apps remain unknown.";
            return result;
        }

        static AuditPackages Winget()
        {
            try
            {
                string executable = Engine.WingetExe(), output;
                if (executable == null) return new AuditPackages { Error = "winget is not available for installed-app detection." };
                int code = Proc.Capture(executable, "list --disable-interactivity", out output, 25000);
                return ParseWinget(code, output);
            }
            catch (Exception ex) { return new AuditPackages { Error = "winget inventory: " + ex.Message }; }
        }

        static AuditPackages Choco()
        {
            var result = new AuditPackages();
            try
            {
                if (!File.Exists(Engine.ChocoExe)) { result.Complete = true; return result; }
                string output;
                if (Proc.Capture(Engine.ChocoExe, "list -r", out output, 15000) != 0) throw new IOException("The local package list failed or timed out.");
                foreach (string line in output.Split('\n'))
                {
                    string[] fields = line.Trim().Split('|');
                    if (fields.Length == 2 && Regex.IsMatch(fields[0], @"\A[A-Za-z0-9][A-Za-z0-9.-]*\z")) result.Versions[fields[0]] = fields[1];
                    else if (line.Contains("|")) throw new IOException("The local package list format is unsupported.");
                }
                result.Complete = true;
            }
            catch (Exception ex) { result.Error = "Chocolatey inventory: " + ex.Message; }
            return result;
        }

        static object WindowsInventory(bool windows, bool devices)
        {
            if (!windows && !devices) return null;
            string work = null;
            try
            {
                work = Engine.ExtractWork();
                string args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Engine.Q(Path.Combine(work, "AuditScan.ps1"));
                if (windows) args += " -Windows -DebloatRoot " + Engine.Q(Engine.ExtractDebloat(work));
                if (devices) args += " -Devices";
                string output;
                if (Proc.Capture(Engine.PowerShellPath, args, out output, 45000) != 0) throw new IOException("The Windows/device inventory failed or timed out.");
                var json = new System.Web.Script.Serialization.JavaScriptSerializer();
                object result = json.DeserializeObject(output.Trim().TrimStart('\uFEFF'));
                if (J.I(result, "SchemaVersion", 0) != 1 || !(J.Get(result, "AppxComplete") is bool) || !(J.Get(result, "DevicesComplete") is bool)) throw new IOException("The Windows/device inventory returned incomplete data.");
                return result;
            }
            finally { Engine.Cleanup(work); }
        }

        public static AuditSnapshot Scan(AuditTarget target, Debloat definitions)
        {
            var facts = new AuditSnapshot();
            bool needsWinget = false, needsChoco = false;
            var selected = new HashSet<string>(target.Recipe.Apps, StringComparer.OrdinalIgnoreCase);
            foreach (var app in ListFile.Parse(target.Recipe.ListText).Packages)
                if (selected.Contains(Data.KeyFor(app.Source, app.Id))) { needsWinget |= app.IsWinget; needsChoco |= app.Source == "choco"; }
            bool windows = target.Recipe.HasWindowsSelections && target.Recipe.Tweaks.Count + target.Recipe.Remove.Count > 0;
            var removals = new HashSet<string>(target.Recipe.Remove, StringComparer.OrdinalIgnoreCase);
            if (windows && definitions != null) foreach (var removal in definitions.Apps)
                if (removal.RemovalMethod != "Appx" && (removals.Contains(removal.AppId) || removal.AppIds.Exists(id => removals.Contains(id)))) needsWinget = true;
            var winget = needsWinget ? Task.Factory.StartNew(() => Winget()) : null;
            var choco = needsChoco ? Task.Factory.StartNew(() => Choco()) : null;
            var system = Task.Factory.StartNew(() => WindowsInventory(windows, target.CheckDevices));
            try { facts.Registry = InstalledSnapshot.Capture(); }
            catch (Exception ex) { facts.Errors.Add("Installed-app registry inventory: " + ex.Message); }
            if (winget != null) { var inventory = winget.Result; facts.WingetComplete = inventory.Complete; foreach (var pair in inventory.Versions) facts.Winget[pair.Key] = pair.Value; if (inventory.Error != null) facts.Errors.Add(inventory.Error); }
            if (choco != null) { var inventory = choco.Result; facts.ChocoComplete = inventory.Complete; foreach (var pair in inventory.Versions) facts.Choco[pair.Key] = pair.Value; if (inventory.Error != null) facts.Errors.Add(inventory.Error); }
            try
            {
                object data = system.Result;
                if (data != null)
                {
                    var features = J.Get(data, "Features") as Dictionary<string, object>;
                    if (features != null) foreach (var pair in features) if (pair.Value is string) facts.Features[pair.Key] = (string)pair.Value;
                    foreach (object name in J.A(data, "Appx")) facts.Appx.Add(Convert.ToString(name));
                    facts.AppxComplete = J.B(data, "AppxComplete"); facts.DevicesComplete = J.B(data, "DevicesComplete");
                    foreach (object device in J.A(data, "Devices")) if (J.I(device, "Code", 0) > 0) facts.Devices.Add(new AuditDevice { Name = J.S(device, "Name"), Id = J.S(device, "Id"), Code = J.I(device, "Code", 0) });
                    foreach (object error in J.A(data, "Errors")) facts.Errors.Add(Convert.ToString(error));
                }
            }
            catch (Exception ex) { facts.Errors.Add("Windows/device inventory: " + ex.GetBaseException().Message); }
            if (target.CheckDevices || target.Stages != null) facts.RestartPending = Preflight.PendingRestart();
            if (target.Recipe.HasWindowsSelections && target.Recipe.Backups.Count > 0)
            {
                try
                {
                    var roots = BackupCatalog.Roots(); var processes = BackupCatalog.RunningProcesses();
                    foreach (var pair in CheckBackups(target.Recipe, roots, processes)) facts.Backups[pair.Key] = pair.Value;
                }
                catch (Exception ex) { facts.Errors.Add("Recipe backup settings: " + ex.Message); }
            }
            facts.CheckedUtc = DateTime.UtcNow;
            return facts;
        }
    }
}
