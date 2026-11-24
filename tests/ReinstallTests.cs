using System;
using System.Collections.Generic;
using System.IO;
namespace Freshkit
{
    internal static class ReinstallTests
    {
        public static void Run(Action<bool, string> check)
        {
            // Manifest round trip and validation.
            var manifest = new ReinstallManifest { CreatedUtc = "2026-11-24T00:00:00Z", SettingsBackup = "settings-backup-20261124-120000.zip" };
            manifest.SteamLibraries.Add(@"D:\SteamLibrary");
            var parsed = ReinstallManifest.Parse(manifest.Serialize());
            check(parsed.Recipe == "setup.freshkit.json" && parsed.SettingsBackup == manifest.SettingsBackup && parsed.SteamLibraries.Count == 1 && parsed.SteamLibraries[0] == @"D:\SteamLibrary", "Reinstall manifest round-trips its recipe, backup and Steam libraries");
            Func<string, bool> rejects = delegate (string text) { try { ReinstallManifest.Parse(text); return false; } catch (InvalidDataException) { return true; } };
            check(rejects("{\"Format\":\"Other\",\"Version\":1,\"Recipe\":\"setup.freshkit.json\"}"), "Unknown manifest formats are rejected");
            check(rejects("{\"Format\":\"FreshkitReinstall\",\"Version\":1,\"Recipe\":\"..\\\\outside.json\"}"), "Manifest recipe paths can't leave the folder");
            check(rejects("{\"Format\":\"FreshkitReinstall\",\"Version\":1,\"Recipe\":\"C:\\\\Windows\\\\setup.json\"}"), "Manifest recipe paths can't be absolute");
            check(rejects("{\"Format\":\"FreshkitReinstall\",\"Version\":1,\"Recipe\":\"setup.freshkit.json\",\"SettingsBackup\":\"sub\\\\backup.zip\"}"), "Manifest backups must be a ZIP in the same folder");
            check(rejects("{\"Format\":\"FreshkitReinstall\",\"Version\":1,\"Recipe\":\"setup.freshkit.json\",\"SteamLibraries\":[\"relative\\\\path\"]}"), "Steam libraries must be absolute folders");
            check(rejects("{\"Format\":\"FreshkitReinstall\",\"Version\":2,\"Recipe\":\"setup.freshkit.json\"}"), "Future manifest versions are rejected");

            // Steam's libraryfolders.vdf.
            string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"228980\"\t\t\"1\"\n\t\t}\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t\t\"apps\"\n\t\t{\n\t\t}\n\t}\n\t\"2\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Games\\\\Steam\"\n\t}\n}\n";
            var paths = SteamLibraries.ReadPaths(vdf);
            check(paths.Count == 3 && paths[1] == @"D:\SteamLibrary", "Steam library paths are read and unescaped");
            var portable = SteamLibraries.Portable(paths, @"C:\Program Files (x86)\Steam", @"C:\Windows\system32");
            check(portable.Count == 1 && portable[0] == @"D:\SteamLibrary", "Only libraries off the Windows drive are recorded");
            int added;
            string merged = SteamLibraries.Merge(vdf, @"C:\Program Files (x86)\Steam", new[] { @"d:\steamlibrary\", @"E:\Games\Steam" }, out added);
            check(added == 1 && merged.Contains("\"3\"") && merged.Contains("\"E:\\\\Games\\\\Steam\"") && merged.Contains("\"228980\""), "Merging adds only missing libraries with the next index and keeps existing entries");
            check(SteamLibraries.ReadPaths(merged).Count == 4 && merged.TrimEnd().EndsWith("}"), "Merged file stays readable");
            string again = SteamLibraries.Merge(merged, @"C:\Program Files (x86)\Steam", new[] { @"E:\Games\Steam" }, out added);
            check(added == 0 && again == merged, "Merging is idempotent");
            string created = SteamLibraries.Merge("", @"C:\Program Files (x86)\Steam", new[] { @"D:\SteamLibrary" }, out added);
            var createdPaths = SteamLibraries.ReadPaths(created);
            check(added == 1 && createdPaths.Count == 2 && createdPaths[0] == @"C:\Program Files (x86)\Steam", "A fresh Steam install gets a new library file with its own folder first");
            bool badFormat = false;
            try { SteamLibraries.Merge("not a vdf", null, new[] { @"D:\SteamLibrary" }, out added); } catch (InvalidDataException) { badFormat = true; }
            check(badFormat, "Unexpected library files are left alone");

            // Applying to an installed Steam is never fatal.
            string message = SteamLibraries.ApplyToInstalledSteam(new List<string> { @"D:\SteamLibrary" }, delegate (string name) { return true; });
            check(message != null && message.Contains("Steam") && message.Contains("Settings > Storage"), "Steam library problems produce manual instructions instead of failing");
            check(SteamLibraries.ApplyToInstalledSteam(new List<string>(), delegate (string name) { return false; }) == null, "Nothing to add produces no message");
        }
    }
}
