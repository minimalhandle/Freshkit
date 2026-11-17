using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
namespace Freshkit
{
    internal static class PreflightTests
    {
        static int checks;
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        static PreflightSnapshot Healthy()
        {
            return new PreflightSnapshot { SystemFreeBytes = 50L * 1073741824, TempFreeBytes = 50L * 1073741824, RestartPending = false, WingetAvailable = true, RunningProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
        }
        static void Main()
        {
            var snapshot = Healthy(); var request = new PreflightRequest();
            Check(!Preflight.Evaluate(snapshot, request).Exists(x => x.Status != "Ready"), "Healthy local-only setup has no unrelated source warnings");
            snapshot.SystemFreeBytes = 400L * 1048576;
            Check(Preflight.Evaluate(snapshot, request).Exists(x => x.Title == "Free space" && x.Status == "Warning"), "Low system space offers a warning without pretending to know package sizes");
            snapshot = Healthy(); snapshot.TempFreeBytes = null; snapshot.RestartPending = null;
            Check(Preflight.Evaluate(snapshot, request).Find(x => x.Title == "Free space").Status == "Unknown", "Unreadable temp space is not a pass");
            Check(Preflight.Evaluate(snapshot, request).Find(x => x.Title == "Pending restart").Status == "Unknown", "Unknown restart status is explicit");
            snapshot = Healthy(); snapshot.RestartPending = true;
            Check(Preflight.Evaluate(snapshot, request).Find(x => x.Title == "Pending restart").Status == "Warning", "Pending restart offers restart guidance");
            request.Sources.Add("winget"); request.NeedsWinget = true; snapshot.WingetAvailable = false;
            snapshot.Sources["winget"] = false;
            var result = Preflight.Evaluate(snapshot, request);
            Check(result.Exists(x => x.Title == "winget" && x.Status == "Warning") && result.Exists(x => x.Title == "winget source" && x.Status == "Warning"), "Missing winget and unreachable required source remain visible");
            request.Sources.Clear(); request.NeedsWinget = false;
            Check(!Preflight.Evaluate(snapshot, request).Exists(x => x.Title.Contains("winget")), "Deselected sources no longer warn");
            request.RestoreProfiles.Add(new BackupProfile { Id = "obs", Name = "OBS Studio", Processes = new List<string> { "obs64" } });
            snapshot.RunningProcesses.Add("OBS64");
            Check(Preflight.Evaluate(snapshot, request).Exists(x => x.Title == "Settings restore" && x.Status == "Warning" && x.Detail.Contains("OBS Studio")), "Running restore apps are named using case-insensitive process matching");
            request.RestoreProfiles.Clear();
            Check(!Preflight.Evaluate(snapshot, request).Exists(x => x.Title == "Settings restore"), "No running-app warning when restore is disabled");
            snapshot = Healthy(); request.Sources.Add("GitHub");
            Check(Preflight.Evaluate(snapshot, request).Find(x => x.Title == "GitHub source").Status == "Unknown", "Missing source result cannot appear ready");
            string path = Path.Combine(Path.GetTempPath(), "Freshkit-preflight-test-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (var stream = File.Create(path))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                    writer.Write("{\"SchemaVersion\":1,\"Apps\":[{\"Id\":\"obs\",\"Items\":0},{\"Id\":\"git\",\"Items\":1}],\"Files\":[],\"Registry\":[]}");
                snapshot = Healthy(); snapshot.RunningProcesses.Add("obs64");
                request = new PreflightRequest { RestoreZip = path };
                request.RestoreProfiles.Add(new BackupProfile { Id = "obs", Name = "OBS Studio", Processes = new List<string> { "obs64" } });
                request.RestoreProfiles.Add(new BackupProfile { Id = "git", Name = "Git", Processes = new List<string> { "git" } });
                typeof(Preflight).GetMethod("ReadRestoreIds", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { snapshot, path });
                Check(Preflight.Evaluate(snapshot, request).Find(x => x.Title == "Settings restore").Status == "Ready", "Running apps with zero backed-up items do not need closing");
                snapshot.RunningProcesses.Add("git");
                Check(Preflight.Evaluate(snapshot, request).Find(x => x.Title == "Settings restore").Detail.Contains("Git"), "Running app with populated backup remains visible");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            Console.WriteLine("PASS " + checks + " preflight checks");
        }
    }
}
