// Freshkit - reinstall folder: a recipe, a settings backup and the Steam libraries that survive a Windows reinstall.
// No UI code; the menu actions live in MainUi.Reinstall.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Freshkit
{
    internal sealed class ReinstallManifest
    {
        public const string FileName = "freshkit-reinstall.json", RecipeFile = "setup.freshkit.json";
        public string Format = "FreshkitReinstall";
        public int Version = 1;
        public string CreatedUtc = "", Recipe = RecipeFile, SettingsBackup = "";
        public List<string> SteamLibraries = new List<string>();

        public string Serialize() { return new JavaScriptSerializer().Serialize(this); }

        public static ReinstallManifest Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 1024 * 1024) throw new InvalidDataException("The reinstall manifest is empty or exceeds 1 MB.");
            object json;
            try { json = new JavaScriptSerializer().DeserializeObject(text); }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid reinstall manifest: " + ex.Message, ex); }
            catch (InvalidOperationException ex) { throw new InvalidDataException("Invalid reinstall manifest: " + ex.Message, ex); }
            if (!(json is Dictionary<string, object>) || J.S(json, "Format") != "FreshkitReinstall" || !(J.Get(json, "Version") is int) || (int)J.Get(json, "Version") != 1)
                throw new InvalidDataException("This folder's freshkit-reinstall.json isn't a supported Freshkit reinstall manifest.");
            var m = new ReinstallManifest { CreatedUtc = J.S(json, "CreatedUtc"), Recipe = J.S(json, "Recipe"), SettingsBackup = J.S(json, "SettingsBackup") };
            if (!PlainFileName(m.Recipe) || !m.Recipe.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The manifest's recipe must be a .json file in the same folder.");
            if (m.SettingsBackup.Length > 0 && (!PlainFileName(m.SettingsBackup) || !m.SettingsBackup.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("The manifest's settings backup must be a .zip file in the same folder.");
            object libraries = J.Get(json, "SteamLibraries");
            if (libraries != null)
            {
                var list = libraries as object[];
                if (list == null || list.Length > 64) throw new InvalidDataException("Invalid Steam library list.");
                foreach (object item in list)
                {
                    string path = item as string;
                    if (!Freshkit.SteamLibraries.ValidPath(path)) throw new InvalidDataException("Invalid Steam library folder: " + path);
                    if (!m.SteamLibraries.Exists(p => Freshkit.SteamLibraries.SamePath(p, path))) m.SteamLibraries.Add(path);
                }
            }
            return m;
        }

        /// A bare file name inside the reinstall folder: no directories, drive letters or parent references.
        public static bool PlainFileName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length <= 200 && name.Trim() == name && name != "." && name != ".." &&
                name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name == Path.GetFileName(name);
        }

        /// True when the folder is on the drive Windows runs from (a reinstall normally wipes it).
        public static bool OnWindowsDrive(string folder)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(folder)), system = Path.GetPathRoot(Environment.SystemDirectory);
            return string.Equals(root, system, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class SteamLibraries
    {
        public static bool ValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 260 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.IndexOf('"') >= 0) return false;
            try { return Path.IsPathRooted(path) && Path.GetPathRoot(path).Length >= 3 && !Regex.IsMatch(path, @"(^|[\\/])\.\.?([\\/]|$)"); }
            catch (ArgumentException) { return false; }
        }

        public static string Normalize(string path)
        {
            return Path.GetFullPath(path.Replace('/', '\\')).TrimEnd('\\');
        }

        public static bool SamePath(string a, string b)
        {
            try { return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        /// Library paths listed in Steam's libraryfolders.vdf (VDF escapes backslashes).
        public static List<string> ReadPaths(string vdf)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(vdf)) return list;
            foreach (Match m in Regex.Matches(vdf, "(?im)^\\s*\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string path = Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1");
                if (ValidPath(path) && !list.Exists(p => SamePath(p, path))) list.Add(path);
            }
            return list;
        }

        /// Libraries worth recording before a reinstall: not Steam's own folder and not on the Windows drive.
        public static List<string> Portable(IEnumerable<string> paths, string steamRoot, string windowsDrive)
        {
            var list = new List<string>();
            foreach (string path in paths)
            {
                if (!ValidPath(path)) continue;
                if (!string.IsNullOrEmpty(steamRoot) && SamePath(path, steamRoot)) continue;
                if (!string.IsNullOrEmpty(windowsDrive) && string.Equals(Path.GetPathRoot(Normalize(path)), Path.GetPathRoot(windowsDrive), StringComparison.OrdinalIgnoreCase)) continue;
                string normal = Normalize(path);
                if (!list.Exists(p => SamePath(p, normal))) list.Add(normal);
            }
            return list;
        }

        static string Quote(string value) { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        static string Entry(int index, string path)
        {
            return "\t" + Quote(index.ToString()) + "\n\t{\n\t\t\"path\"\t\t" + Quote(path) + "\n\t\t\"label\"\t\t\"\"\n\t\t\"contentid\"\t\t\"0\"\n\t\t\"totalsize\"\t\t\"0\"\n\t\t\"apps\"\n\t\t{\n\t\t}\n\t}\n";
        }

        /// Adds missing libraries to libraryfolders.vdf text, or creates the file's text when there is none.
        public static string Merge(string vdf, string steamRoot, IEnumerable<string> libraries, out int added)
        {
            added = 0;
            var existing = ReadPaths(vdf);
            var additions = new List<string>();
            foreach (string path in libraries)
                if (ValidPath(path) && !existing.Exists(p => SamePath(p, path)) && !additions.Exists(p => SamePath(p, path)) && !(steamRoot != null && SamePath(path, steamRoot))) additions.Add(Normalize(path));
            if (string.IsNullOrWhiteSpace(vdf))
            {
                var text = new StringBuilder("\"libraryfolders\"\n{\n");
                int i = 0;
                if (!string.IsNullOrEmpty(steamRoot)) text.Append(Entry(i++, Normalize(steamRoot)));
                foreach (string path in additions) { text.Append(Entry(i++, path)); added++; }
                return text.Append("}\n").ToString();
            }
            if (additions.Count == 0) return vdf;
            int close = vdf.LastIndexOf('}');
            if (close < 0 || !Regex.IsMatch(vdf, "^\\s*\"libraryfolders\"", RegexOptions.IgnoreCase)) throw new InvalidDataException("Steam's libraryfolders.vdf has an unexpected format.");
            int next = 0;
            foreach (Match m in Regex.Matches(vdf, "(?m)^\\s*\"(\\d+)\"\\s*$")) next = Math.Max(next, int.Parse(m.Groups[1].Value) + 1);
            var insert = new StringBuilder();
            foreach (string path in additions) { insert.Append(Entry(next++, path)); added++; }
            string before = vdf.Substring(0, close);
            if (!before.EndsWith("\n")) before += "\n";
            return before + insert + vdf.Substring(close);
        }

        /// Steam's install folder from the current user's registry (SteamPath uses forward slashes).
        public static string InstalledRoot()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string path = key == null ? null : key.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(path) && ValidPath(path.Replace('/', '\\')) && Directory.Exists(path)) return Normalize(path);
                }
            }
            catch (Exception) { }
            string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            return File.Exists(Path.Combine(standard, "steam.exe")) ? standard : null;
        }

        /// Libraries to record in a reinstall folder.
        public static List<string> CollectPortable()
        {
            string root = InstalledRoot();
            if (root == null) return new List<string>();
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            string text = File.Exists(vdf) ? File.ReadAllText(vdf) : "";
            return Portable(ReadPaths(text), root, Environment.SystemDirectory);
        }

        /// Adds the saved libraries that still exist to the installed Steam. Never throws; returns a message for the log.
        public static string ApplyToInstalledSteam(List<string> libraries, Func<string, bool> steamRunning)
        {
            if (libraries == null || libraries.Count == 0) return null;
            string manual = " Add them in Steam: Settings > Storage > Add drive: " + string.Join(", ", libraries.ToArray()) + ".";
            try
            {
                string root = InstalledRoot();
                if (root == null) return "Steam libraries weren't added because Steam isn't installed." + manual;
                if (steamRunning("steam")) return "Steam libraries weren't added because Steam is running. Exit Steam, then add them yourself." + manual;
                var present = libraries.FindAll(p => Directory.Exists(Path.Combine(p, "steamapps")));
                if (present.Count == 0) return "None of the saved Steam library folders were found on this PC." + manual;
                string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string before = File.Exists(file) ? File.ReadAllText(file) : "";
                int added;
                string after = Merge(before, root, present, out added);
                if (added == 0) return "Steam already lists your saved library folders.";
                if (before.Length > 0) File.Copy(file, file + ".freshkit-backup", true);
                File.WriteAllText(file, after, new UTF8Encoding(false));
                string missing = present.Count < libraries.Count ? " Not found (skipped): " + string.Join(", ", libraries.FindAll(p => !present.Contains(p)).ToArray()) + "." : "";
                return "Added " + added + " Steam library folder" + (added == 1 ? "" : "s") + ". Steam finds their installed games the next time it starts." + missing;
            }
            catch (Exception ex) { return "Steam libraries weren't added: " + ex.Message + "." + manual; }
        }
    }
}
