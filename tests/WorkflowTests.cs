using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace Freshkit
{
    internal static class WorkflowTests
    {
        public static void Recipes(Action<bool, string> check)
        {
            bool custom; string baseline = Engine.CurrentListText(out custom);
            var recipe = new SetupRecipe { ListText = baseline, Apps = new List<string> { "winget:Mozilla.Firefox.en-GB", "github:minimalhandle/Afterclip" }, Tweaks = new List<string> { "DisableTelemetry" }, Remove = new List<string> { "Test.App" }, Backups = new List<string> { "afterclip" } };
            var roundtrip = SetupRecipe.Parse(recipe.Serialize(), baseline);
            check(roundtrip.Apps.Count == 2 && roundtrip.Apps.Contains("github:minimalhandle/Afterclip") && roundtrip.Tweaks[0] == "DisableTelemetry" && roundtrip.Backups[0] == "afterclip", "A recipe round trip preserves source identities, tweaks, removals and backup selections");
            string before = Engine.SessionList; bool invalid = false;
            try { SetupRecipe.Parse(recipe.Serialize().Replace("\"Version\":1", "\"Version\":99"), baseline); } catch (InvalidDataException) { invalid = true; }
            check(invalid && Engine.SessionList == before, "Unsupported recipe versions leave the session untouched");
            recipe.Apps.Add("winget:Missing.App"); invalid = false;
            try { SetupRecipe.Parse(recipe.Serialize(), baseline); } catch (InvalidDataException) { invalid = true; }
            check(invalid, "Recipe selections must exist in their validated catalog");
            var winget = SetupRecipe.Parse("{\"Sources\":[{\"SourceDetails\":{\"Name\":\"winget\"},\"Packages\":[{\"PackageIdentifier\":\"Example.Tool\",\"Version\":\"1.0\"}]}]}", baseline);
            check(winget.Apps.Count == 1 && winget.Apps[0] == "winget:Example.Tool" && winget.ListText.Contains("::WINGET Example.Tool|Example.Tool|") && !winget.HasWindowsSelections, "Winget app-only imports add package IDs and leave Windows selections unchanged");
            invalid = false;
            try { SetupRecipe.Parse("{\"Sources\":[{\"SourceDetails\":{\"Name\":\"untrusted\"},\"Packages\":[{\"PackageIdentifier\":\"Example.Tool\"}]}]}", baseline); } catch (InvalidDataException) { invalid = true; }
            check(invalid, "Winget imports reject unsupported package sources");
        }
        public static void Search(Action<bool, string> check)
        {
            bool partial = false;
            SearchCoordinator.Run(new Dictionary<string, Func<CancellationToken, List<SearchResult>>> {
                { "GitHub", delegate { throw new PartialSearchException(new List<SearchResult> { new SearchResult { Name = "Found" } }, "One release lookup failed"); } }
            }, 1000, CancellationToken.None, delegate(SearchBatch batch) { partial = batch.IsFinal && batch.Results.Count == 1 && batch.Errors.ContainsKey("GitHub"); });
            check(partial, "Partial source failures retain search results and report incompleteness");
            using (var release = new ManualResetEvent(false))
            {
                bool progressive = false, final = false;
                SearchCoordinator.Run(new Dictionary<string, Func<CancellationToken, List<SearchResult>>> {
                    { "fast", delegate { return new List<SearchResult> { new SearchResult { Name = "Fast" } }; } },
                    { "slow", delegate(CancellationToken token) { WaitHandle.WaitAny(new[] { release, token.WaitHandle }); token.ThrowIfCancellationRequested(); return new List<SearchResult>(); } }
                }, 3000, CancellationToken.None, delegate(SearchBatch batch) {
                    if (!batch.IsFinal && batch.Completed == 1) { progressive = batch.Results.Count == 1; release.Set(); }
                    if (batch.IsFinal) final = batch.Completed == 2;
                });
                check(progressive && final, "Fast search results publish before a slow source finishes");
            }
            using (var cancel = new CancellationTokenSource())
            using (var stopped = new ManualResetEvent(false))
            using (var started = new ManualResetEvent(false))
            {
                var worker = new Thread(delegate() { SearchCoordinator.Run(new Dictionary<string, Func<CancellationToken, List<SearchResult>>> {
                    { "blocked", delegate(CancellationToken token) { using (token.Register(delegate { stopped.Set(); })) { started.Set(); token.WaitHandle.WaitOne(); } token.ThrowIfCancellationRequested(); return null; } }
                }, 5000, cancel.Token, delegate { throw new Exception("Cancelled work published results"); }); });
                worker.Start(); bool active = started.WaitOne(2000); cancel.Cancel();
                check(worker.Join(2000) && active && stopped.WaitOne(0), "Closing a search cancels an active source and promptly stops its coordinator");
            }
            bool timedOut = false;
            SearchCoordinator.Run(new Dictionary<string, Func<CancellationToken, List<SearchResult>>> {
                { "slow", delegate(CancellationToken token) { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return null; } }
            }, 80, CancellationToken.None, delegate(SearchBatch batch) { if (batch.IsFinal) timedOut = batch.Errors.ContainsKey("slow"); });
            check(timedOut, "A common search deadline reports unfinished sources");
        }
        public static void Updates(Action<bool, string> check)
        {
            var mixed = UpdateCheck.Run(new Dictionary<string, Func<List<UpdateItem>>> { { "GitHub", delegate {
                throw new PartialUpdatesException(new List<UpdateItem> { new UpdateItem { Name = "Confirmed app", Source = "github" } }, "Another app needs identity confirmation"); } } });
            check(!mixed.Complete && mixed.Items.Count == 1, "One uncertain GitHub identity does not discard confirmed updates");
            var chocolatey = Sources.ParseChocolateyUpdates(0, "example|1.0|2.0|false\n");
            check(chocolatey.Count == 1 && chocolatey[0].Source == "choco", "Chocolatey update rows use the source key consumed by the merged UI");
            bool commandFailed = false;
            try { Sources.ParseChocolateyUpdates(-1, ""); } catch (Exception ex) { commandFailed = ex is TimeoutException; }
            check(commandFailed, "A timed-out Chocolatey check cannot appear as an empty successful check");
            var failed = UpdateCheck.Run(new Dictionary<string, Func<List<UpdateItem>>> {
                { "winget", delegate { throw new IOException("offline"); } },
                { "Chocolatey", delegate { return new List<UpdateItem>(); } } });
            check(!failed.Complete && failed.Status.Contains("incomplete") && failed.Status.Contains("winget") && !failed.Status.Contains("Everything is up to date"), "Zero results with a failed source reports an incomplete update check");
            var current = UpdateCheck.Run(new Dictionary<string, Func<List<UpdateItem>>> { { "winget", delegate { return new List<UpdateItem>(); } } });
            check(current.Complete && current.Status == "Everything is up to date.", "Only a fully successful check reports everything current");
            using (var both = new CountdownEvent(2))
            {
                Func<List<UpdateItem>> source = delegate { both.Signal(); if (!both.Wait(3000)) throw new Exception("Checks did not overlap"); return new List<UpdateItem> { new UpdateItem { Name = "App" } }; };
                var parallel = UpdateCheck.Run(new Dictionary<string, Func<List<UpdateItem>>> { { "one", source }, { "two", source } });
                check(parallel.Complete && parallel.Items.Count == 2, "Independent update checks overlap and retain all results");
            }
        }
        public static void Identity(Action<bool, string> check)
        {
            var entry = new InstalledRecord { RegistryPath = "LocalMachine|Registry64|Afterclip", Name = "Afterclip", Version = "1.4.0", Publisher = "Afterclip", InstallLocation = "C:\\Apps\\Afterclip", UninstallString = "uninstall.exe" };
            var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var snapshot = new InstalledSnapshot(new[] { entry }, links);
            check(!snapshot.Resolve("github", "minimalhandle/Afterclip", "Afterclip").Confirmed, "A same-name installed app is tentative even when its version matches a release");
            entry.Repository = "minimalhandle/Afterclip";
            check(snapshot.Resolve("github", "minimalhandle/Afterclip", "Afterclip").Confirmed && !snapshot.Resolve("github", "panzergen/AfterClip", "AfterClip").Confirmed, "Repository metadata identifies the correct project even when its installed version is old");
            entry.Repository = null; links[entry.Fingerprint] = "minimalhandle/Afterclip";
            check(snapshot.Resolve("github", "minimalhandle/Afterclip", "Afterclip").Confirmed && snapshot.Resolve("github", "panzergen/AfterClip", "Afterclip").Record == null, "An explicit registry association cannot identify an unrelated namesake");
            entry.UninstallString = "another-uninstaller.exe";
            check(!snapshot.Resolve("github", "minimalhandle/Afterclip", "Afterclip").Confirmed, "Changed registration metadata invalidates an old repository association");
            check(InstalledSnapshot.RepositoryFromUrl("https://github.com/minimalhandle/Afterclip/releases") == "minimalhandle/Afterclip" && InstalledSnapshot.RepositoryFromUrl("https://github.com.evil.test/minimalhandle/Afterclip") == null, "Repository evidence requires an actual GitHub URL");
            entry.PackageId = "Example.App";
            check(snapshot.Resolve("winget", "Example.App", "Renamed app").Confirmed, "Exact package identity survives a display-name change");
        }
    }
}
