// Freshkit - Prepare for reinstall / Restore from reinstall folder (Profile ⋯ menu).
// The folder holds a recipe, a settings backup and Steam libraries. Nothing here is read without the user choosing the folder.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Controls;

namespace Freshkit
{
    internal sealed partial class MainUi
    {
        List<string> pendingSteamLibraries; // session-only: from the reinstall folder the user chose
        const string DailyBackupTask = "Freshkit daily settings backup";

        void InitReinstallAndDailyBackup()
        {
            Act("BtnPrepareReinstall", delegate { PrepareForReinstall(); });
            Act("BtnRestoreReinstall", delegate { RestoreFromReinstallFolder(); });
            var time = Get<ComboBox>("DailyBackupTime");
            foreach (string t in new[] { "00:00", "03:00", "06:00", "09:00", "12:00", "15:00", "18:00", "21:00" }) time.Items.Add(t);
            time.SelectedIndex = 1;
            var keep = Get<ComboBox>("DailyBackupKeep");
            foreach (int n in new[] { 3, 7, 14, 30 }) keep.Items.Add(new ComboBoxItem { Content = "Keep " + n + " backups", Tag = n });
            keep.SelectedIndex = 1;
            Click("BtnDailyBackupBrowse", delegate
            {
                string folder = ChooseReinstallFolder("Choose a folder for daily settings backups. A second drive or a synced folder keeps them safe if Windows is reinstalled.");
                if (folder != null) Get<TextBox>("DailyBackupFolder").Text = folder;
            });
            Act("BtnDailyBackupOn", delegate
            {
                var box = Get<TextBox>("DailyBackupFolder");
                string folder = box.Text.Trim().Trim('"');
                if (folder.Length == 0 || !Directory.Exists(folder))
                {
                    folder = ChooseReinstallFolder("Choose a folder for daily settings backups. A second drive or a synced folder keeps them safe if Windows is reinstalled.");
                    if (folder == null) return;
                    box.Text = folder;
                }
                var ids = new List<string>();
                foreach (ProfileItem p in settingsProfiles) if (p.IsSelected) ids.Add(p.Id);
                string profiles = ids.Count == 0 || ids.Count == settingsProfiles.Count ? "ALL" : string.Join(",", ids.ToArray());
                string at = (string)time.SelectedItem, count = Convert.ToString(((ComboBoxItem)keep.SelectedItem).Tag);
                RunRunner("Turning on daily settings backup", "ScheduleBackup", Vals("ArchivePath", folder, "Profiles", profiles, "Time", at, "Keep", count), null, delegate { RefreshDailyBackup(); });
            });
            Act("BtnDailyBackupOff", delegate { RunRunner("Turning off daily settings backup", "UnscheduleBackup", null, null, delegate { RefreshDailyBackup(); }); });
            RefreshDailyBackup();
        }

        void RefreshDailyBackup()
        {
            new System.Threading.Thread(delegate ()
            {
                string output;
                string cmd = "$t=Get-ScheduledTask -TaskPath '\\' -TaskName '" + DailyBackupTask + "' -ErrorAction SilentlyContinue; if($t){$i=$t|Get-ScheduledTaskInfo -ErrorAction SilentlyContinue;'ON|'+$(if($i -and $i.NextRunTime){$i.NextRunTime.ToString('ddd d MMM HH:mm')}else{''})+'|'+$(if($i -and $i.LastRunTime -and $i.LastRunTime.Year -gt 2000){$i.LastRunTime.ToString('ddd d MMM HH:mm')+'|'+$i.LastTaskResult}else{'|'})+'|'+@($t.Actions)[0].Arguments}else{'OFF'}";
                try { Proc.Capture(Engine.PowerShellPath, "-NoProfile -NonInteractive -Command \"" + cmd.Replace("\"", "\\\"") + "\"", out output, 30000); }
                catch { output = ""; }
                Ui(delegate
                {
                    string line = "";
                    foreach (string l in output.Split('\n')) if (l.StartsWith("ON|") || l.StartsWith("OFF")) line = l.Trim();
                    var status = Get<TextBlock>("DailyBackupStatus");
                    if (!line.StartsWith("ON|")) { status.Text = "Off. Turn it on to save a settings backup every day without opening Freshkit. Apps that are open at that time are skipped."; return; }
                    string[] p = line.Split(new[] { '|' }, 5);
                    var folder = System.Text.RegularExpressions.Regex.Match(p.Length > 4 ? p[4] : "", "-ArchivePath \"([^\"]+)\"");
                    var keepMatch = System.Text.RegularExpressions.Regex.Match(p.Length > 4 ? p[4] : "", @"-Keep (\d+)");
                    if (folder.Success && Get<TextBox>("DailyBackupFolder").Text.Trim().Length == 0) Get<TextBox>("DailyBackupFolder").Text = folder.Groups[1].Value;
                    string last = p.Length > 3 && p[2].Length > 0 ? " Last run " + p[2] + (p[3] == "0" ? " (OK)." : " (didn't finish; see %LOCALAPPDATA%\\Freshkit\\logs).") : "";
                    status.Text = "On" + (folder.Success ? ": " + folder.Groups[1].Value : "") + (keepMatch.Success ? ", keeping " + keepMatch.Groups[1].Value : "") + "." + (p[1].Length > 0 ? " Next run " + p[1] + "." : "") + last + " Runs while you're signed in and catches up if the PC was off.";
                });
            }) { IsBackground = true }.Start();
        }

