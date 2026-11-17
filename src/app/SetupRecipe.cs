using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Freshkit
{
    internal sealed class SetupRecipe
    {
        public string Format = "FreshkitRecipe";
        public int Version = 1;
        public string ListText;
        public List<string> Apps = new List<string>(), Tweaks = new List<string>(), Remove = new List<string>(), Backups = new List<string>();
        public bool HasWindowsSelections = true, RestorePoint, ForceEdge;

        public string Serialize() { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(this); }

        public static SetupRecipe Parse(string text, string baseline)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 4 * 1024 * 1024) throw new InvalidDataException("The recipe is empty or exceeds 4 MB.");
            try
            {
                var json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.DeserializeObject(text) as Dictionary<string, object>;
                if (json == null) throw new InvalidDataException("Expected a recipe JSON object.");
                if (!json.ContainsKey("Format") && json.ContainsKey("Sources")) return FromWinget(json, baseline);
                if (J.S(json, "Format") != "FreshkitRecipe" || !(J.Get(json, "Version") is int) || (int)J.Get(json, "Version") != 1) throw new InvalidDataException("Unsupported recipe format or version.");
                var recipe = new SetupRecipe { ListText = J.Get(json, "ListText") as string, Apps = Strings(json, "Apps"), Tweaks = Strings(json, "Tweaks"), Remove = Strings(json, "Remove"), Backups = Strings(json, "Backups"),
                    HasWindowsSelections = Bool(json, "HasWindowsSelections"), RestorePoint = Bool(json, "RestorePoint"), ForceEdge = Bool(json, "ForceEdge") };
                recipe.Validate(); return recipe;
            }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid recipe JSON: " + ex.Message, ex); }
            catch (InvalidOperationException ex) { throw new InvalidDataException("Invalid recipe JSON: " + ex.Message, ex); }
        }

        static bool Bool(object json, string name)
        {
            object value = J.Get(json, name);
            if (!(value is bool)) throw new InvalidDataException("Missing or invalid " + name + ".");
            return (bool)value;
        }

        static List<string> Strings(object json, string name)
        {
            var array = J.Get(json, name) as object[];
            if (array == null || array.Length > 2000) throw new InvalidDataException("Missing or invalid " + name + " selection.");
            var result = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in array)
            {
                var value = item as string;
                if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || Regex.IsMatch(value, @"[\r\n|]") || !seen.Add(value)) throw new InvalidDataException("Invalid or duplicate " + name + " entry.");
                result.Add(value);
            }
            return result;
        }

        void Validate()
        {
            if (string.IsNullOrEmpty(ListText) || ListText.Length > 2 * 1024 * 1024) throw new InvalidDataException("Missing or oversized recipe catalog.");
            var catalog = ListFile.Parse(ListText); var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in catalog.Packages) keys.Add(Data.KeyFor(p.IsGitHub ? "github" : p.IsWinget ? "winget" : "choco", p.Id));
            foreach (string key in Apps) if (!keys.Contains(key)) throw new InvalidDataException("Selected app is missing from the recipe catalog: " + key);
            var backupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in BackupCatalog.Load(ListText)) backupIds.Add(p.Id);
            foreach (string id in Backups) if (!backupIds.Contains(id)) throw new InvalidDataException("Unknown backup profile: " + id);
        }

        static SetupRecipe FromWinget(object json, string baseline)
        {
            var recipe = new SetupRecipe { ListText = baseline, HasWindowsSelections = false };
            var sources = J.Get(json, "Sources") as object[];
            if (sources == null || sources.Length == 0 || sources.Length > 20) throw new InvalidDataException("Invalid winget sources.");
            var catalog = ListFile.Parse(baseline); var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in catalog.Packages) if (p.IsWinget) known.Add(p.Id);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object source in sources)
            {
                string name = J.S(J.Get(source, "SourceDetails"), "Name");
                if (name != "winget" && name != "msstore") throw new InvalidDataException("Only the built-in winget and Microsoft Store sources can be imported.");
                var packages = J.Get(source, "Packages") as object[];
                if (packages == null || packages.Length > 2000) throw new InvalidDataException("Invalid winget package list.");
                foreach (object package in packages)
                {
                    string id = J.S(package, "PackageIdentifier");
                    if (!Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9.+_-]{0,199}\z") || (name == "msstore") != Data.IsStoreId(id)) throw new InvalidDataException("Invalid package identifier for " + name + ": " + id);
                    if (selected.Add(id)) recipe.Apps.Add("winget:" + id);
                    if (known.Add(id))
                    {
                        int begin = recipe.ListText.IndexOf("::BEGIN_WINGET", StringComparison.Ordinal);
                        if (begin < 0) { recipe.ListText += "\r\n::BEGIN_WINGET\r\n::END_WINGET\r\n"; begin = recipe.ListText.IndexOf("::BEGIN_WINGET", StringComparison.Ordinal); }
                        int at = recipe.ListText.IndexOf('\n', begin) + 1;
                        recipe.ListText = recipe.ListText.Insert(at, "::WINGET " + id + "|" + id + "|\r\n");
                    }
                }
            }
            recipe.Validate(); return recipe;
        }
    }
}
