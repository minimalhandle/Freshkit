using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Freshkit
{
    internal sealed class InstalledRecord
    {
        public string RegistryPath, Name, Version, Publisher, InstallLocation, UninstallString, Repository, PackageId;
        public string Fingerprint
        {
            get
            {
                using (var hash = SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", new[] { RegistryPath, Name, Publisher, InstallLocation, UninstallString })))).Replace("-", "");
            }
        }
    }

    internal sealed class InstalledMatch
    {
        public InstalledRecord Record;
        public bool Confirmed;
        public string Reason;
    }

    // One registry snapshot per search/check, independent of available release versions.
    internal sealed class InstalledSnapshot
    {
        readonly List<InstalledRecord> records;
        readonly IDictionary<string, string> links;
        public bool Complete { get; private set; }
        internal static string LinksPath { get { return Path.Combine(Engine.DataDir, "installed-identities.json"); } }
        public InstalledSnapshot(IEnumerable<InstalledRecord> records, IDictionary<string, string> links, bool complete = true)
        { this.records = new List<InstalledRecord>(records); this.links = links; Complete = complete; }

        internal static string NameKey(string name)
        {
            return Data.Norm(Regex.Replace(name ?? "", @"\s+v?\d+(\.\d+)+.*$", ""));
        }

        internal static string RepositoryFromUrl(string value)
        {
            Uri url;
            if (!Uri.TryCreate(value, UriKind.Absolute, out url) || url.Scheme != "https" || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || url.UserInfo.Length > 0) return null;
            string[] parts = url.AbsolutePath.Trim('/').Split('/');
            if (parts.Length < 2) return null;
            string repo = parts[0] + "/" + parts[1];
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo.Substring(0, repo.Length - 4);
            return Data.IsGitHubRepo(repo) ? repo : null;
        }

        internal static Dictionary<string, string> ReadLinks()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(LinksPath)) return result;
            try
            {
                var values = Engine.Json.DeserializeObject(File.ReadAllText(LinksPath)) as Dictionary<string, object>;
                if (values != null) foreach (var pair in values) if (Data.IsGitHubRepo(pair.Value as string)) result[pair.Key] = (string)pair.Value;
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
            return result;
        }

        internal static void Link(InstalledRecord record, string repo)
        {
            if (record == null || !Data.IsGitHubRepo(repo)) throw new InvalidDataException("Invalid installed-app association.");
            var links = ReadLinks(); links[record.Fingerprint] = repo;
            Directory.CreateDirectory(Engine.DataDir);
            string temporary = LinksPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Engine.Json.Serialize(links), new UTF8Encoding(false));
                if (File.Exists(LinksPath)) File.Replace(temporary, LinksPath, null); else File.Move(temporary, LinksPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static InstalledSnapshot Capture()
        {
            var records = new List<InstalledRecord>();
            bool complete = true;
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey keys = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (keys == null) continue;
                            foreach (string sub in keys.GetSubKeyNames())
                                using (RegistryKey key = keys.OpenSubKey(sub))
                                {
                                    if (key == null) { complete = false; continue; }
                                    string name = key.GetValue("DisplayName") as string;
                                    if (string.IsNullOrWhiteSpace(name)) continue;
                                    string repo = null;
                                    foreach (string field in new[] { "URLInfoAbout", "HelpLink", "URLUpdateInfo" })
                                    { repo = RepositoryFromUrl(key.GetValue(field) as string); if (repo != null) break; }
                                    records.Add(new InstalledRecord { RegistryPath = hive + "|" + view + "|" + sub, Name = name,
                                        Version = key.GetValue("DisplayVersion") as string ?? "", Publisher = key.GetValue("Publisher") as string,
                                        InstallLocation = key.GetValue("InstallLocation") as string, UninstallString = key.GetValue("UninstallString") as string,
                                        Repository = repo, PackageId = key.GetValue("WinGetPackageIdentifier") as string ?? key.GetValue("ChocolateyPackageName") as string ?? sub });
                                }
                        }
                    }
                    catch (System.Security.SecurityException) { complete = false; } catch (UnauthorizedAccessException) { complete = false; } catch (IOException) { complete = false; }
                }
            return new InstalledSnapshot(records, ReadLinks(), complete);
        }

        public InstalledMatch Resolve(string source, string id, string name)
        {
            var confirmed = new List<InstalledRecord>(); var possible = new List<InstalledRecord>();
            foreach (InstalledRecord record in records)
            {
                string repo = record.Repository, linked;
                if (string.IsNullOrEmpty(repo) && links.TryGetValue(record.Fingerprint, out linked)) repo = linked;
                bool exact = source == "github" ? string.Equals(repo, id, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(record.PackageId, id, StringComparison.OrdinalIgnoreCase);
                if (exact) confirmed.Add(record);
                else if (NameKey(name).Length >= 3 && NameKey(record.Name) == NameKey(name) && (source != "github" || string.IsNullOrEmpty(repo))) possible.Add(record);
            }
            // HKCU's uninstall view is shared across 32/64-bit registry views; deduplicate those copies.
            confirmed = Unique(confirmed); possible = Unique(possible);
            if (confirmed.Count == 1) return new InstalledMatch { Record = confirmed[0], Confirmed = true, Reason = source == "github" ? "Repository identity confirmed" : "Exact package identity" };
            if (confirmed.Count > 1) return new InstalledMatch { Reason = "Multiple installed registrations match this identity" };
            if (possible.Count == 1) return new InstalledMatch { Record = possible[0], Confirmed = source != "github", Reason = source == "github" ? "Name matches; repository identity needs confirmation" : "Unique installed display name" };
            return new InstalledMatch { Reason = possible.Count > 1 ? "Multiple installed apps share this name" : "No installed match" };
        }

        static List<InstalledRecord> Unique(List<InstalledRecord> values)
        {
            var result = new List<InstalledRecord>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in values)
            {
                string identity = r.RegistryPath.StartsWith("CurrentUser|", StringComparison.Ordinal) ? r.RegistryPath.Replace("|Registry32|", "|Registry64|") : r.RegistryPath;
                if (seen.Add(identity)) result.Add(r);
            }
            return result;
        }
    }
}
