using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Freshkit
{
    internal static class UiTests
    {
        static int checks;
        static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        static object Call(MainUi ui, string name, params object[] args)
        {
            MethodInfo method = typeof(MainUi).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null) throw new Exception("Missing UI behavior: " + name);
            return method.Invoke(ui, args);
        }
        static T Field<T>(MainUi ui, string name) { return (T)typeof(MainUi).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui); }
        static T Get<T>(MainUi ui, string name) where T : class { return ui.Window.FindName(name) as T; }
        static void SetField(MainUi ui, string name, object value) { typeof(MainUi).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ui, value); }
        // Assemble only the selection UI. The application constructor performs job recovery
        // against real user state, so it must not be used by synthetic UI checks.
        static MainUi Fixture()
        {
            var ui = (MainUi)FormatterServices.GetUninitializedObject(typeof(MainUi));
            SetField(ui, "Window", System.Windows.Markup.XamlReader.Parse(Engine.ReadResourceText("MainWindow.xaml")));
            foreach (string name in new[] { "actionControls", "groups", "apps", "picks", "installedKeys", "searchResults", "pickItems", "savedProfiles", "removal", "runItems", "setupItems", "updates", "settingsProfiles", "rail" })
            {
                var field = typeof(MainUi).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                field.SetValue(ui, Activator.CreateInstance(field.FieldType));
            }
            string[] pages = { "PageApps", "PageTweaks", "PageDrivers", "PageProgress", "PageUpdates", "PageSettings" };
            string[] navs = { "NavApps", "NavTweaks", "NavDrivers", "NavProgress", "NavUpdates", "NavSettings" };
            SetField(ui, "pages", pages); SetField(ui, "navs", navs); SetField(ui, "online", true); SetField(ui, "categoryFilter", "*");
            for (int i = 0; i < navs.Length; i++)
            {
                string page = pages[i]; Get<RadioButton>(ui, navs[i]).Checked += delegate { Call(ui, "ShowPage", page); };
            }
            Call(ui, "InitApps"); Call(ui, "InitTweaks"); Call(ui, "LoadList", false);
            Get<TextBlock>(ui, "SysWindows").Text = "Windows 11 · UI preview";
            Get<TextBlock>(ui, "SysChoco").Text = "Chocolatey";
            return ui;
        }
        static void Render(MainUi ui, string name, int width, int height)
        {
            var root = (FrameworkElement)ui.Window.Content;
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); root.UpdateLayout();
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); var bg = new DrawingVisual(); using (var dc = bg.RenderOpen()) dc.DrawRectangle(ui.Window.Background, null, new Rect(0, 0, width, height)); image.Render(bg); image.Render(root);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-previews"); Directory.CreateDirectory(folder);
            using (var stream = File.Create(Path.Combine(folder, name + ".png"))) png.Save(stream);
        }
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                IconTests.Run(Check);
                var ui = Fixture();
                // Catalog and Apps page structure
                var all = Field<List<AppItem>>(ui, "apps");
                Check(all.Count >= 160 && all.FindAll(a => a.IsSelected).Count == 41, "Built-in catalog includes the Ninite apps with the curated default selection");
                var sleek = all.Find(a => a.Id == "minimalhandle/SleekMeeter");
                Check(sleek != null && sleek.IsGitHub && sleek.IsSelected && sleek.Key == "github:minimalhandle/SleekMeeter" && sleek.EnginePackageId == "github-minimalhandle-sleekmeeter" && sleek.SourceLabel == "GitHub" && sleek.Category == "Media", "GitHub catalog entries parse with their own key, engine ID and category");
                Check(all.Find(a => a.Id == "minimalhandle/Afterclip").Artwork.Image != null, "Bundled logos load for GitHub entries");
                Check(Sources.PickInstallerAsset(new[] { "App-1.0-arm64.msi", "App-1.0-portable.zip", "App_1.0_x86-setup.exe", "App_1.0_x86_64-setup.exe", "App.exe.sig" }) == "App_1.0_x86_64-setup.exe" && Sources.PickInstallerAsset(new[] { "a-setup.exe", "a.msi" }) == "a.msi", "GitHub asset choice prefers MSI, then x64 setup, and skips ARM, 32-bit and portable builds");
                var ranked = Sources.Rank("sleekmeeter", new List<SearchResult> {
                    new SearchResult { Source = "choco", Id = "voicemeeter", Name = "Voicemeeter", Meta = "voicemeeter", Popularity = 900000 },
                    new SearchResult { Source = "github", Id = "minimalhandle/SleekMeeter", Name = "SleekMeeter", Meta = "minimalhandle/SleekMeeter", Popularity = 12 },
                    new SearchResult { Source = "winget", Id = "Example.SleekMeeter", Name = "Sleekmeeter", Meta = "Example.SleekMeeter" } });
                Check(ranked.Count == 3 && ranked[0].Source == "winget" && ranked[0].IsBest && !ranked[0].Meta.Contains("also on GitHub") && ranked[1].Source == "github" && ranked[2].Id == "voicemeeter", "Exact matches rank first and same-named GitHub projects stay separate rows");
                var twins = Sources.Rank("afterclip", new List<SearchResult> {
                    new SearchResult { Source = "github", Id = "someone/afterclip", Name = "afterclip-cli", Meta = "x", Popularity = 40 },
                    new SearchResult { Source = "github", Id = "minimalhandle/Afterclip", Name = "Afterclip", Meta = "y", Popularity = 3 },
                    new SearchResult { Source = "github", Id = "other/Afterclip", Name = "Afterclip", Meta = "z", Popularity = 1 } });
                Check(twins.Count == 3 && twins[0].Id == "minimalhandle/Afterclip", "An exact app name outranks a repository-name match, and same-source namesakes stay separate");
                var newest = Sources.Rank("icloud", new List<SearchResult> {
                    new SearchResult { Source = "choco", Id = "icloud", Name = "iCloud", Version = "7.21.0.23", Meta = "icloud", Popularity = 50000 },
                    new SearchResult { Source = "store", Id = "9PKTQ5699M62", Name = "iCloud", Version = "15.8.118.0", Meta = "9PKTQ5699M62" } });
                Check(newest.Count == 1 && newest[0].Source == "store" && newest[0].Key == "winget:9PKTQ5699M62" && newest[0].Meta.Contains("also on Chocolatey (v7.21.0.23)"), "The source with the newest release is shown, with the older source noted");
                var spotify = Sources.Rank("spotify", new List<SearchResult> { new SearchResult { Source = "store", Id = "9NCBCSZSJRSB", Name = "Spotify", Version = "1.2.75.510", Meta = "s" }, new SearchResult { Source = "choco", Id = "spotify", Name = "Spotify", Version = "1.2.75.510", Meta = "c" } });
                Check(spotify.Count == 1 && spotify[0].Source == "choco" && spotify[0].Meta.Contains("also on Microsoft Store"), "Same release on the Store and another source uses the regular installer");
                var gimp = Sources.Rank("gimp", new List<SearchResult> { new SearchResult { Source = "store", Id = "9PNSJCLXDZ0V", Name = "GIMP", Version = "3.2.80.0", Meta = "s" }, new SearchResult { Source = "choco", Id = "gimp", Name = "GIMP", Version = "3.2.8", Meta = "c" } });
                Check(gimp[0].Source == "choco", "A Store build-number difference alone does not make the Store preferred");
                var unknown = Sources.Rank("zoom", new List<SearchResult> { new SearchResult { Source = "store", Id = "XP99J3KP4XZ4VV", Name = "Zoom", Version = "", Meta = "s" }, new SearchResult { Source = "winget", Id = "Zoom.Zoom", Name = "Zoom", Version = "", Meta = "w" } });
                Check(unknown[0].Source == "winget", "With unknown versions the Store is not chosen over another source");
                var only = Sources.Rank("blip", new List<SearchResult> { new SearchResult { Source = "store", Id = "9N7JSXC1SJK6", Name = "Blip", Version = "1.2.8.0", Meta = "s" } });
                Check(only.Count == 1 && only[0].Source == "store", "A Store-only app is still offered");
                var lookalike = Sources.Rank("greenshot", new List<SearchResult> {
                    new SearchResult { Source = "choco", Id = "greenshot", Name = "Greenshot", Version = "1.3.328", Meta = "greenshot" },
                    new SearchResult { Source = "store", Id = "9N8Z6RQX8LV8", Name = "Greenshot (Unofficial)", Version = "1.13290.1.0", Meta = "9N8Z6RQX8LV8" } });
                Check(lookalike.Count == 2 && lookalike[0].Source == "choco", "An unofficial Store lookalike is not merged with, or preferred over, the real app");
                var variants = Sources.Rank("vlc", new List<SearchResult> { new SearchResult { Source = "choco", Id = "vlc", Name = "VLC media player", Version = "3.0.24", Meta = "vlc" }, new SearchResult { Source = "choco", Id = "vlc.install", Name = "VLC media player (Install)", Version = "3.0.24", Meta = "vlc.install" }, new SearchResult { Source = "choco", Id = "vlc.portable", Name = "VLC media player (Portable)", Version = "3.0.21", Meta = "vlc.portable" } });
                Check(variants.Count == 1, "Chocolatey install/portable variants of one package show as one app");
                Check(Data.CompareVersions("2026.1109.1228.0", "11.42.3") == null && Data.CompareVersions("15.10.39.0", "15.8.118.0") > 0 && Data.CompareVersions("v1.5.0", "1.5") == 0, "Versions compare numerically and date-style versions are not compared with semantic ones");
                Check(Data.IsStoreId("9NKSQGP7F2NH") && Data.IsStoreId("XPFFTQ037JWMHS") && !Data.IsStoreId("Microsoft.PowerToys"), "Both Microsoft Store ID formats are recognised");
                var whatsapp = all.Find(a => a.Name == "WhatsApp");
                Check(whatsapp != null && whatsapp.IsStore && whatsapp.Id == "9NKSQGP7F2NH" && whatsapp.Artwork.Image != null && all.Exists(a => a.Id == "9PKTQ5699M62") && all.Exists(a => a.Id == "9N7JSXC1SJK6"), "WhatsApp, iCloud and Blip come from the Microsoft Store with bundled logos");
                Check(all.TrueForAll(a => a.Artwork.Image != null || a.Id == "nonexistent"), "Every built-in catalog app has a bundled logo");
                Check(!Data.IsGitHubRepo("../etc") && !Data.IsGitHubRepo("owner/..") && Data.IsGitHubRepo("minimalhandle/Afterclip"), "GitHub repository IDs are validated");
                Check(all.Find(a => a.Id == "googlechrome").Category == "Browsers" && !all.Find(a => a.Id == "googlechrome").DefaultOn, "Catalog ::GROUP sections drive categories and new apps start unticked");
                Check(!all.Find(a => a.Id == "dotnet-7.0-desktopruntime").DefaultOn, "End-of-life .NET 7 runtime is not selected by default");
                var store = all.Find(a => a.Id == "9NKSQGP7F2NH");
                Check(store != null && store.IsWinget && store.SourceLabel == "Store", "Microsoft Store entries are labelled as Store apps");
                var rail = Field<ObservableCollection<CategoryFilter>>(ui, "rail");
                rail.First(c => c.Key == "Runtimes").IsChecked = true;
                Check(all.TrueForAll(a => (a.Visibility == Visibility.Visible) == (a.Category == "Runtimes")), "Picking a category shows only that category");
                Get<TextBox>(ui, "SearchBox").Text = "chrome";
                Check(Field<string>(ui, "categoryFilter") == "*" && all.Find(a => a.Id == "googlechrome").Visibility == Visibility.Visible, "Typing a search looks across every category");
                Get<TextBox>(ui, "SearchBox").Text = "";
                rail.First(c => c.Key == "selected").IsChecked = true;
                Check(all.TrueForAll(a => (a.Visibility == Visibility.Visible) == a.IsSelected), "Selected filter shows exactly the ticked apps");
                rail.First(c => c.Key == "*").IsChecked = true;
                var box = Get<ComboBox>(ui, "ProfileBox");
                int starter = -1; for (int i = 0; i < box.Items.Count; i++) if ((box.Items[i] as string ?? "").StartsWith("Starter: essentials")) starter = i;
                Check(starter > 1, "Starter profiles are offered in the profile picker");
                var tweaksBefore = Field<object>(ui, "debloat") == null ? 0 : ((System.Collections.ICollection)Field<object>(ui, "debloat").GetType().GetMethod("SelectedFeatures").Invoke(Field<object>(ui, "debloat"), null)).Count;
                box.SelectedIndex = starter;
                var tweaksAfter = Field<object>(ui, "debloat") == null ? 0 : ((System.Collections.ICollection)Field<object>(ui, "debloat").GetType().GetMethod("SelectedFeatures").Invoke(Field<object>(ui, "debloat"), null)).Count;
                Check(all.Find(a => a.Id == "sumatrapdf.install").IsSelected && !all.Find(a => a.Id == "steam").IsSelected && tweaksBefore == tweaksAfter, "Starter profile selects its apps and leaves Windows tweaks unchanged");
                box.SelectedIndex = 1;
                Check(all.FindAll(a => a.IsSelected).Count == 41, "List defaults restore the curated selection");
                ListFile.AddPackage("github", "octo-org/Tool.App", "Tool App");
                Call(ui, "LoadList", false);
                var added = Field<List<AppItem>>(ui, "apps").Find(x => x.Id == "octo-org/Tool.App");
                bool customList; string sessionText = Engine.CurrentListText(out customList);
                Check(added != null && added.IsGitHub && sessionText.Contains("::GITHUB octo-org/Tool.App|Tool App|") && !ListFile.AddPackage("github", "octo-org/Tool.App", "Tool App"), "GitHub search results are added once to the session list");
                ListFile.RemovePackage("github", "octo-org/Tool.App");
                Check(!Engine.CurrentListText(out customList).Contains("octo-org/Tool.App"), "GitHub entries can be removed from the session list");
                Engine.SessionList = null; Call(ui, "LoadList", false);

                Call(ui, "OpenSearch");
                int old = (int)Call(ui, "BeginSearch", "old", "choco");
                int latest = (int)Call(ui, "BeginSearch", "new", "winget");
                var results = new List<SearchResult> { new SearchResult { Id = "Example.App", Name = "Example", Source = "winget", Meta = "Example.App", Summary = "An app" } };
                Call(ui, "CompleteSearch", latest, "new", "winget", results, null, "");
                Call(ui, "CompleteSearch", old, "old", "choco", new List<SearchResult>(), "stale error", "");
                Check(Field<ObservableCollection<SearchResult>>(ui, "searchResults").Count == 1 && Get<TextBlock>(ui, "SheetStatus").Text.Contains("new"), "Older search cannot overwrite newer results or status");
                int closed = (int)Call(ui, "BeginSearch", "closing", "choco");
                Call(ui, "CloseSearch");
                Call(ui, "CompleteSearch", closed, "closing", "choco", results, null, "");
                Check(Get<Grid>(ui, "SearchSheet").Visibility == Visibility.Collapsed && Field<ObservableCollection<SearchResult>>(ui, "searchResults").Count == 0, "Completion after close cannot populate a closed sheet");
                Call(ui, "OpenSearch");
                int empty = (int)Call(ui, "BeginSearch", "nothing", "choco");
                Call(ui, "CompleteSearch", empty, "nothing", "choco", new List<SearchResult>(), null, "");
                Check(Get<StackPanel>(ui, "SearchState").Visibility == Visibility.Visible && Get<Button>(ui, "BtnSearchRetry").Visibility == Visibility.Collapsed, "No results has a visible explanatory state");
                int failed = (int)Call(ui, "BeginSearch", "failed", "choco");
                Call(ui, "CompleteSearch", failed, "failed", "choco", null, "Connection unavailable", "");
                Check(Get<Button>(ui, "BtnSearchRetry").Visibility == Visibility.Visible && Get<TextBlock>(ui, "SearchStateDetail").Text.Contains("Connection unavailable"), "Failed search offers retry and explains the error");
                Call(ui, "CloseSearch");

                var apps = Field<List<AppItem>>(ui, "apps");
                foreach (AppItem a in apps) a.IsSelected = false;
                AppItem first = apps[0]; first.IsSelected = true;
                var debloat = Field<Debloat>(ui, "debloat");
                Check(debloat != null, "Embedded tweak data is available");
                debloat.ApplyFeatures(new string[0]); debloat.ApplyApps(new string[0]);
                Get<CheckBox>(ui, "ForceEdgeSwitch").IsChecked = false;
                Call(ui, "OpenInstallPlan");
                Check(Get<Grid>(ui, "ReviewSheet").Visibility == Visibility.Visible && !Field<bool>(ui, "running"), "Opening review does not start setup");
                Check(!Get<RadioButton>(ui, "NavApps").IsEnabled && !Get<TextBox>(ui, "SearchBox").IsEnabled, "Review prevents keyboard editing of selections behind the sheet");
                Check(Get<TextBlock>(ui, "ReviewAppsDetail").Text.Contains(first.Name) && Get<TextBlock>(ui, "ReviewAppsDetail").Text.Contains(first.Id), "Review lists selected app names and package IDs");
                Get<CheckBox>(ui, "ReviewDrivers").IsChecked = false;
                Get<CheckBox>(ui, "ReviewUpgrade").IsChecked = true;
                var spec = (JobSpec)Call(ui, "BuildReviewedJob");
                Check(spec.Apps == first.Key && spec.SkipDrivers && spec.Upgrade && spec.DebloatArgs == null, "Reviewed options map to the actual setup job");
                string fixtures = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-fixtures-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(fixtures, "Drivers")); Directory.CreateDirectory(Path.Combine(fixtures, "WiFi"));
                try
                {
                    File.WriteAllText(Path.Combine(fixtures, "Drivers", "network.inf"), "synthetic");
                    File.WriteAllText(Path.Combine(fixtures, "WiFi", "network.xml"), "synthetic");
                    string zip = Path.Combine(fixtures, "settings.zip"); File.WriteAllText(zip, "synthetic path only; never restored");
                    Engine.KitDir = fixtures;
                    Get<CheckBox>(ui, "ReviewKit").IsChecked = true;
                    Get<CheckBox>(ui, "ReviewRestore").Tag = zip;
                    Get<CheckBox>(ui, "ReviewRestore").IsChecked = true;
                    Get<CheckBox>(ui, "ReviewAllowMismatch").IsChecked = true;
                    spec = (JobSpec)Call(ui, "BuildReviewedJob");
                    Check(spec.DriverKit == Path.Combine(fixtures, "Drivers") && spec.WifiKit == Path.Combine(fixtures, "WiFi") && spec.RestoreZip == zip && spec.AllowMismatch, "Enabled kit and restore options reach JobSpec");
                    Get<CheckBox>(ui, "ReviewKit").IsChecked = false;
                    Get<CheckBox>(ui, "ReviewRestore").IsChecked = false;
                    spec = (JobSpec)Call(ui, "BuildReviewedJob");
                    Check(spec.DriverKit == null && spec.WifiKit == null && spec.RestoreZip == null && !spec.AllowMismatch, "Disabled kit and restore are omitted from JobSpec");
                    Get<CheckBox>(ui, "ReviewRestore").IsChecked = true;
                    File.Delete(zip);
                    Check(Call(ui, "BuildReviewedJob") == null && Get<TextBlock>(ui, "ReviewValidation").Text.Length > 0, "A removed backup blocks setup with an explanation");
                    Get<CheckBox>(ui, "ReviewRestore").IsChecked = false;
                }
                finally { Engine.KitDir = ""; Directory.Delete(fixtures, true); }
                Call(ui, "CloseReview");
                Check(!Field<bool>(ui, "running"), "Cancelling review leaves setup idle");
                Check(Get<RadioButton>(ui, "NavApps").IsEnabled && Get<TextBox>(ui, "SearchBox").IsEnabled, "Closing review restores page interaction");
                first.IsSelected = false;
                Get<CheckBox>(ui, "ForceEdgeSwitch").IsChecked = true;
                Call(ui, "OpenInstallPlan");
                Check(Get<TextBlock>(ui, "ReviewTweaksDetail").Text.Contains("Microsoft Edge"), "Force Edge removal is explicit in review");
                Get<CheckBox>(ui, "ReviewTweaks").IsChecked = false;
                Check(Call(ui, "BuildReviewedJob") == null, "All sections disabled produces no runnable job");
                Get<CheckBox>(ui, "ReviewTweaks").IsChecked = true;
                spec = (JobSpec)Call(ui, "BuildReviewedJob");
                Check(spec.Action == null && spec.DebloatArgs != null, "Tweaks-only setup does not add app or driver installation");
                Get<CheckBox>(ui, "ReviewRestorePoint").IsChecked = false;
                spec = (JobSpec)Call(ui, "BuildReviewedJob");
                Check(!spec.DebloatArgs.Contains("-CreateRestorePoint"), "Review restore-point option is used by the job");
                Get<Button>(ui, "BtnReviewTweaks").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Get<Grid>(ui, "ReviewSheet").Visibility == Visibility.Collapsed && Get<Grid>(ui, "PageTweaks").Visibility == Visibility.Visible && !Field<bool>(ui, "running"), "Edit tweaks returns to selections without running setup");
                Call(ui, "CloseReview");
                if (args.Length > 0 && args[0] == "--render")
                {
                    foreach (AppItem a in apps) a.IsSelected = a.DefaultOn;
                    apps[0].Installed = true;
                    Field<ObservableCollection<AppGroup>>(ui, "groups")[0].Items[0].Installed = true;
                    Call(ui, "Navigate", "NavApps");
                    Render(ui, "apps", 1264, 801);
                    Render(ui, "apps-small", 1064, 641);
                    Call(ui, "OpenSearch"); Render(ui, "search-empty", 1264, 801);
                    int request = (int)Call(ui, "BeginSearch", "example", "winget");
                    Render(ui, "search-loading", 1064, 641);
                    var mixed = Sources.Rank("afterclip", new List<SearchResult> {
                        new SearchResult { Source = "github", Id = "minimalhandle/Afterclip", Name = "Afterclip", Version = "1.5.0", Popularity = 3, Meta = "minimalhandle/Afterclip  ·  v1.5.0  ·  3 stars  ·  Afterclip_1.5.0_x64-setup.exe", Summary = "A lightweight game clipper for Windows, with hotkeys and Stream Deck support" },
                        new SearchResult { Source = "choco", Id = "obs-studio", Name = "OBS Studio", Popularity = 2000000, Meta = "obs-studio  ·  v31.0  ·  2M downloads", Summary = "Free and open source software for video recording and live streaming" },
                        new SearchResult { Source = "store", Id = "9PKTQ5699M62", Name = "iCloud", Version = "15.8.118.0", Meta = "9PKTQ5699M62  ·  v15.8.118.0  ·  Apple Inc.  ·  also on Chocolatey (v7.21.0.23)", Summary = "Access photos, files and passwords from your Apple devices" },
                        new SearchResult { Source = "github", Id = "tamnguyenvan/afterclip", Name = "afterclip-cli", Popularity = 40, Meta = "tamnguyenvan/afterclip  ·  v0.3  ·  40 stars  ·  afterclip-setup.exe", Summary = "Another project" } });
                    Call(ui, "CompleteSearch", request, "afterclip", "all", mixed, null, "");
                    Render(ui, "search-results", 1064, 641);
                    Call(ui, "CloseSearch"); Call(ui, "OpenInstallPlan");
                    Render(ui, "review", 1264, 801); Render(ui, "review-small", 1064, 641);
                    var root = (FrameworkElement)ui.Window.Content;
                    var button = Get<Button>(ui, "BtnReviewStart");
                    Point origin = button.TranslatePoint(new Point(), root);
                    Check(origin.Y >= 0 && origin.Y + button.ActualHeight <= 641 && Get<ScrollViewer>(ui, "ReviewScroll").ScrollableHeight > 0, "Minimum window keeps start action visible while review scrolls");
                    Get<ScrollViewer>(ui, "ReviewScroll").ScrollToBottom(); Render(ui, "review-bottom-small", 1064, 641);
                }
                ui.Window.Close();
                Console.WriteLine(checks + " UI checks passed");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
