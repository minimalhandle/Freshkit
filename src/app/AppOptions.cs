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
    internal static class AppOptions
    {
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
