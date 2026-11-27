// Source-specific installation arguments. These remain in the in-memory session catalog.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace Freshkit
{
    internal sealed class AppLanguage
    {
        public string Code, Name, PackageId;
        public override string ToString() { return Name; }
    }
    internal sealed class AppComponent
    {
        public string Flag, Name, ControlName;
    }
    internal sealed class AppOptionPreset
    {
        public string Name, Arguments, Documentation;
        public override string ToString() { return Name; }
    }
    internal sealed class AppVersion
    {
        public string Label, PackageId, DisplayName;
        public override string ToString() { return Label; }
    }
    internal static class AppOptions
    {
        // One catalog entry, several installable versions. The first entry is the family's catalog default and logo.
        // Fields: package id | picker label | display name in the app list.
        static readonly string[][] VersionFamilies =
        {
            new[] { "winget",
                "Microsoft.VisualStudio.BuildTools|2026 (latest)|Visual Studio Build Tools 2026",
                "Microsoft.VisualStudio.2022.BuildTools|2022|Visual Studio Build Tools 2022",
                "Microsoft.VisualStudio.2019.BuildTools|2019 (workloads as advanced arguments only)|Visual Studio Build Tools 2019" },
            new[] { "choco",
                "python3|Latest Python 3|Python 3",
                "python314|3.14|Python 3.14",
                "python313|3.13|Python 3.13",
                "python312|3.12|Python 3.12",
                "python311|3.11|Python 3.11" },
        };
        public static List<AppVersion> Versions(string source, string id)
        {
            var result = new List<AppVersion>();
            foreach (string[] family in VersionFamilies)
            {
                if (family[0] != source) continue;
                var versions = new List<AppVersion>();
                bool member = false;
                for (int i = 1; i < family.Length; i++)
                {
                    string[] f = family[i].Split('|');
                    versions.Add(new AppVersion { PackageId = f[0], Label = f[1], DisplayName = f[2] });
                    if (string.Equals(f[0], id, StringComparison.OrdinalIgnoreCase)) member = true;
                }
                if (member) return versions;
            }
            return result;
        }
        public static bool SameVersionFamily(string source, string a, string b)
        {
            return Versions(source, a).Exists(v => string.Equals(v.PackageId, b, StringComparison.OrdinalIgnoreCase));
        }
        /// The family's catalog default (used for its logo); the id itself when it has no other versions.
        public static string CanonicalVersionId(string source, string id)
        {
            var versions = Versions(source, id);
            return versions.Count > 0 ? versions[0].PackageId : id;
        }
        public static bool IsLocalizedFirefox(string id)
        {
            return string.Equals(id, "Mozilla.Firefox", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "Mozilla.Firefox.en-GB", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "Mozilla.Firefox.de", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "Mozilla.Firefox.fr", StringComparison.OrdinalIgnoreCase);
        }
        public static List<AppLanguage> Languages(string source, string id)
        {
            var result = new List<AppLanguage>();
            if (!(source == "winget" && IsLocalizedFirefox(id)) && !(source == "choco" && string.Equals(id, "firefox", StringComparison.OrdinalIgnoreCase))) return result;
            if (source == "choco") result.Add(new AppLanguage { Code = "", Name = "Package default" });
            result.Add(new AppLanguage { Code = "en-GB", Name = "English (United Kingdom)", PackageId = "Mozilla.Firefox.en-GB" });
            result.Add(new AppLanguage { Code = "en-US", Name = "English (United States)", PackageId = "Mozilla.Firefox" });
            result.Add(new AppLanguage { Code = "de", Name = "German", PackageId = "Mozilla.Firefox.de" });
            result.Add(new AppLanguage { Code = "fr", Name = "French", PackageId = "Mozilla.Firefox.fr" });
            return result;
        }
        public static string LanguagePackage(string currentId, string language)
        {
            foreach (var choice in Languages("winget", currentId)) if (choice.Code == language) return choice.PackageId;
            throw new System.IO.InvalidDataException("Choose one of the supported Firefox languages.");
        }
        public static List<AppComponent> Components(string source, string id)
        {
            var result = new List<AppComponent>();
            if (source != "choco") return result;
            if (string.Equals(id, "git", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "git.install", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new AppComponent { Flag = "NoShellIntegration", Name = "Explorer Git Bash / Git GUI context menus", ControlName = "GitShellIntegration" });
                result.Add(new AppComponent { Flag = "NoGitLfs", Name = "Git LFS", ControlName = "GitLfs" });
            }
            if (string.Equals(id, "firefox", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new AppComponent { Flag = "NoDesktopShortcut", Name = "Desktop shortcut", ControlName = "FirefoxDesktopShortcut" });
                result.Add(new AppComponent { Flag = "NoTaskbarShortcut", Name = "Taskbar shortcut", ControlName = "FirefoxTaskbarShortcut" });
                result.Add(new AppComponent { Flag = "NoMaintenanceService", Name = "Mozilla Maintenance Service", ControlName = "FirefoxMaintenanceService" });
            }
            return result;
        }
        public static bool IsBuildTools(string source, string id)
        {
            return source == "winget" && (string.Equals(id, "Microsoft.VisualStudio.BuildTools", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "Microsoft.VisualStudio.2022.BuildTools", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "Microsoft.VisualStudio.2019.BuildTools", StringComparison.OrdinalIgnoreCase));
        }
        /// Keeps the chosen workloads that the other Build Tools version also offers (MAUI maps between its two IDs).
        public static string SwitchBuildToolsVersion(string fromId, string toId, string existing)
        {
            if (!IsBuildTools("winget", toId)) return existing;
            // Without a verified workload list for the target (2019), the arguments stay as they are for manual editing.
            if (Workloads("winget", toId).Count == 0) return existing;
            string maui = toId.IndexOf("2022", StringComparison.Ordinal) >= 0 ? "Microsoft.VisualStudio.Workload.XamarinBuildTools" : "Microsoft.VisualStudio.Workload.NetCrossPlatBuildTools";
            var chosen = new List<string>(); var old = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in Workloads("winget", fromId))
            {
                old.Add(w.Flag);
                if (HasWorkload(existing, w.Flag)) chosen.Add(w.Flag.EndsWith("XamarinBuildTools") || w.Flag.EndsWith("NetCrossPlatBuildTools") ? maui : w.Flag);
            }
            var tokens = ArgumentTokens(existing); var kept = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], "--add", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count && old.Contains(tokens[i + 1].Split(';')[0])) { i++; continue; }
                kept.Add(tokens[i]);
            }
            return WorkloadArguments(toId, string.Join(" ", kept.ToArray()), chosen.ToArray(), HasToken(existing, "--includeRecommended"));
        }
        /// Visual Studio Build Tools workloads (learn.microsoft.com: workload and component IDs for Build Tools).
        public static List<AppComponent> Workloads(string source, string id)
        {
            var result = new List<AppComponent>();
            if (!IsBuildTools(source, id) || id.IndexOf("2019", StringComparison.Ordinal) >= 0) return result;
            bool vs2022 = id.IndexOf("2022", StringComparison.Ordinal) >= 0;
            Action<string, string> add = (workload, name) => result.Add(new AppComponent { Flag = "Microsoft.VisualStudio.Workload." + workload, Name = name, ControlName = "Workload" + workload });
            add("VCTools", "Desktop development with C++ (MSVC, Windows SDK)");
            add("ManagedDesktopBuildTools", ".NET desktop build tools");
            add("MSBuildTools", "MSBuild tools");
            add("WebBuildTools", "Web development build tools");
            add("NodeBuildTools", "Node.js build tools");
            add("UniversalBuildTools", vs2022 ? "Universal Windows Platform build tools" : "WinUI application development build tools");
            add(vs2022 ? "XamarinBuildTools" : "NetCrossPlatBuildTools", ".NET Multi-platform App UI development");
            add("LinuxBuildTools", "C++ tools for Linux and Mac development");
            add("DataBuildTools", "Data storage and processing build tools");
            add("AzureBuildTools", "Azure development build tools");
            add("OfficeBuildTools", "Office/SharePoint build tools");
            add("VisualStudioExtensionBuildTools", "Visual Studio extension development");
            return result;
        }
        public const string BuildToolsSilent = "--quiet --wait --norestart --nocache";
        public static bool HasWorkload(string arguments, string workload)
        {
            var tokens = ArgumentTokens(arguments);
            for (int i = 0; i + 1 < tokens.Count; i++) if (string.Equals(tokens[i], "--add", StringComparison.OrdinalIgnoreCase) && string.Equals(tokens[i + 1].Split(';')[0], workload, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        public static bool HasToken(string arguments, string token) { return ArgumentTokens(arguments).Exists(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase)); }
        /// Rebuilds the installer override: silent switches, then the chosen workloads; other custom arguments are kept.
        public static string WorkloadArguments(string id, string existing, string[] workloads, bool includeRecommended)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in Workloads("winget", id)) known.Add(w.Flag);
            var tokens = ArgumentTokens(existing); var kept = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], "--add", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count && known.Contains(tokens[i + 1].Split(';')[0])) { i++; continue; }
                if (string.Equals(tokens[i], "--includeRecommended", StringComparison.OrdinalIgnoreCase)) continue;
                kept.Add(tokens[i]);
            }
            foreach (string s in BuildToolsSilent.Split(' ')) if (!kept.Exists(t => string.Equals(t, s, StringComparison.OrdinalIgnoreCase))) kept.Add(s);
            var chosen = new HashSet<string>(workloads ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (var w in Workloads("winget", id)) if (chosen.Contains(w.Flag)) { kept.Add("--add"); kept.Add(w.Flag); }
            if (includeRecommended && chosen.Count > 0) kept.Add("--includeRecommended");
            return string.Join(" ", kept.ToArray());
        }
        static List<string> ArgumentTokens(string arguments)
        {
            var result = new List<string>();
            string text = arguments ?? ""; int start = -1; bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]) && !quoted)
                {
                    if (start >= 0) { result.Add(text.Substring(start, i - start)); start = -1; }
                    continue;
                }
                if (start < 0) start = i;
                if (text[i] == '"')
                {
                    int slashes = 0; for (int j = i - 1; j >= 0 && text[j] == '\\'; j--) slashes++;
                    if (slashes % 2 == 0) quoted = !quoted;
                }
            }
            if (start >= 0) result.Add(text.Substring(start));
            return result;
        }
        public static bool HasFlag(string arguments, string flag) { return ArgumentTokens(arguments).Exists(t => string.Equals(t, "/" + flag, StringComparison.OrdinalIgnoreCase)); }
        public static string CurrentLanguage(string source, string id, string arguments)
        {
            if (source == "winget") { foreach (var choice in Languages(source, id)) if (string.Equals(choice.PackageId, id, StringComparison.OrdinalIgnoreCase)) return choice.Code; return ""; }
            string token = ArgumentTokens(arguments).Find(t => t.StartsWith("/l:", StringComparison.OrdinalIgnoreCase));
            if (token == null) return "";
            foreach (var choice in Languages(source, id)) if (string.Equals(choice.Code, token.Substring(3), StringComparison.OrdinalIgnoreCase)) return choice.Code;
            return "__custom";
        }
        public static string GuidedArguments(string source, string id, string existing, string language, string[] omitted)
        {
            var tokens = ArgumentTokens(existing);
            var additions = new List<string>();
            if (source == "choco" && string.Equals(id, "firefox", StringComparison.OrdinalIgnoreCase) && language != "__custom")
            {
                if (!Languages(source, id).Exists(l => l.Code == language)) throw new System.IO.InvalidDataException("Unsupported Firefox language.");
                tokens.RemoveAll(t => t.StartsWith("/l:", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(language)) additions.Add("/l:" + language);
            }
            foreach (var component in Components(source, id))
            {
                tokens.RemoveAll(t => string.Equals(t, "/" + component.Flag, StringComparison.OrdinalIgnoreCase));
                if (Array.Exists(omitted ?? new string[0], f => string.Equals(f, component.Flag, StringComparison.OrdinalIgnoreCase))) additions.Add("/" + component.Flag);
            }
            string value = string.Join(" ", tokens.ToArray());
            return value + (value.Length > 0 && additions.Count > 0 ? " " : "") + string.Join(" ", additions.ToArray());
        }
        public static string Label(string source) { return source == "choco" ? "Chocolatey package parameters" : source == "github" ? "GitHub installer arguments" : "Installer override arguments (winget --override)"; }
        public static string Help(string source, string id)
        {
            if (source == "store" || Data.IsStoreId(id)) return "Microsoft Store apps do not expose installer arguments. Language and optional features are managed by the Store app itself.";
            if (source == "choco") return "Passed as one --package-parameters value to this Chocolatey package. Use its documented package parameters for language or optional components; installer switches and Chocolatey CLI options are different. Empty uses package defaults.";
            if (IsBuildTools(source, id) && Workloads(source, id).Count == 0) return "Passed as one --override value to the Visual Studio 2019 installer. Include the silent switches (" + BuildToolsSilent + ") and add workloads as --add <workload ID> arguments from Microsoft's Build Tools 2019 workload list.";
            if (IsBuildTools(source, id)) return "Passed as one --override value to the Visual Studio installer. Choose workloads above; they become --add arguments next to the silent switches (" + BuildToolsSilent + "). Without a workload, Build Tools installs almost nothing. Other installer options, such as --add for single components, can stay here.";
            if (source == "github") return "EXE: replaces the detected silent arguments, so include the installer's silent switches. MSI: appends properties to /i, /qn and /norestart. Options depend on the latest release's installer. Empty keeps automatic silent defaults.";
            return "Passed as one --override value to this app's installer, replacing winget's default installer arguments. Include the installer's silent switches. These are not winget CLI options: --locale, --scope, --id and --source do not belong here. Empty uses winget's defaults. Language may require a separate localized package (Firefox en-GB is already in the catalog).";
        }
        public static string Validate(string source, string id, string value)
        {
            value = value ?? "";
            if (value.Length > 2048) return "Keep arguments within 2,048 characters.";
            foreach (char c in value) if (char.IsControl(c) || c == '\u2028' || c == '\u2029' || "|&;`<>$".IndexOf(c) >= 0) return "Use a single line of installer arguments without pipes, shell operators or control characters.";
            bool quoted = false;
            for (int i = 0; i < value.Length; i++) if (value[i] == '"' && (i == 0 || value[i - 1] != '\\')) quoted = !quoted;
            if (quoted) return "Close all double quotes before saving.";
            if (Regex.IsMatch(value.Replace("\"", "").Replace("'", ""), @"(?i)(?:https?|ftp|file)://|\\\\|(?:^|[\s=])(?:powershell|pwsh|cmd|wscript|cscript|mshta)(?:\.exe)?(?:\s|$)|(?:^|\s)(?:--?|/)(?:i|a|x|j|p|update|uninstall|id|source|package|package-id|url|download-url|installer-url|install-url|override|locale|scope|manifest|command|encodedcommand)(?:[\s=:]|$)"))
                return "Enter this package's installer options only; package/source selection, download URLs and shell commands cannot be changed here.";
            if ((source == "store" || Data.IsStoreId(id)) && value.Trim().Length > 0) return "Microsoft Store apps do not support installer overrides.";
            return null;
        }
        public static List<AppOptionPreset> Presets(string source, string id)
        {
            var result = new List<AppOptionPreset>();
            if (source == "choco" && string.Equals(id, "firefox", StringComparison.OrdinalIgnoreCase))
                result.Add(new AppOptionPreset { Name = "Firefox: British English", Arguments = "/l:en-GB", Documentation = "https://community.chocolatey.org/packages/firefox" });
            // Maintained package's own parameter reference; never transfer these to winget or GitHub.
            if (source == "choco" && (string.Equals(id, "git.install", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "git", StringComparison.OrdinalIgnoreCase)))
            {
                const string docs = "https://github.com/chocolatey-community/chocolatey-packages/blob/master/automatic/git.install/ARGUMENTS.md";
                result.Add(new AppOptionPreset { Name = "Git: omit Explorer context menu entries", Arguments = "/NoShellIntegration", Documentation = docs });
                result.Add(new AppOptionPreset { Name = "Git: omit Git LFS", Arguments = "/NoGitLfs", Documentation = docs });
            }
            return result;
        }
    }
}
