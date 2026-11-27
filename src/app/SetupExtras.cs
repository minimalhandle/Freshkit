// Freshkit - UI-free helpers: app dependencies, command-line recipes, setup estimates and winget/Chocolatey exports.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Freshkit
{
    /// Catalog lines "::REQUIRES app-key|needed-key,needed-key" inside ::BEGIN_REQUIRES ... ::END_REQUIRES.
    /// Keys use the selection format: choco id, winget:<Id> or github:<owner/repo>.
    internal static class AppDependencies
    {
        public static Dictionary<string, List<string>> Parse(string listText)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Match section = Regex.Match(listText ?? "", @"(?ms)^::BEGIN_REQUIRES\r?\n(?<body>.*?)^::END_REQUIRES\r?$");
            if (!section.Success) return result;
            foreach (string raw in Regex.Split(section.Groups["body"].Value, @"\r?\n"))
            {
                Match m = Regex.Match(raw, @"^::REQUIRES\s+(?<app>[^|]+)\|(?<needs>.+)$");
                if (!m.Success) continue;
                string app = m.Groups["app"].Value.Trim();
                List<string> needs;
                if (!result.TryGetValue(app, out needs)) { needs = new List<string>(); result[app] = needs; }
                foreach (string n in m.Groups["needs"].Value.Split(','))
                {
                    string key = n.Trim();
                    if (key.Length > 0 && !string.Equals(key, app, StringComparison.OrdinalIgnoreCase) && !needs.Exists(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase))) needs.Add(key);
                }
            }
            return result;
        }

        /// Apps the given app needs (directly or through other needed apps) that are in the catalog and not selected yet.
        public static List<string> Missing(string key, ICollection<string> selected, ICollection<string> catalog, Dictionary<string, List<string>> map)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key };
            var queue = new Queue<string>(); queue.Enqueue(key);
            while (queue.Count > 0)
            {
                List<string> needs;
                if (!map.TryGetValue(queue.Dequeue(), out needs)) continue;
                foreach (string n in needs)
                {
                    if (!seen.Add(n)) continue;
                    if (!Contains(catalog, n)) continue;
                    if (!Contains(selected, n)) result.Add(n);
                    queue.Enqueue(n);
                }
            }
            return result;
        }

        static bool Contains(ICollection<string> set, string key)
        {
            foreach (string s in set) if (string.Equals(s, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// Freshkit.exe Install|Plan|List <recipe.json>: the recipe's apps on its own catalog. Windows selections stay in the app.
    internal static class CliRecipe
    {
        public static bool Accepts(string mode, string[] args)
        {
            if (args == null || args.Length < 2) return false;
            foreach (string m in new[] { "Install", "Plan", "List" }) if (string.Equals(mode, m, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static SetupRecipe Load(string path, string baseline)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("Recipe not found: " + path);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The recipe exceeds 4 MB.");
            var recipe = SetupRecipe.Parse(File.ReadAllText(path), baseline);
            if (recipe.Apps.Count == 0) throw new InvalidDataException("The recipe selects no apps.");
            return recipe;
        }

        public static string Notice(SetupRecipe recipe)
        {
            bool windows = recipe.HasWindowsSelections && (recipe.Tweaks.Count > 0 || recipe.Remove.Count > 0 || recipe.Backups.Count > 0 || recipe.ForceEdge);
            return "Recipe: " + recipe.Apps.Count + " app" + (recipe.Apps.Count == 1 ? "" : "s") + "." +
                (windows ? " Windows tweaks, removals and backup selections in the recipe are not applied from the command line; import the recipe in the Freshkit window for those." : "");
        }
    }

    internal sealed class AppEstimate { public string Key, Name; public long Bytes; public double Minutes; public bool Measured; }

    /// Rough, offline setup estimates. Figures are typical sizes, not promises; measured sizes replace them when checked.
    internal static class SetupEstimate
    {
        const long MB = 1024L * 1024L;
        // Large installers whose size would otherwise skew the estimate badly (MB, minutes).
        static readonly Dictionary<string, double[]> Known = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget:Microsoft.VisualStudio.BuildTools", new double[] { 3000, 20 } },
            { "winget:Microsoft.VisualStudio.2022.BuildTools", new double[] { 3000, 20 } },
            { "winget:Microsoft.Office", new double[] { 3500, 15 } },
            { "winget:Docker.DockerDesktop", new double[] { 600, 6 } },
            { "winget:BlenderFoundation.Blender", new double[] { 350, 4 } },
            { "winget:Adobe.Acrobat.Reader.64-bit", new double[] { 450, 5 } },
            { "winget:TheDocumentFoundation.LibreOffice", new double[] { 350, 5 } },
            { "openoffice", new double[] { 180, 4 } },
            { "winget:EpicGames.EpicGamesLauncher", new double[] { 200, 4 } },
            { "winget:Apple.iTunes", new double[] { 250, 4 } },
            { "winget:Microsoft.Teams", new double[] { 150, 3 } },
            { "winget:Bambulab.Bambustudio", new double[] { 250, 3 } },
            { "winget:Ultimaker.Cura", new double[] { 400, 4 } },
            { "winget:GIMP.GIMP.3", new double[] { 330, 4 } },
            { "winget:KDE.Krita", new double[] { 200, 3 } },
            { "winget:Python.Python.2", new double[] { 20, 1 } },
            { "python3", new double[] { 30, 2 } },
            { "vcredist-all", new double[] { 150, 4 } },
            { "git.install", new double[] { 70, 2 } },
            { "nodejs-lts", new double[] { 30, 1.5 } },
            { "googleearthpro", new double[] { 120, 2 } },
            { "k-litecodecpack-standard", new double[] { 60, 1.5 } },
        };

        public static AppEstimate For(string key, string name)
        {
            double[] known;
            if (Known.TryGetValue(key, out known)) return new AppEstimate { Key = key, Name = name, Bytes = (long)(known[0] * MB), Minutes = known[1] };
            bool store = key.StartsWith("winget:", StringComparison.OrdinalIgnoreCase) && Data.IsStoreId(key.Substring(7));
            return new AppEstimate { Key = key, Name = name, Bytes = (store ? 150 : 100) * MB, Minutes = store ? 2 : 1.5 };
        }

        public static string Describe(List<AppEstimate> apps, bool chocolateyReady)
        {
            if (apps.Count == 0) return "";
            long bytes = 0; double minutes = chocolateyReady ? 1 : 3; int measured = 0;
            foreach (var a in apps) { bytes += a.Bytes; minutes += a.Minutes; if (a.Measured) measured++; }
            int low = Math.Max(1, (int)Math.Round(minutes * 0.7)), high = Math.Max(low + 1, (int)Math.Ceiling(minutes * 1.4));
            return "Estimate: about " + low + "–" + high + " min and " + Size(bytes) + " to download" +
                (measured > 0 ? " (" + measured + " of " + apps.Count + " sizes measured, the rest typical)." : " (typical sizes; actual time depends on your connection and each installer).");
        }

        public static string Size(long bytes)
        {
            if (bytes >= 1024L * MB) return (bytes / (1024.0 * MB)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
            return Math.Max(1, (int)Math.Round(bytes / (double)MB)) + " MB";
        }

        /// The x64 (or only) installer URL from "winget show" output, or null.
        public static string InstallerUrl(string wingetShow)
        {
            foreach (Match m in Regex.Matches(wingetShow ?? "", @"(?im)^\s*Installer Url:\s*(?<url>https://\S+)\s*$")) return m.Groups["url"].Value;
            return null;
        }
    }

    /// Selections as files other PCs can use without Freshkit: winget import JSON and a Chocolatey packages.config.
    internal sealed class PortableExport
    {
        public string WingetJson, ChocoConfig;
        public List<string> Winget = new List<string>(), Store = new List<string>(), Choco = new List<string>(), Skipped = new List<string>(), OptionsDropped = new List<string>();

        public static PortableExport Build(string listText, ICollection<string> selected, DateTime now)
        {
            var export = new PortableExport();
            var keys = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            var chocoXml = new StringBuilder();
            foreach (var p in ListFile.Parse(listText).Packages)
            {
                if (!keys.Contains(Data.KeyFor(p.Source, p.Id))) continue;
                if (p.IsGitHub) { export.Skipped.Add(p.Name + " (GitHub release)"); continue; }
                if (p.IsWinget)
                {
                    if (Data.IsStoreId(p.Id)) export.Store.Add(p.Id); else export.Winget.Add(p.Id);
                    if (!string.IsNullOrEmpty(p.Parameters)) export.OptionsDropped.Add(p.Name);
                    continue;
                }
                export.Choco.Add(p.Id);
                chocoXml.Append("  <package id=\"").Append(SecurityElement.Escape(p.Id)).Append('"');
                if (!string.IsNullOrEmpty(p.Parameters)) chocoXml.Append(" packageParameters=\"").Append(SecurityElement.Escape(p.Parameters)).Append('"');
                chocoXml.Append(" />\r\n");
            }
            var sources = new List<object>();
            if (export.Winget.Count > 0) sources.Add(Source(export.Winget, "https://cdn.winget.microsoft.com/cache", "Microsoft.Winget.Source_8wekyb3d8bbwe", "winget", "Microsoft.PreIndexed.Package"));
            if (export.Store.Count > 0) sources.Add(Source(export.Store, "https://storeedgefd.dsx.mp.microsoft.com/v9.0", "StoreEdgeFD", "msstore", "Microsoft.Rest"));
            if (sources.Count > 0)
            {
                var root = new Dictionary<string, object>();
                root["$schema"] = "https://aka.ms/winget-packages.schema.2.0.json";
                root["CreationDate"] = now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", System.Globalization.CultureInfo.InvariantCulture);
                root["Sources"] = sources;
                root["WinGetVersion"] = "1.6.0";
                export.WingetJson = new JavaScriptSerializer().Serialize(root);
            }
            if (export.Choco.Count > 0) export.ChocoConfig = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<packages>\r\n" + chocoXml + "</packages>\r\n";
            return export;
        }

        static Dictionary<string, object> Source(List<string> ids, string argument, string identifier, string name, string type)
        {
            var packages = new List<object>();
            foreach (string id in ids) packages.Add(new Dictionary<string, object> { { "PackageIdentifier", id } });
            return new Dictionary<string, object> {
                { "Packages", packages },
                { "SourceDetails", new Dictionary<string, object> { { "Argument", argument }, { "Identifier", identifier }, { "Name", name }, { "Type", type } } } };
        }
    }

    /// A recipe carries the catalog from when it was exported. Applying it adds built-in apps, dependencies and starter
    /// profiles that catalog lacks (apps unticked), so apps added to Freshkit later stay available.
    internal static class CatalogMerge
    {
        public static string AddMissing(string recipeList, string builtinList, out int added)
        {
            added = 0;
            if (string.IsNullOrEmpty(builtinList)) return recipeList;
            var recipe = ListFile.Parse(recipeList); var builtin = ListFile.Parse(builtinList);
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in recipe.Packages) { have.Add(Data.KeyFor(p.Source, p.Id)); families.Add(Family(p)); }
            var lines = new List<string>(Regex.Split(recipeList.TrimEnd('\r', '\n'), @"\r?\n"));
            var bySection = new Dictionary<string, List<string>>();
            foreach (var p in builtin.Packages)
            {
                if (have.Contains(Data.KeyFor(p.Source, p.Id)) || families.Contains(Family(p))) continue;
                string section = p.IsGitHub ? "GITHUB" : p.IsWinget ? "WINGET" : "PACKAGES";
                List<string> block;
                if (!bySection.TryGetValue(section, out block)) { block = new List<string>(); bySection[section] = block; }
                block.Add(p.Category != null ? "::GROUP " + p.Category : "::GROUP");
                block.Add("::DISABLED " + p.Id + "|" + p.Name + "|" + p.Parameters);
                added++;
            }
            foreach (var kv in bySection) InsertBeforeEnd(lines, kv.Key, kv.Value);
            // Dependencies and starter profiles the recipe's catalog doesn't define yet.
            var requires = new List<string>();
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string l in lines) { Match m = Regex.Match(l, @"^::REQUIRES\s+([^|]+)\|"); if (m.Success) existing.Add(m.Groups[1].Value.Trim()); }
            foreach (string l in Regex.Split(builtinList, @"\r?\n")) { Match m = Regex.Match(l, @"^::REQUIRES\s+([^|]+)\|"); if (m.Success && !existing.Contains(m.Groups[1].Value.Trim())) requires.Add(l.TrimEnd()); }
            if (requires.Count > 0) InsertBeforeEnd(lines, "REQUIRES", requires);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var s in recipe.Saved) names.Add(s.Name);
            var profiles = new List<string>();
            foreach (string l in Regex.Split(builtinList, @"\r?\n")) { Match m = Regex.Match(l, @"^::PROFILE\s+([^|]*)\|"); if (m.Success && !names.Contains(m.Groups[1].Value.Trim())) profiles.Add(l.TrimEnd()); }
            if (profiles.Count > 0) InsertBeforeEnd(lines, "PROFILES", profiles);
            string merged = string.Join("\r\n", lines.ToArray()) + "\r\n";
            ListFile.Parse(merged);
            return merged;
        }

        static string Family(PackageEntry p)
        {
            if (p.Source == "winget" && AppOptions.IsLocalizedFirefox(p.Id)) return "winget:firefox-family";
            return Data.KeyFor(p.Source, AppOptions.CanonicalVersionId(p.Source, p.Id));
        }

        static void InsertBeforeEnd(List<string> lines, string section, List<string> block)
        {
            int end = lines.FindIndex(l => l.TrimEnd() == "::END_" + section);
            if (end < 0) { lines.Add("::BEGIN_" + section); lines.AddRange(block); lines.Add("::END_" + section); return; }
            lines.InsertRange(end, block);
        }
    }

    internal static class RecipeGaps
    {
        /// Apps a comparison reported as Missing (never Unknown: incomplete evidence is not treated as missing).
        public static List<string> MissingApps(AuditReport report)
        {
            var result = new List<string>();
            if (report == null) return result;
            foreach (var row in report.Rows) if (row.Category == "Apps" && row.Status == "Missing") result.Add(row.Key);
            return result;
        }
    }
}
