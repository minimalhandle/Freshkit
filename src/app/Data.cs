// Freshkit - list file (packages, winget apps, settings rules, profiles) and view models.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Freshkit
{
    public abstract class Notifier : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise(string name) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(name)); }
    }

    public sealed class AppItem : Notifier
    {
        bool selected, installed; Visibility visibility = Visibility.Visible;
        public string Id { get; set; }
        public string Name { get; set; }
        public string Parameters { get; set; }
        public bool IsWinget { get; set; }
        /// GitHub release package; Id is owner/repo.
        public bool IsGitHub { get; set; }
        public string Source { get { return IsGitHub ? "github" : IsWinget ? "winget" : "choco"; } }
        public bool DefaultOn { get; set; }
        public string Category { get; set; }
        public Brush TileBrush { get; set; }
        public Brush TileForeground { get; set; }
        AppIcon artwork;
        // GitHub projects share names with unrelated apps, so they never borrow an installed app's icon by name.
        public AppIcon Artwork { get { return artwork ?? (artwork = AppIcons.Get(Id, Name, Source, Source != "github")); } }
        public Action Changed;
        /// Selection key used by the runner and profiles: choco id, winget:<Id> or github:<owner/repo>.
        public string Key { get { return Data.KeyFor(Source, Id); } }
        /// Package id the engine reports for this item.
        public string EnginePackageId { get { return Data.EngineKeyFor(Source, Id); } }
        public string Monogram
        {
            get { foreach (char c in Name) if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString(); return "?"; }
        }
        public string Subtitle { get { return Parameters.Length > 0 ? Id + "  ·  " + Parameters : Id; } }
        public string Tooltip
        {
            get
            {
                return Name + (IsGitHub ? "\nLatest GitHub release of: github.com/" : IsStore ? "\nMicrosoft Store app (via winget): " : IsWinget ? "\nwinget package: " : "\nChocolatey package: ") + Id +
                    (Parameters.Length > 0 ? "\nParameters: " + Parameters : "") + (installed ? "\nAlready installed on this PC" : "") +
                    "\nRight-click for installation options or to remove it from the list.";
            }
        }
        public Visibility WingetVisibility { get { return IsWinget || IsGitHub ? Visibility.Visible : Visibility.Collapsed; } }
        public bool IsStore { get { return IsWinget && Data.IsStoreId(Id); } }
        public string SourceLabel { get { return IsGitHub ? "GitHub" : IsStore ? "Store" : IsWinget ? "winget" : "Chocolatey"; } }
        public Visibility InstalledVisibility { get { return installed ? Visibility.Visible : Visibility.Collapsed; } }
        public bool Installed { get { return installed; } set { installed = value; Raise("InstalledVisibility"); Raise("Tooltip"); } }
        public bool IsSelected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Raise("IsSelected"); if (Changed != null) Changed(); }
        }
        public Visibility Visibility { get { return visibility; } set { visibility = value; Raise("Visibility"); } }
    }

    public sealed class AppGroup : Notifier
    {
        Visibility visibility = Visibility.Visible;
        public string Name { get; set; }
        public Brush Dot { get; set; }
        public ObservableCollection<AppItem> Items { get; private set; }
        public AppGroup() { Items = new ObservableCollection<AppItem>(); }
        public string Header { get { return Name.ToUpperInvariant(); } }
        public string CountText { get { int n = 0; foreach (AppItem i in Items) if (i.IsSelected) n++; return n + " of " + Items.Count + " selected"; } }
        public string ToggleText { get { foreach (AppItem i in Items) if (!i.IsSelected) return "Select all"; return "Clear all"; } }
        public Visibility Visibility { get { return visibility; } set { visibility = value; Raise("Visibility"); } }
        public void Refresh() { Raise("CountText"); Raise("ToggleText"); }
    }

    /// One entry in the Apps page category rail ("*" = all apps, "selected" = ticked apps, else a category name).
    public sealed class CategoryFilter : Notifier
    {
        bool isChecked; string count = "";
        public string Key { get; set; }
        public string Name { get; set; }
        public Brush Dot { get; set; }
        public Action Picked;
        public string Count { get { return count; } set { if (count == value) return; count = value; Raise("Count"); } }
        public bool IsChecked
        {
            get { return isChecked; }
            set { if (isChecked == value) return; isChecked = value; Raise("IsChecked"); if (value && Picked != null) Picked(); }
        }
    }

    public sealed class ProfileItem : Notifier
    {
        bool selected = true;
        string status = "Checking settings...", detail = "";
        public string Id { get; set; }
        public string Name { get; set; }
        public string Tooltip { get; set; }
        public string StatusText { get { return status; } set { status = value; Raise("StatusText"); } }
        public string Detail { get { return detail; } set { detail = value; Raise("Detail"); } }
        public bool IsSelected { get { return selected; } set { selected = value; Raise("IsSelected"); } }
    }

    public sealed class PickItem : Notifier
    {
        AppIcon artwork;
        public AppIcon Artwork
        {
            get { var app = Tag as InstalledApp; return app == null ? null : artwork ?? (artwork = AppIcons.Get(app.Id, app.Name, true)); }
        }
        bool selected;
        public string Name { get; set; }
        public string Detail { get; set; }
        public object Tag { get; set; }
        public bool IsSelected { get { return selected; } set { selected = value; Raise("IsSelected"); } }
    }

    internal sealed class PackageEntry
    {
        public string Id, Name, Parameters, Category; public bool Enabled, IsWinget, IsGitHub;
        public string Source { get { return IsGitHub ? "github" : IsWinget ? "winget" : "choco"; } }
    }
    internal sealed class ProfileEntry { public string Id, Name; public List<string> Locations = new List<string>(); }

    internal sealed class SavedProfile
    {
        public string Name = "";
        public List<string> Apps = new List<string>(), Tweaks = new List<string>(), Remove = new List<string>();
        /// Built-in starter profiles omit tweaks=/remove= so applying them leaves Windows tweaks alone.
        public bool HasTweaks = true, HasRemove = true;

        public string ToLine()
        {
            return "::PROFILE " + Name + "|apps=" + string.Join(",", Apps.ToArray()) + "|tweaks=" + string.Join(",", Tweaks.ToArray()) + "|remove=" + string.Join(",", Remove.ToArray());
        }

        public static SavedProfile Parse(string entry)
        {
            string[] parts = entry.Split('|');
            var p = new SavedProfile { Name = parts[0].Trim(), HasTweaks = false, HasRemove = false };
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq < 0) continue;
                string key = parts[i].Substring(0, eq).Trim().ToLowerInvariant();
                var values = new List<string>();
                foreach (string v in parts[i].Substring(eq + 1).Split(',')) if (v.Trim().Length > 0) values.Add(v.Trim());
                if (key == "apps") p.Apps = values; else if (key == "tweaks") { p.Tweaks = values; p.HasTweaks = true; } else if (key == "remove") { p.Remove = values; p.HasRemove = true; }
            }
            return p;
        }
    }

    internal sealed class ListFile
    {
        public List<PackageEntry> Packages = new List<PackageEntry>();
        public List<ProfileEntry> Profiles = new List<ProfileEntry>();
        public List<SavedProfile> Saved = new List<SavedProfile>();

        static Match Section(string text, string name)
        {
            return Regex.Match(text, @"(?ms)^::BEGIN_" + name + @"\r?\n(?<body>.*?)^::END_" + name + @"\r?$");
        }

        public static ListFile Parse(string text)
        {
            var result = new ListFile();
            Match pkg = Section(text, "PACKAGES");
            if (!pkg.Success) throw new InvalidDataException("The list has no ::BEGIN_PACKAGES ... ::END_PACKAGES section.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string group = null;
            foreach (string raw in Regex.Split(pkg.Groups["body"].Value, @"\r?\n"))
            {
                if (IsGroup(raw, ref group)) continue;
                Match m = Regex.Match(raw, @"^::(?<kind>PACKAGE|DISABLED)\s+(?<entry>.+)$");
                if (!m.Success) continue;
                string[] parts = m.Groups["entry"].Value.Split('|');
                if (parts.Length > 3) throw new InvalidDataException("Use package-id|Display name|Optional parameters on each PACKAGE line: " + raw);
                string id = parts[0].Trim();
                if (!Regex.IsMatch(id, @"\A[a-zA-Z0-9]+(?:[.-][a-zA-Z0-9]+)*\z")) throw new InvalidDataException("Invalid Chocolatey package ID: " + id);
                if (!seen.Add(id)) throw new InvalidDataException("Duplicate Chocolatey package ID: " + id);
                result.Packages.Add(Entry(id, parts, m.Groups["kind"].Value == "PACKAGE", false, group));
            }
            Match win = Section(text, "WINGET");
            if (win.Success)
            {
                group = null;
                foreach (string raw in Regex.Split(win.Groups["body"].Value, @"\r?\n"))
                {
                    if (IsGroup(raw, ref group)) continue;
                    Match m = Regex.Match(raw, @"^::(?<kind>WINGET|DISABLED)\s+(?<entry>.+)$");
                    if (!m.Success) continue;
                    string[] parts = m.Groups["entry"].Value.Split('|');
                    if (parts.Length > 3) throw new InvalidDataException("Use winget-id|Display name|Optional arguments on each WINGET line: " + raw);
                    string id = parts[0].Trim();
                    if (!Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z")) throw new InvalidDataException("Invalid winget package ID: " + id);
                    if (!seen.Add("winget:" + id)) throw new InvalidDataException("Duplicate winget package ID: " + id);
                    result.Packages.Add(Entry(id, parts, m.Groups["kind"].Value == "WINGET", true, group));
                }
            }
            Match gh = Section(text, "GITHUB");
            if (gh.Success)
            {
                group = null;
                foreach (string raw in Regex.Split(gh.Groups["body"].Value, @"\r?\n"))
                {
                    if (IsGroup(raw, ref group)) continue;
                    Match m = Regex.Match(raw, @"^::(?<kind>GITHUB|DISABLED)\s+(?<entry>.+)$");
                    if (!m.Success) continue;
                    string[] parts = m.Groups["entry"].Value.Split('|');
                    if (parts.Length > 3) throw new InvalidDataException("Use owner/repo|Display name|Optional silent installer arguments on each GITHUB line: " + raw);
                    string id = parts[0].Trim();
                    if (!Data.IsGitHubRepo(id)) throw new InvalidDataException("Invalid GitHub repository: " + id);
                    if (!seen.Add("github:" + id)) throw new InvalidDataException("Duplicate GitHub repository: " + id);
                    PackageEntry e = Entry(id, parts, m.Groups["kind"].Value == "GITHUB", false, group);
                    e.IsGitHub = true;
                    result.Packages.Add(e);
                }
            }
            Match cfg = Section(text, "CONFIG");
            if (cfg.Success)
            {
                var byId = new Dictionary<string, ProfileEntry>();
                foreach (string raw in Regex.Split(cfg.Groups["body"].Value, @"\r?\n"))
                {
                    Match m = Regex.Match(raw, @"^::CONFIG\s+(?<entry>.+)$");
                    if (!m.Success) continue;
                    string[] parts = m.Groups["entry"].Value.Split('|');
                    if (parts.Length != 4 && parts.Length != 5) throw new InvalidDataException("Use profile-id|Display name|Root|Relative path|optional processes for CONFIG rules: " + raw);
                    string id = parts[0].Trim();
                    ProfileEntry p;
                    if (!byId.TryGetValue(id, out p)) { p = new ProfileEntry(); p.Id = id; byId[id] = p; result.Profiles.Add(p); }
                    p.Name = parts[1].Trim();
                    p.Locations.Add(parts[2].Trim().ToUpperInvariant() + ":" + parts[3].Trim());
                }
            }
            Match prof = Section(text, "PROFILES");
            if (prof.Success)
                foreach (string raw in Regex.Split(prof.Groups["body"].Value, @"\r?\n"))
                {
                    Match m = Regex.Match(raw, @"^::PROFILE\s+(?<entry>.+)$");
                    if (m.Success) result.Saved.Add(SavedProfile.Parse(m.Groups["entry"].Value));
                }
            return result;
        }

        /// "::GROUP Name" names the Apps page section for the package lines that follow it.
        static bool IsGroup(string raw, ref string group)
        {
            Match m = Regex.Match(raw, @"^::GROUP(?:\s+(?<name>.*))?$");
            if (!m.Success) return false;
            string name = m.Groups["name"].Value.Trim();
            group = name.Length > 0 ? name : null;
            return true;
        }

        static PackageEntry Entry(string id, string[] parts, bool enabled, bool winget, string group)
        {
            var e = new PackageEntry();
            e.Category = group;
            e.Id = id;
            e.Name = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : id;
            e.Parameters = parts.Length > 2 ? parts[2].Trim() : "";
            string error = AppOptions.Validate(winget ? "winget" : "choco", id, e.Parameters);
            if (error != null) throw new InvalidDataException("Invalid arguments for " + id + ": " + error);
            e.Enabled = enabled;
            e.IsWinget = winget;
            return e;
        }

        // ---------------------------------------------------------------- session edits (no companion files)
        static List<string> LoadLines()
        {
            bool custom;
            return new List<string>(Regex.Split(Engine.CurrentListText(out custom).TrimEnd('\r', '\n'), @"\r?\n"));
        }

        static void SaveLines(List<string> lines)
        {
            Engine.SessionList = string.Join("\r\n", lines.ToArray()) + "\r\n";
        }

        static int EnsureSection(List<string> lines, string name, string comment)
        {
            int end = lines.FindIndex(l => l.TrimEnd() == "::END_" + name);
            if (end >= 0) return end;
            lines.Add("::BEGIN_" + name);
            if (comment != null) lines.Add(comment);
            lines.Add("::END_" + name);
            return lines.Count - 1;
        }

        static string Clean(string s) { return Regex.Replace(s ?? "", @"[|\r\n]+", " ").Trim(); }

        /// Store apps ("store") are winget entries with a Store product ID.
        static string SectionFor(string source) { return source == "github" ? "GITHUB" : source == "winget" || source == "store" ? "WINGET" : "PACKAGES"; }

        public static bool AddPackage(bool winget, string id, string name) { return AddPackage(winget ? "winget" : "choco", id, name); }

        public static bool AddPackage(string source, string id, string name)
        {
            List<string> lines = LoadLines();
            string section = SectionFor(source);
            int begin = lines.FindIndex(l => l.TrimEnd() == "::BEGIN_" + section);
            int end = EnsureSection(lines, section, source == "github" ? ":: GitHub releases: ::GITHUB owner/repo|Display name|Optional silent installer arguments" : source == "winget" ? ":: winget apps added with Freshkit" : null);
            if (begin < 0) begin = lines.FindIndex(l => l.TrimEnd() == "::BEGIN_" + section);
            var pattern = new Regex(@"^::(PACKAGE|DISABLED|WINGET|GITHUB)\s+" + Regex.Escape(id) + @"\s*(\||$)", RegexOptions.IgnoreCase);
            for (int i = begin + 1; i < end; i++) if (pattern.IsMatch(lines[i])) return false;
            // Session additions go above the first ::GROUP so they aren't filed under a built-in section.
            int at = end;
            for (int i = begin + 1; i < end; i++) if (lines[i].StartsWith("::GROUP", StringComparison.OrdinalIgnoreCase)) { at = i; break; }
            lines.Insert(at, "::" + (section == "PACKAGES" ? "PACKAGE" : section) + " " + id + "|" + Clean(name) + "|");
            SaveLines(lines);
            return true;
        }

        public static void RemovePackage(bool winget, string id) { RemovePackage(winget ? "winget" : "choco", id); }

        public static void SetPackageParameters(string source, string id, string parameters)
        {
            SetPackageOptions(source, id, id, parameters);
        }

        public static void SetPackageOptions(string source, string id, string targetId, string parameters)
        {
            bool changedId = !string.Equals(id, targetId, StringComparison.OrdinalIgnoreCase);
            if (changedId && (source != "winget" || !AppOptions.IsLocalizedFirefox(id) || !AppOptions.IsLocalizedFirefox(targetId)))
                throw new InvalidDataException("Only supported Firefox language packages can be switched here.");
            string error = AppOptions.Validate(source, targetId, parameters);
            if (error != null) throw new InvalidDataException(error);
            List<string> lines = LoadLines();
            if (changedId)
                foreach (var entry in Parse(string.Join("\r\n", lines.ToArray())).Packages)
                    if (entry.Source == source && string.Equals(entry.Id, targetId, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("That Firefox language package is already in this session. Remove the duplicate before changing language.");
            string section = SectionFor(source);
            int begin = lines.FindIndex(l => l.TrimEnd() == "::BEGIN_" + section);
            int end = lines.FindIndex(l => l.TrimEnd() == "::END_" + section);
            var pattern = new Regex(@"^::(?:PACKAGE|DISABLED|WINGET|GITHUB)\s+" + Regex.Escape(id) + @"\s*(?:\||$)", RegexOptions.IgnoreCase);
            if (begin >= 0 && end > begin)
                for (int i = begin + 1; i < end; i++) if (pattern.IsMatch(lines[i]))
                {
                    string[] parts = lines[i].Split('|');
                    string prefix = Regex.Match(parts[0], @"^::\w+\s+").Value;
                    lines[i] = prefix + targetId + "|" + (parts.Length > 1 ? parts[1] : id) + "|" + (parameters ?? "").Trim();
                    if (changedId)
                    {
                        string oldKey = Data.KeyFor(source, id), newKey = Data.KeyFor(source, targetId);
                        for (int j = 0; j < lines.Count; j++)
                            if (Regex.IsMatch(lines[j], @"^::PROFILE\s+"))
                                lines[j] = Regex.Replace(lines[j], @"(?i)(\|\s*apps\s*=\s*)([^|]*)", delegate(Match m)
                                {
                                    var keys = new List<string>();
                                    foreach (string key in m.Groups[2].Value.Split(',')) keys.Add(string.Equals(key.Trim(), oldKey, StringComparison.OrdinalIgnoreCase) ? newKey : key);
                                    return m.Groups[1].Value + string.Join(",", keys.ToArray());
                                });
                    }
                    Parse(string.Join("\r\n", lines.ToArray()));
                    SaveLines(lines); return;
                }
            throw new InvalidDataException("This app is no longer in the session catalog. Reload the list and try again.");
        }

        public static void RemovePackage(string source, string id)
        {
            List<string> lines = LoadLines();
            string section = SectionFor(source);
            int begin = lines.FindIndex(l => l.TrimEnd() == "::BEGIN_" + section);
            int end = lines.FindIndex(l => l.TrimEnd() == "::END_" + section);
            if (begin < 0 || end < 0) return;
            var pattern = new Regex(@"^::(PACKAGE|DISABLED|WINGET|GITHUB)\s+" + Regex.Escape(id) + @"\s*(\||$)", RegexOptions.IgnoreCase);
            for (int i = end - 1; i > begin; i--) if (pattern.IsMatch(lines[i])) lines.RemoveAt(i);
            SaveLines(lines);
        }

        public static void SaveProfile(SavedProfile profile)
        {
            profile.Name = Clean(profile.Name).Replace(",", " ");
            List<string> lines = LoadLines();
            DeleteProfileLines(lines, profile.Name);
            int end = EnsureSection(lines, "PROFILES", ":: Saved selections (Freshkit)");
            lines.Insert(end, profile.ToLine());
            SaveLines(lines);
        }

        public static void DeleteProfile(string name)
        {
            List<string> lines = LoadLines();
            DeleteProfileLines(lines, name);
            SaveLines(lines);
        }

        static void DeleteProfileLines(List<string> lines, string name)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                Match m = Regex.Match(lines[i], @"^::PROFILE\s+(?<name>[^|]*)");
                if (m.Success && string.Equals(m.Groups["name"].Value.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) lines.RemoveAt(i);
            }
        }
    }

    internal static class Data
    {
        /// .NET 4.7+ defaults to the OS's TLS choice (1.2 and 1.3 on Windows 11). Forcing "Tls12" would drop TLS 1.3,
        /// so only add TLS 1.2 when an older runtime has pinned a legacy protocol list.
        public static void EnsureModernTls()
        {
            if (System.Net.ServicePointManager.SecurityProtocol != 0)
                System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
        }

        /// Microsoft Store product IDs (e.g. 9NKSQGP7F2NH, XP89DCGQ3K6VLD) install through winget's msstore source.
        public static bool IsStoreId(string id)
        {
            return Regex.IsMatch(id ?? "", @"\A(?:9[A-Z0-9]{11}|XP[A-Z0-9]{12})\z");
        }

        /// Numeric parts of a version such as "v15.8.118.0"; null when it has none.
        public static int[] VersionParts(string version)
        {
            Match m = Regex.Match(version ?? "", @"\d+(?:\.\d+)*");
            if (!m.Success) return null;
            string[] parts = m.Value.Split('.');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) { long n; long.TryParse(parts[i], out n); result[i] = (int)Math.Min(n, int.MaxValue); }
            return result;
        }

        /// Compares versions only when they use the same scheme: date-style (2026.1109) and semantic (11.42) versions
        /// are not comparable. Returns null when they can't be compared.
        public static int? CompareVersions(string a, string b)
        {
            int[] x = VersionParts(a), y = VersionParts(b);
            if (x == null || y == null) return null;
            if ((x[0] >= 1000) != (y[0] >= 1000)) return null;
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        public static bool IsGitHubRepo(string id)
        {
            return Regex.IsMatch(id ?? "", @"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}\z") && !Regex.IsMatch(id, @"/\.\.?\z");
        }

        /// Matches the runner's catalogue key for a GitHub release package.
        public static string GitHubEngineKey(string repo)
        {
            return "github-" + Regex.Replace(repo.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        }

        public static string KeyFor(string source, string id) { return source == "github" ? "github:" + id : source == "winget" || source == "store" ? "winget:" + id : id; }
        public static string EngineKeyFor(string source, string id) { return source == "github" ? GitHubEngineKey(id) : source == "winget" || source == "store" ? WingetEngineKey(id) : id; }

        /// Matches the runner's catalogue key for a winget package.
        public static string WingetEngineKey(string id)
        {
            return "winget-" + Regex.Replace(id.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        }

        /// Loose name normalisation for matching installed programs to list entries.
        public static string Norm(string s)
        {
            s = Regex.Replace((s ?? "").ToLowerInvariant(), @"\([^)]*\)", " ");
            s = Regex.Replace(s, @"\b(x64|x86|64-bit|32-bit|install|portable)\b", " ");
            return Regex.Replace(s, "[^a-z0-9]", "");
        }
    }

    internal static class Categories
    {
        public static readonly string[] Order = { "Browsers", "Messaging", "Media", "Imaging & 3D", "Documents & AI", "Gaming & devices", "Security", "Cloud & downloads", "Remote & network", "Utilities", "Developer", "Runtimes", "Other" };
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>();

        static Categories()
        {
            Put("Browsers", "#60A5FA", "firefox", "googlechrome", "brave", "microsoft-edge", "vivaldi", "opera", "librewolf", "waterfox", "chromium", "tor-browser");
            Put("Messaging", "#38BDF8", "discord", "chatterino", "tutanota", "thunderbird", "telegram", "signal", "slack", "zoom", "whatsapp", "microsoft-teams-new-bootstrapper", "pidgin", "trillian", "element-desktop");
            Put("Media", "#F472B6", "audacity", "mpvio.install", "mpv", "obs-studio", "spotify", "vlc", "stremio", "streamlink", "streamlink-twitch-gui", "handbrake", "k-litecodecpack-standard", "k-litecodecpackfull", "foobar2000", "plex", "musicbee", "aimp", "winamp", "mediamonkey", "itunes", "cccp");
            Put("Imaging & 3D", "#FB923C", "gimp", "paint.net", "krita", "blender", "inkscape", "irfanview", "xnview", "fsviewer", "greenshot", "sharex", "bambustudio", "cura-new", "davinci-resolve");
            Put("Documents & AI", "#C084FC", "claude", "chatgpt", "adobereader", "sumatrapdf.install", "sumatrapdf", "foxitreader", "libreoffice-fresh", "libreoffice-still", "openoffice", "pdf24", "cutepdf", "ghostscript", "evernote", "grammarly-for-windows", "googleearthpro", "nvda", "obsidian", "notion");
            Put("Gaming & devices", "#A78BFA", "steam", "epicgameslauncher", "rockstar-launcher", "playnite", "lghub", "openrgb.install", "pawnio", "goggalaxy", "ea-app", "ubisoft-connect", "msiafterburner", "battle.net");
            Put("Security", "#F87171", "1password", "bitwarden", "keepass", "keepassxc", "malwarebytes", "avastfreeantivirus", "avgantivirusfree", "avirafreeantivirus", "spybot", "superantispyware");
            Put("Cloud & downloads", "#22D3EE", "ab-download-manager", "transmission", "qbittorrent", "dropbox", "googledrive", "onedrive", "sugarsync");
            Put("Remote & network", "#4ADE80", "windscribe", "tailscale", "wireguard", "rustdesk", "anydesk.install", "anydesk", "teamviewer", "tightvnc", "ultravnc", "advanced-ip-scanner", "wireshark", "nordvpn", "protonvpn");
            Put("Utilities", "#34D399", "7zip.install", "7zip", "windirstat", "rpi-imager", "everything", "powertoys", "peazip", "winrar", "wiztree", "teracopy", "revo-uninstaller", "open-shell", "launchy", "imgburn", "cdburnerxp", "infrarecorder", "ccleaner", "glaryutilities-free");
            Put("Developer", "#FBBF24", "git.install", "git", "gh", "github-desktop", "nodejs.install", "nodejs", "nodejs-lts", "notepadplusplus", "innosetup", "putty.install", "putty", "winscp", "vscode", "cursoride", "sublimetext4", "python", "python3", "python2", "docker-desktop", "winmerge", "filezilla", "eclipse", "powershell-core", "microsoft-windows-terminal");
            Colors["Runtimes"] = Parse("#94A3B8");
            Colors["Other"] = Parse("#9CA3AF");
        }

        static Color Parse(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        static void Put(string category, string hex, params string[] ids) { Colors[category] = Parse(hex); foreach (string id in ids) Map[id] = category; }

        public static string For(string id, bool winget)
        {
            string key = id;
            if (id != null && id.Contains("/")) key = id.Substring(id.LastIndexOf('/') + 1).ToLowerInvariant();
            else if (winget)
            {
                // winget ids look like Publisher.App: try the app part against the known list.
                string[] parts = id.Split('.');
                key = parts[parts.Length - 1];
                if (Regex.IsMatch(id, @"^Microsoft\.(VCRedist|DotNet|WindowsAppRuntime|EdgeWebView2)", RegexOptions.IgnoreCase)) return "Runtimes";
            }
            if (Regex.IsMatch(key, @"^(vcredist|dotnet|netfx|directx|javaruntime|temurin|corretto|jre|openjdk|webview2|vstor)", RegexOptions.IgnoreCase)) return "Runtimes";
            string c;
            if (Map.TryGetValue(key, out c)) return c;
            if (winget && Map.TryGetValue(key.ToLowerInvariant().Replace(" ", ""), out c)) return c;
            return "Other";
        }

        public static Brush Tile(string category, byte alpha)
        {
            Color c;
            if (category == null || !Colors.TryGetValue(category, out c)) c = Colors["Other"];
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
    }
}