        string ChooseReinstallFolder(string description)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = description, ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(new WindowHandle(new System.Windows.Interop.WindowInteropHelper(Window).Handle)) != System.Windows.Forms.DialogResult.OK) return null;
                return dialog.SelectedPath;
            }
        }

        void PrepareForReinstall()
        {
            if (running) return;
            string folder = ChooseReinstallFolder("Choose a folder for your reinstall kit. Use a drive that won't be wiped, such as a second drive or a USB stick.");
            if (folder == null) return;
            Action go = delegate { WriteReinstallFolder(folder); };
            if (File.Exists(Path.Combine(folder, ReinstallManifest.FileName)))
            {
                Action next = go;
                go = delegate { Confirm("Replace the reinstall kit in this folder?", "This folder already has a Freshkit reinstall kit. Its recipe and manifest are replaced; older settings backups stay in the folder.", "Replace", next); };
            }
            if (ReinstallManifest.OnWindowsDrive(folder))
                Confirm("This folder is on the Windows drive", "Reinstalling Windows normally wipes this drive, and the kit with it. Choose a folder on another drive, or copy the folder somewhere safe before you reinstall.", "Use it anyway", go);
            else go();
        }

        void WriteReinstallFolder(string folder)
        {
            if (running) return;
            var manifest = new ReinstallManifest { CreatedUtc = DateTime.UtcNow.ToString("o") };
            try
            {
                File.WriteAllText(Path.Combine(folder, manifest.Recipe), CaptureRecipe().Serialize(), new UTF8Encoding(false));
                try { manifest.SteamLibraries = SteamLibraries.CollectPortable(); }
                catch (Exception ex) { Log("Steam libraries couldn't be read: " + ex.Message); }
                File.WriteAllText(Path.Combine(folder, ReinstallManifest.FileName), manifest.Serialize(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Info("Couldn't prepare the reinstall folder", ex.Message); return; }

            var ids = new List<string>();
            foreach (ProfileItem p in settingsProfiles) if (p.IsSelected) ids.Add(p.Id);
            string profiles = ids.Count == 0 || ids.Count == settingsProfiles.Count ? "ALL" : string.Join(",", ids.ToArray());
            string zipName = "settings-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";
            string zip = Path.Combine(folder, zipName);
            Log("Saved recipe and reinstall manifest to " + folder + ". No drivers are copied: setup installs current drivers instead.");
            RunRunner("Preparing for reinstall", "Backup", Vals("ArchivePath", zip, "Profiles", profiles), null, delegate (int rc)
            {
                string backupText;
                if (File.Exists(zip))
                {
                    manifest.SettingsBackup = zipName;
                    try { File.WriteAllText(Path.Combine(folder, ReinstallManifest.FileName), manifest.Serialize(), new UTF8Encoding(false)); }
                    catch (Exception ex) { Info("Couldn't update the reinstall manifest", ex.Message); return; }
                    backupText = "App settings: " + zipName + (rc == 0 ? "" : " (some apps were skipped; see the activity log)") + ".";
                }
                else backupText = "App settings weren't backed up (nothing found, or every app was running). See the activity log; you can run Prepare for reinstall again after closing apps.";
                Get<TextBox>("ArchiveBox").Text = File.Exists(zip) ? zip : Get<TextBox>("ArchiveBox").Text;
                string steamText = manifest.SteamLibraries.Count == 0 ? "No Steam libraries outside the Windows drive." : "Steam libraries to re-add: " + string.Join(", ", manifest.SteamLibraries.ToArray()) + ".";
                Info("Reinstall folder ready", "Saved to " + folder + ".\n\nRecipe: " + manifest.Recipe + " (apps, Windows tweaks, removals and backup choices).\n" + backupText + "\n" + steamText +
                    "\n\nAfter reinstalling Windows, open Freshkit and choose Profile ⋯ → Restore from reinstall folder…");
            });
        }

        void RestoreFromReinstallFolder()
        {
            if (running) return;
            string folder = ChooseReinstallFolder("Choose the folder you saved with Prepare for reinstall.");
            if (folder == null) return;
            try
            {
                string manifestPath = Path.Combine(folder, ReinstallManifest.FileName);
                if (!File.Exists(manifestPath)) throw new InvalidDataException("This folder has no " + ReinstallManifest.FileName + ". Choose the folder made by Prepare for reinstall.");
                if (new FileInfo(manifestPath).Length > 1024 * 1024) throw new InvalidDataException("The reinstall manifest exceeds 1 MB.");
                var manifest = ReinstallManifest.Parse(File.ReadAllText(manifestPath));
                string recipePath = Path.Combine(folder, manifest.Recipe);
                if (!File.Exists(recipePath)) throw new InvalidDataException("The recipe " + manifest.Recipe + " is missing from this folder.");
                if (new FileInfo(recipePath).Length > 4 * 1024 * 1024) throw new InvalidDataException("The recipe exceeds 4 MB.");
                bool custom;
                var recipe = SetupRecipe.Parse(File.ReadAllText(recipePath), Engine.CurrentListText(out custom));
                string zip = manifest.SettingsBackup.Length > 0 ? Path.Combine(folder, manifest.SettingsBackup) : null;
                var extra = new StringBuilder();
                extra.AppendLine("Reinstall folder: " + folder);
                if (zip != null && File.Exists(zip)) extra.AppendLine("Settings restored after setup: " + manifest.SettingsBackup);
                else { extra.AppendLine(zip == null ? "No settings backup in this kit." : "Settings backup " + manifest.SettingsBackup + " is missing, so settings won't be restored."); zip = null; }
                extra.Append(manifest.SteamLibraries.Count == 0 ? "No Steam libraries to re-add." : "Steam libraries re-added after setup: " + string.Join(", ", manifest.SteamLibraries.ToArray()));
                var libraries = manifest.SteamLibraries;
                PreviewRecipeWith(recipe, "Restore from reinstall folder?", extra.ToString(), delegate
                {
                    if (zip != null) Get<TextBox>("ArchiveBox").Text = zip;
                    pendingSteamLibraries = libraries.Count > 0 ? new List<string>(libraries) : null;
                    Log("Reinstall folder loaded. Review setup installs the apps" + (zip != null ? ", restores your settings" : "") + (libraries.Count > 0 ? " and re-adds your Steam libraries" : "") + ".");
                    Navigate("NavApps");
                    OpenInstallPlan();
                });
            }
            catch (Exception ex) { Info("Couldn't open the reinstall folder", ex.Message); }
        }

        void ApplyPendingSteamLibraries(SetupRecord record)
        {
            var job = record.Recipe;
            if (job == null || job.SteamLibraries == null || job.SteamLibraries.Count == 0) return;
            var libraries = job.SteamLibraries;
            job.SteamLibraries = null;
            if (record.VerificationJob != null) record.VerificationJob.SteamLibraries = null;
            if (File.Exists(SetupProgress.PathName)) { try { SetupProgress.Save(record); } catch { } }
            pendingSteamLibraries = null;
            string message = SteamLibraries.ApplyToInstalledSteam(libraries, delegate (string name) { var found = Process.GetProcessesByName(name); bool any = found.Length > 0; foreach (var p in found) p.Dispose(); return any; });
            if (message != null) Log(message);
        }
    }
}
