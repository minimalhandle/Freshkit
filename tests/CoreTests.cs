using System;
using System.Collections.Generic;
using System.IO;
namespace Freshkit
{
    internal static class CoreTests
    {
        static int count;
        static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
        public static int Main()
        {
            try
            {
                WorkflowTests.Identity(Check);
                SystemFactsTests.Run(Check);
                SignalsTests.Run(Check);
                ReinstallTests.Run(Check);
                WorkflowTests.Updates(Check);
                WorkflowTests.Search(Check);
                WorkflowTests.Recipes(Check);
                bool custom;
                string defaults = Engine.CurrentListText(out custom);
                Check(!custom, "Startup uses embedded defaults, ignoring companion files");
                Engine.SessionList = defaults.Replace("::PACKAGE spotify|", "::DISABLED spotify|");
                Check(Engine.CurrentListText(out custom).Contains("::DISABLED spotify|") && custom, "Session changes are used without saving a file");
                Engine.SessionList = null;
                Check(Engine.CurrentListText(out custom) == defaults, "Reset restores built-in defaults");
                var catalog = BackupCatalog.Load("::BEGIN_PACKAGES\n::PACKAGE steam|Steam|\n::END_PACKAGES\n::BEGIN_CONFIG\n::CONFIG mpv|mpv|APPDATA|custom-mpv\n::END_CONFIG\n");
                Check(catalog.Exists(p => p.Id == "afterclip") && catalog.Exists(p => p.Id == "lghub"), "Incomplete session config retains built-in backup profiles");
                Check(catalog.Find(p => p.Id == "mpv").Rules[0].Path == "custom-mpv", "Explicit session rules override a named profile");
                Check(!BackupCatalog.Load(defaults.Replace("::END_CONFIG", "::NOCONFIG mpv|mpv|APPDATA|mpv\n::END_CONFIG")).Exists(p => p.Id == "mpv"), "Explicit backup opt-out is respected");
                bool invalid = false;
                try { BackupCatalog.Load(defaults.Replace("::CONFIG mpv|mpv|APPDATA|mpv", "::CONFIG mpv|mpv|APPDATA|..\\escape")); } catch (InvalidDataException) { invalid = true; }
                Check(invalid, "Invalid settings paths are rejected before discovery");
                string root = Path.Combine(Path.GetTempPath(), "Freshkit-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, "mpv", "cache"));
                try
                {
                    File.WriteAllText(Path.Combine(root, "mpv", "mpv.conf"), "abcd");
                    File.WriteAllText(Path.Combine(root, "mpv", "cache", "cached.bin"), "ignored");
                    File.WriteAllText(Path.Combine(root, "mpv", "debug.log"), "ignored");
                    var profile = BackupCatalog.Load(defaults).Find(p => p.Id == "mpv");
                    var roots = new Dictionary<string, string> { { "APPDATA", root } };
                    var found = BackupCatalog.Discover(profile, roots, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    Check(found.Status == "Settings found" && found.Files == 1 && found.Bytes == 4, "Discovery counts included settings and excludes caches/logs");
                    found = BackupCatalog.Discover(profile, roots, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mpv" });
                    Check(found.Status == "App running", "Discovery identifies running apps");
                    File.Delete(Path.Combine(root, "mpv", "mpv.conf"));
                    found = BackupCatalog.Discover(profile, roots, new HashSet<string>());
                    Check(found.Status == "Nothing to back up", "Excluded files alone do not count as settings");
                }
                finally { Directory.Delete(root, true); }
                var stages = SetupProgress.Create(new JobSpec { Action = "Install", Apps = "steam", TrackAsInstall = true });
                Check(stages.Count == 7 && stages.Find(s => s.Id == "Apps").Status == "Pending", "Checklist includes the complete setup sequence");
                Check(stages.Find(s => s.Id == "OfflineDrivers").Status == "Skipped", "Unselected stages are explicit skips");
                var failed = stages.Find(s => s.Id == "Tweaks"); failed.Status = "Failed";
                var recipe = new JobSpec { Action = "Install", Apps = "steam", DebloatArgs = new List<string> { "-DisableTelemetry" } };
                var retry = SetupProgress.Retry(recipe, stages, null);
                Check(retry.DebloatArgs != null && retry.Action == "Install", "Retry retains failed prerequisites and blocked remaining work");
                stages.Find(s => s.Id == "Bootstrap").Status = "Done";
                stages.Find(s => s.Id == "Drivers").Status = "Done";
                stages.Find(s => s.Id == "Apps").Status = "Done";
                retry = SetupProgress.Retry(recipe, stages, null);
                Check(retry.Action == null && retry.DebloatArgs != null, "Retry avoids repeating successful app and driver stages");

                // Reopen-after-restart: an Explorer restart runs and deletes RunOnce entries mid-install.
                string reopen = "\"C:\\Kit\\Freshkit.exe\" --progress", stored = null; int writes = 0;
                Func<string> read = delegate { return stored; };
                Action<string> write = delegate (string v) { stored = v; writes++; };
                Check(Engine.RearmReopen(reopen, read, write) && stored == reopen, "A used-up reopen entry is put back during an install");
                Check(!Engine.RearmReopen(reopen, read, write) && writes == 1, "An intact reopen entry is left alone");
                stored = "\"E:\\Old\\Freshkit.exe\" --progress";
                Check(Engine.RearmReopen(reopen, read, write) && stored == reopen, "A reopen entry for another EXE location is replaced");
                stages.Find(s => s.Id == "Apps").Status = "Failed";
                recipe.Apps = "steam";
                var unrelated = new RunSnapshot { RunId = "old" };
                unrelated.Rows.Add(new RunRow { Package = "vlc", Kind = "Chocolatey", Outcome = "Failed" });
                retry = SetupProgress.Retry(recipe, stages, unrelated, "new");
                Check(retry.Apps == "steam", "Retry ignores an unrelated previous install report");
                retry = SetupProgress.Retry(recipe, stages, unrelated, "old");
                Check(retry.Apps != "vlc", "Retry cannot add apps outside the approved selection");
                var interrupted = new SetupRecord { Recipe = recipe, Steps = stages };
                stages.Find(s => s.Id == "Settings").Status = "Running";
                SetupProgress.InterruptRestore(interrupted);
                Check(stages.Find(s => s.Id == "Settings").Status == "Failed", "Interrupted restore becomes a targeted retry");
                stages.Find(s => s.Id == "Drivers").Status = "Failed";
                unrelated.Rows.Add(new RunRow { Package = "geforce-game-ready-driver", Kind = "Driver", Outcome = "Failed" });
                unrelated.Rows.Add(new RunRow { Package = "amd-ryzen-chipset", Kind = "Driver", Outcome = "Installed" });
                retry = SetupProgress.Retry(recipe, stages, unrelated, "old");
                Check(retry.DriverIds == "geforce-game-ready-driver", "Retry selects failed drivers without repeating successful driver packages");
                Console.WriteLine(count + " checks passed"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex.Message); return 1; }
        }
    }
}
