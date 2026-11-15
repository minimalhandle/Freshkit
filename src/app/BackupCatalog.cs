using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Freshkit
{
    internal sealed class BackupRule { public string Root, Path; }
    internal sealed class BackupProfile
    {
        public string Id, Name;
        public List<BackupRule> Rules = new List<BackupRule>();
        public List<string> Processes = new List<string>(), Exclude = new List<string>();
    }
    internal sealed class BackupAvailability
    {
        public string Status, Error = "";
        public long Bytes;
        public int Files, RegistryValues;
        public string Detail
        {
            get { return Error.Length > 0 ? Error : Files + " file" + (Files == 1 ? "" : "s") + ", about " +
                (Bytes < 1024 ? Bytes + " B" : Bytes < 1048576 ? (Bytes / 1024.0).ToString("0.#") + " KB" : (Bytes / 1048576.0).ToString("0.#") + " MB") +
                (RegistryValues > 0 ? " + " + RegistryValues + " registry values" : ""); }
        }
    }
    internal static class BackupCatalog
    {
        public static List<BackupProfile> Load(string session)
        {
            var profiles = new List<BackupProfile>();
            foreach (object p in J.A(Engine.Json.DeserializeObject(Engine.ReadResourceText("payload/Settings.Profiles.json")), "profiles"))
            {
                var item = new BackupProfile { Id = J.S(p, "id"), Name = J.S(p, "name") };
                foreach (object r in J.A(p, "rules")) item.Rules.Add(new BackupRule { Root = J.S(r, "root"), Path = J.S(r, "path") });
                foreach (object s in J.A(p, "processes")) item.Processes.Add(Convert.ToString(s));
                foreach (object s in J.A(p, "exclude")) item.Exclude.Add(Convert.ToString(s));
                profiles.Add(item);
            }
            foreach (ProfileEntry p in ListFile.Parse(session).Profiles)
            {
                BackupProfile item = profiles.Find(x => x.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase));
                if (item == null) { item = new BackupProfile { Id = p.Id, Exclude = new List<string> { "*.log", "*.tmp", "cache", "GPUCache", "Cookies*", "Login Data*", "hosts.yml" } }; profiles.Add(item); }
                item.Name = p.Name; item.Rules.Clear();
                var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string location in p.Locations)
                {
                    int colon = location.IndexOf(':');
                    string root = location.Substring(0, colon), path = location.Substring(colon + 1);
                    if (!unique.Add(location)) throw new InvalidDataException("Duplicate settings rule: " + location);
                    if (root != "APPDATA" && root != "LOCALAPPDATA" && root != "USERPROFILE" && root != "DOCUMENTS" && root != "HKCU") throw new InvalidDataException("Unsupported settings root: " + root);
                    if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || path.Contains(":") || Regex.IsMatch(path, @"(^|[\\/])\.\.?([\\/]|$)|[\x00-\x1f]")) throw new InvalidDataException("Invalid relative settings path: " + path);
                    if (root == "HKCU" && (!Regex.IsMatch(path, @"^Software\\[^\\]+", RegexOptions.IgnoreCase) || Regex.IsMatch(path, @"^Software\\(Classes|Policies|Microsoft\\Windows)(\\|$)", RegexOptions.IgnoreCase))) throw new InvalidDataException("Registry rules must target app-specific HKCU Software keys.");
                    item.Rules.Add(new BackupRule { Root = root, Path = path });
                }
            }
            // Explicit opt-outs; absence from a legacy/session list never disables built-ins.
            foreach (Match m in Regex.Matches(session, @"(?m)^::NOCONFIG\s+([^|\r\n]+)\|")) profiles.RemoveAll(p => p.Id.Equals(m.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase));
            foreach (Match m in Regex.Matches(session, @"(?m)^::CONFIG\s+([^|\r\n]+)\|[^\r\n]*$"))
            {
                string[] fields = m.Value.Split('|');
                var p = profiles.Find(x => x.Id.Equals(m.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase));
                if (p != null && fields.Length == 5) foreach (string process in fields[4].Split(','))
                    if (process.Trim().Length > 0)
                    {
                        string name = Regex.Replace(process.Trim(), @"\.exe$", "", RegexOptions.IgnoreCase);
                        if (!Regex.IsMatch(name, @"\A[A-Za-z0-9._-]+\z")) throw new InvalidDataException("Invalid process name: " + name);
                        p.Processes.Add(name);
                    }
            }
            return profiles;
        }

        public static Dictionary<string, string> Roots()
        {
            return new Dictionary<string, string> {
                { "APPDATA", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) },
                { "LOCALAPPDATA", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) },
                { "USERPROFILE", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
                { "DOCUMENTS", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) } };
        }
        public static HashSet<string> RunningProcesses()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process p in Process.GetProcesses()) using (p) { try { names.Add(p.ProcessName); } catch { } }
            return names;
        }
        static bool Excluded(BackupProfile p, string relative)
        {
            foreach (string pattern in p.Exclude)
            {
                string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (Regex.IsMatch(Path.GetFileName(relative), regex, RegexOptions.IgnoreCase) || Regex.IsMatch(relative.Replace('\\', '/'), regex, RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }
        static void PlainPath(string path)
        {
            for (string node = Path.GetFullPath(path); !string.IsNullOrEmpty(node); node = Path.GetDirectoryName(node))
                if ((File.Exists(node) || Directory.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A settings path contains a link. It will not be followed.");
        }
        public static BackupAvailability Discover(BackupProfile profile, Dictionary<string, string> roots, HashSet<string> running)
        {
            var result = new BackupAvailability();
            bool appRunning = profile.Processes.Exists(p => running.Contains(p));
            try
            {
                foreach (BackupRule rule in profile.Rules)
                {
                    if (rule.Root == "HKCU")
                    {
                        if (!Regex.IsMatch(rule.Path, @"^Software\\[^\\]+", RegexOptions.IgnoreCase) || Regex.IsMatch(rule.Path, @"^Software\\(Classes|Policies|Microsoft\\Windows)(\\|$)", RegexOptions.IgnoreCase)) throw new IOException("Invalid registry settings path.");
                        ScanRegistry(rule.Path, result); continue;
                    }
                    string root;
                    if (!roots.TryGetValue(rule.Root, out root) || string.IsNullOrEmpty(root)) throw new IOException("Settings root is unavailable: " + rule.Root);
                    string basePath = Path.GetFullPath(root).TrimEnd('\\') + "\\";
                    string target = Path.GetFullPath(Path.Combine(basePath, rule.Path));
                    if (Path.IsPathRooted(rule.Path) || !target.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Settings path escapes its root.");
                    PlainPath(target);
                    if (File.Exists(target)) { if (!Excluded(profile, Path.GetFileName(target))) { result.Files++; result.Bytes += new FileInfo(target).Length; } continue; }
                    if (!Directory.Exists(target)) continue;
                    var pending = new Stack<string>(); pending.Push(target);
                    while (pending.Count > 0) foreach (string child in Directory.GetFileSystemEntries(pending.Pop()))
                    {
                        PlainPath(child);
                        string relative = child.Substring(target.TrimEnd('\\').Length + 1);
                        if (Excluded(profile, relative)) continue;
                        if (Directory.Exists(child)) pending.Push(child);
                        else { result.Files++; result.Bytes += new FileInfo(child).Length; }
                    }
                }
                result.Status = appRunning ? "App running" : result.Files + result.RegistryValues > 0 ? "Settings found" : "Nothing to back up";
                if (appRunning) result.Error = "Close the app, including its tray icon, before backup or restore.";
            }
            catch (Exception ex) { result.Status = "Unable to check"; result.Error = ex.Message; }
            return result;
        }
        static void ScanRegistry(string path, BackupAvailability result)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
            {
                if (key == null) return;
                foreach (string name in key.GetValueNames()) if (!Regex.IsMatch(name, "password|token|secret|credential", RegexOptions.IgnoreCase)) result.RegistryValues++;
                foreach (string child in key.GetSubKeyNames()) ScanRegistry(path + "\\" + child, result);
            }
        }
    }
}
