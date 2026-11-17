// Read-only readiness facts and selection-aware guidance. No installers or repairs.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Freshkit
{
    internal sealed class PreflightRequest
    {
        public readonly HashSet<string> Sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<BackupProfile> RestoreProfiles = new List<BackupProfile>();
        public bool NeedsWinget;
        public string RestoreZip;
        public string RestoreError;
    }

    internal sealed class PreflightSnapshot
    {
        public DateTime CheckedUtc = DateTime.UtcNow;
        public long? SystemFreeBytes, TempFreeBytes;
        public bool? RestartPending, WingetAvailable;
        public HashSet<string> RunningProcesses;
        public readonly Dictionary<string, bool?> Sources = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        public string RestoreZip, RestoreError;
        public HashSet<string> RestoreIds;
    }

    internal sealed class PreflightCheck
    {
        public string Title, Status, Detail;
    }

    internal static class Preflight
    {
        public static List<PreflightCheck> Evaluate(PreflightSnapshot facts, PreflightRequest request)
        {
            var result = new List<PreflightCheck>();
            bool low = (facts.SystemFreeBytes.HasValue && facts.SystemFreeBytes < 5L * 1073741824) || (facts.TempFreeBytes.HasValue && facts.TempFreeBytes < 2L * 1073741824);
            bool unknownSpace = !facts.SystemFreeBytes.HasValue || !facts.TempFreeBytes.HasValue;
            result.Add(new PreflightCheck { Title = "Free space", Status = low ? "Warning" : unknownSpace ? "Unknown" : "Ready",
                Detail = "System drive: " + Space(facts.SystemFreeBytes) + "; temporary-files drive: " + Space(facts.TempFreeBytes) + ". " +
                (low ? "Free space in Windows Storage settings before installing. " : unknownSpace ? "Could not read all drive information. Check available space before starting. " : "") +
                "Guideline: 5 GB system / 2 GB temporary space. Large apps may need much more; this is not an installation size estimate." });
            result.Add(new PreflightCheck { Title = "Pending restart", Status = facts.RestartPending == true ? "Warning" : facts.RestartPending == false ? "Ready" : "Unknown",
                Detail = facts.RestartPending == true ? "Windows reports pending servicing or file operations. Restart from the Start menu, reopen Freshkit and review setup again." : facts.RestartPending == false ? "No standard Windows servicing or file-rename restart signals found. Individual installers may still require a restart." : "Windows restart signals could not all be read. Check Windows Update before starting." });
            foreach (string source in request.Sources)
            {
                bool? available; facts.Sources.TryGetValue(source, out available);
                result.Add(new PreflightCheck { Title = source + " source", Status = available == true ? "Ready" : available == false ? "Warning" : "Unknown",
                    Detail = available == true ? "HTTPS endpoint reachable. Configured sources, individual packages and installer downloads are checked by the install engine." : "Could not verify the " + source + " HTTPS endpoint. Check your connection, proxy or firewall, then Recheck. An offline driver kit does not contain app installers." });
            }
            if (request.NeedsWinget) result.Add(new PreflightCheck { Title = "winget", Status = facts.WingetAvailable == true ? "Ready" : facts.WingetAvailable == false ? "Warning" : "Unknown",
                Detail = facts.WingetAvailable == true ? "The Windows Package Manager command responds." : "winget could not be started. Install or repair Microsoft's App Installer, then Recheck before installing winget or Store apps." });
            if (request.RestoreProfiles.Count > 0 || !string.IsNullOrEmpty(request.RestoreZip) || !string.IsNullOrEmpty(request.RestoreError))
            {
                var names = new List<string>();
                bool sameArchive = string.Equals(request.RestoreZip, facts.RestoreZip, StringComparison.OrdinalIgnoreCase);
                foreach (var profile in request.RestoreProfiles)
                    if ((string.IsNullOrEmpty(request.RestoreZip) || (sameArchive && facts.RestoreIds != null && facts.RestoreIds.Contains(profile.Id))) && facts.RunningProcesses != null && profile.Processes.Exists(p => facts.RunningProcesses.Contains(p))) names.Add(profile.Name);
                bool unknown = facts.RunningProcesses == null || !string.IsNullOrEmpty(request.RestoreError) || (!string.IsNullOrEmpty(request.RestoreZip) && (!sameArchive || facts.RestoreIds == null));
                result.Add(new PreflightCheck { Title = "Settings restore", Status = names.Count > 0 ? "Warning" : unknown ? "Unknown" : "Ready",
                    Detail = names.Count > 0 ? "Close these apps, including tray icons, then Recheck: " + string.Join(", ", names.ToArray()) + ". Running apps are skipped during restoration." : unknown ? "Could not fully check the backup's apps or running processes. " + (request.RestoreError ?? facts.RestoreError) + " Close apps before restoration; the restore engine validates the archive before writing settings." : "No known processes for the backup's apps are running. Apps launched during setup can still prevent restoration." });
            }
            return result;
        }

        static string Space(long? bytes) { return bytes.HasValue ? (bytes.Value / 1073741824d).ToString("0.0") + " GB free" : "Unknown"; }
        static long? FreeSpace(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(path)).AvailableFreeSpace; } catch { return null; }
        }
        internal static bool? PendingRestart()
        {
            bool unknown = false;
            foreach (string path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", @"SYSTEM\CurrentControlSet\Control\Session Manager" })
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (path.EndsWith("Session Manager"))
                        {
                            if (key == null) { unknown = true; continue; }
                            foreach (string valueName in new[] { "PendingFileRenameOperations", "PendingFileRenameOperations2" })
                            {
                                var values = key.GetValue(valueName) as string[];
                                if (values != null && Array.Exists(values, v => !string.IsNullOrEmpty(v))) return true;
                            }
                        }
                        else if (key != null) return true;
                    }
                }
                catch { unknown = true; }
            }
            return unknown ? (bool?)null : false;
        }

        static bool? Probe(string source)
        {
            string url;
            switch (source)
            {
                case "Chocolatey": url = "https://community.chocolatey.org/api/v2/"; break;
                case "winget": url = "https://cdn.winget.microsoft.com/cache/source.msix"; break;
                case "Store": url = "https://storeedgefd.dsx.mp.microsoft.com/v9.0/information"; break;
                case "GitHub": url = "https://github.com/"; break;
                default: return null;
            }
            HttpWebRequest request = null; IAsyncResult pending = null;
            try
            {
                request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "HEAD"; request.AllowAutoRedirect = false;
                request.Timeout = 5000; request.ReadWriteTimeout = 5000; request.UserAgent = "Freshkit preflight";
                pending = request.BeginGetResponse(null, null);
                if (!pending.AsyncWaitHandle.WaitOne(5000)) return false;
                using (var response = (HttpWebResponse)request.EndGetResponse(pending)) return (int)response.StatusCode >= 200 && (int)response.StatusCode < 400;
            }
            catch (WebException ex)
            {
                using (var response = ex.Response as HttpWebResponse)
                    if (response != null && response.StatusCode == HttpStatusCode.MethodNotAllowed) return null;
                return false;
            }
            catch { return null; }
            finally { if (request != null) request.Abort(); if (pending != null) pending.AsyncWaitHandle.Close(); }
        }

        static void ReadRestoreIds(PreflightSnapshot facts, string path)
        {
            facts.RestoreZip = path;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                using (var file = File.OpenRead(path))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    var entry = zip.GetEntry("manifest.json");
                    if (entry == null || entry.Length > 10485760) throw new InvalidDataException("Missing or oversized settings manifest.");
                    string json;
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        var text = new StringBuilder(); var buffer = new char[4096]; int count;
                        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) { if (text.Length + count > 10485760) throw new InvalidDataException("Settings manifest exceeds its limit."); text.Append(buffer, 0, count); }
                        json = text.ToString();
                    }
                    var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 10485760 };
                    object manifest = serializer.DeserializeObject(json);
                    if (J.I(manifest, "SchemaVersion", 0) != 1 || J.Get(manifest, "Apps") == null) throw new InvalidDataException("Unsupported settings manifest.");
                    facts.RestoreIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (object app in J.A(manifest, "Apps")) if (J.I(app, "Items", 0) > 0) facts.RestoreIds.Add(J.S(app, "Id"));
                }
            }
            catch (Exception ex) { facts.RestoreError = ex.Message; facts.RestoreIds = null; }
        }

        public static PreflightSnapshot Scan(PreflightRequest request)
        {
            var facts = new PreflightSnapshot();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var probes = new Dictionary<string, Task<bool?>>();
            foreach (string source in request.Sources) { string name = source; probes.Add(name, Task.Factory.StartNew(() => Probe(name))); }
            facts.SystemFreeBytes = FreeSpace(Environment.GetFolderPath(Environment.SpecialFolder.System));
            facts.TempFreeBytes = FreeSpace(Path.GetTempPath());
            facts.RestartPending = PendingRestart();
            try { facts.RunningProcesses = BackupCatalog.RunningProcesses(); } catch { }
            if (request.NeedsWinget)
            {
                try { string output, exe = Engine.WingetExe(); facts.WingetAvailable = exe != null && Proc.Capture(exe, "--version", out output, 5000) == 0; } catch { facts.WingetAvailable = null; }
            }
            ReadRestoreIds(facts, request.RestoreZip);
            foreach (var pair in probes) { try { facts.Sources[pair.Key] = pair.Value.Result; } catch { facts.Sources[pair.Key] = null; } }
            facts.CheckedUtc = DateTime.UtcNow;
            return facts;
        }
    }
}
