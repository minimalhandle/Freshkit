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
        static Button FindButton(DependencyObject root, string tag)
        {
            var button = root as Button; if (button != null && tag.Equals(button.Tag)) return button;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = FindButton(VisualTreeHelper.GetChild(root, i), tag); if (found != null) return found; }
            return null;
        }
        static T FindNamed<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            var element = root as T; if (element != null && element.Name == name) return element;
            foreach (object child in LogicalTreeHelper.GetChildren(root)) { var next = child as DependencyObject; if (next != null) { var result = FindNamed<T>(next, name); if (result != null) return result; } }
            return null;
        }
        // Assemble only the selection UI. The application constructor performs job recovery
        // against real user state, so it must not be used by synthetic UI checks.
        static MainUi Fixture()
        {
            var ui = (MainUi)FormatterServices.GetUninitializedObject(typeof(MainUi));
            SetField(ui, "Window", System.Windows.Markup.XamlReader.Parse(Engine.ReadResourceText("MainWindow.xaml")));
            var preflight = new PreflightSnapshot { SystemFreeBytes = 100L * 1073741824, TempFreeBytes = 100L * 1073741824, RestartPending = false, WingetAvailable = true, RunningProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
            foreach (string source in new[] { "Chocolatey", "winget", "Store", "GitHub" }) preflight.Sources[source] = true;
            SetField(ui, "reviewPreflight", preflight);
            foreach (string name in new[] { "actionControls", "groups", "apps", "picks", "installedKeys", "searchResults", "pickItems", "savedProfiles", "removal", "runItems", "setupItems", "updates", "settingsProfiles", "rail", "driverPreviewCallbacks" })
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
            SetField(ui, "tweaksScanned", true); // UI checks use synthetic detection, never scan the host.
            Get<TextBlock>(ui, "SysWindows").Text = "Windows 11 Enterprise 25H2";
            Get<TextBlock>(ui, "SysBuild").Text = "Build 26200.7171";
            Get<TextBlock>(ui, "SysArchitecture").Text = "64-bit Windows";
            Get<TextBlock>(ui, "SysDisk").Text = "C: · 142 GB free of 953 GB";
            Get<TextBlock>(ui, "SysChoco").Text = "Chocolatey 2.6.0";
            Get<TextBlock>(ui, "SysWinget").Text = "winget available";
            Get<TextBlock>(ui, "SysNet").Text = "Online";
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
        static void RenderOptions(Window window, string name = "app-options", int width = 680, int height = 530)
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); var background = new DrawingVisual();
            using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height)); image.Render(background); image.Render(root);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-previews"); Directory.CreateDirectory(folder);
            using (var stream = File.Create(Path.Combine(folder, name + ".png"))) png.Save(stream);
        }
        static DriverPreviewReport PreviewFixture()
        {
            return DriverPreviewReport.Parse(@"{""SchemaVersion"":1,""InventoryComplete"":true,""Error"":"""",""Rows"":[{""Hardware"":""Intel Arc A770"",""DeviceId"":""PCI fixture 1"",""Kind"":""Graphics"",""PackageId"":""intel-arc-graphics-driver"",""PackageName"":""Intel Arc / Core Ultra Graphics Driver"",""Compatibility"":""Candidate"",""Reason"":""Reviewed PCI device. Execution revalidates hardware compatibility."",""InstalledDriverVersion"":""32.0.101.9000"",""AvailablePackageVersion"":""32.0.101.9175"",""MetadataStatus"":""Available"",""SupportUrl"":""https://www.intel.com/content/www/us/en/support/articles/000090440/graphics.html""},{""Hardware"":""AMD Radeon RX 7800 XT"",""DeviceId"":""PCI fixture 2"",""Kind"":""Graphics"",""PackageId"":""amd-software-adrenalin-edition"",""PackageName"":""AMD Software: Adrenalin Edition"",""Compatibility"":""Candidate"",""Reason"":""AMD display device detected; OEM and legacy model compatibility require review."",""InstalledDriverVersion"":""32.0.21037.1004"",""AvailablePackageVersion"":""26.10.2"",""MetadataStatus"":""Available"",""SupportUrl"":""https://www.amd.com/en/support/download/drivers.html""}]}");
        }
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                IconTests.Run(Check);
                var ui = Fixture();
                var initialTweak = Field<Debloat>(ui, "debloat").Categories[0].Toggles[0];
                Call(ui, "CompleteTweakDetection", new Dictionary<string, string> { { initialTweak.FeatureId, "Applied" } }, new List<string>(), true, new List<string>(), true, new List<string>());
                Check(!initialTweak.IsSelected && initialTweak.StateText == "Applied", "Initial UI detection reports current state without selecting a setup change");
                Call(ui, "SetRunning", true, "Synthetic job"); Call(ui, "SetRunning", false, null);
                Check(!(bool)Call(ui, "IsTweakScanCurrent", 0), "A scan from before a completed job is rejected even after running becomes false");
                Field<Debloat>(ui, "debloat").ApplyFeatures(new string[0]);
                // Catalog and Apps page structure
                var all = Field<List<AppItem>>(ui, "apps");
                Check(all.Count >= 160 && all.FindAll(a => a.IsSelected).Count == 0 && all.TrueForAll(a => !a.DefaultOn), "Built-in catalog includes the Ninite apps and opens with nothing ticked");
                Check(all.Exists(a => a.Id == "OpenRGB.OpenRGBEffectsPlugin" && a.IsWinget && a.Category == "Gaming & devices") && all.Exists(a => a.Id == "Microsoft.VisualStudio.BuildTools" && a.Category == "Developer" && a.Parameters.Contains("Microsoft.VisualStudio.Workload.VCTools")), "OpenRGB Effects Plugin and Visual Studio Build Tools (C++ workload) are in the catalog");
                var sleek = all.Find(a => a.Id == "minimalhandle/SleekMeeter");
                Check(sleek != null && sleek.IsGitHub && !sleek.IsSelected && sleek.Key == "github:minimalhandle/SleekMeeter" && sleek.EnginePackageId == "github-minimalhandle-sleekmeeter" && sleek.SourceLabel == "GitHub" && sleek.Category == "Media", "GitHub catalog entries parse with their own key, engine ID and category");
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
                Check(!twins.Exists(r => r.IsBest), "Same-named GitHub projects are not guessed as the best match");
                var namesakes = Sources.Rank("afterclip", new List<SearchResult> {
                    new SearchResult { Source = "github", Id = "panzergen/AfterClip", Name = "AfterClip", Version = "0.3.0", Meta = "p", Popularity = 200 },
                    new SearchResult { Source = "github", Id = "minimalhandle/Afterclip", Name = "Afterclip", Version = "1.5.0", Meta = "m", Popularity = 3 } });
                Sources.MarkInstalled(namesakes, new InstalledSnapshot(new[] { new InstalledRecord { RegistryPath = "test", Name = "Afterclip", Version = "1.4.0", Repository = "minimalhandle/Afterclip" } }, new Dictionary<string, string>()));
                Sources.PickBest(namesakes);
                Check(namesakes[0].Id == "minimalhandle/Afterclip" && namesakes[0].IsBest && namesakes[0].InstalledVersion == "1.4.0" && namesakes[1].InstalledVersion == null && !namesakes[1].IsBest, "Repository evidence attributes an older installed version to the correct namesake");
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
                var firefox = all.Find(a => a.Name == "Mozilla Firefox");
                Check(firefox != null && firefox.IsWinget && firefox.Id == "Mozilla.Firefox" && !firefox.IsSelected && firefox.Artwork.Image != null, "Firefox installs from Mozilla's winget package and starts unticked");
                Check(all.Find(a => a.Id == "spotify") != null && all.Find(a => a.Id == "pawnio") != null && all.Find(a => a.Id == "openrgb.install") != null && all.Find(a => a.Id == "python3") != null, "Spotify, install-order pairs and Python 3 stay on Chocolatey");
                var browsers = Field<ObservableCollection<AppGroup>>(ui, "groups").First(g => g.Name == "Browsers").Items.Select(a => a.Name).ToList();
                Check(browsers.SequenceEqual(browsers.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)), "Apps are alphabetical within each category across sources");
                Check(Data.CompareVersions("2026.1109.1228.0", "11.42.3") == null && Data.CompareVersions("15.10.39.0", "15.8.118.0") > 0 && Data.CompareVersions("v1.5.0", "1.5") == 0, "Versions compare numerically and date-style versions are not compared with semantic ones");
                Check(Data.IsStoreId("9NKSQGP7F2NH") && Data.IsStoreId("XPFFTQ037JWMHS") && !Data.IsStoreId("Microsoft.PowerToys"), "Both Microsoft Store ID formats are recognised");
                var whatsapp = all.Find(a => a.Name == "WhatsApp");
                Check(whatsapp != null && whatsapp.IsStore && whatsapp.Id == "9NKSQGP7F2NH" && whatsapp.Artwork.Image != null && all.Exists(a => a.Id == "9PKTQ5699M62") && all.Exists(a => a.Id == "9N7JSXC1SJK6"), "WhatsApp, iCloud and Blip come from the Microsoft Store with bundled logos");
                Check(all.TrueForAll(a => a.Artwork.Image != null || a.Id == "nonexistent"), "Every built-in catalog app has a bundled logo");
                string updateArgs = (string)typeof(MainUi).GetMethod("UpdateArguments", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { new List<UpdateItem> {
                    new UpdateItem { Source = "choco", Id = "vlc", Name = "VLC" }, new UpdateItem { Source = "store", Id = "9NKSQGP7F2NH", Name = "WhatsApp" },
                    new UpdateItem { Source = "github", Id = "minimalhandle/Afterclip", Name = "After;clip" } } });
                Check(updateArgs.Contains("-Choco \"vlc\"") && updateArgs.Contains("-Store \"9NKSQGP7F2NH\"") && updateArgs.Contains("-GitHub \"minimalhandle/Afterclip|After clip\""), "Store and GitHub updates reach Updates.ps1 with safe separators");
                string scheduleArgs = (string)Call(ui, "StoreGitHubArgs");
                Check(scheduleArgs.Contains("9NKSQGP7F2NH") && scheduleArgs.Contains("9PKTQ5699M62") && scheduleArgs.Contains("minimalhandle/SleekMeeter|Sleekmeeter") && scheduleArgs.Contains("minimalhandle/Afterclip|Afterclip"), "The weekly task receives the Store and GitHub apps from the list");
                var detail = new SearchResult { Source = "store", Id = "9N7JSXC1SJK6", Name = "Blip", Homepage = "https://apps.microsoft.com/detail/9N7JSXC1SJK6", Publisher = "Blip Studio Inc." };
                Check(detail.InstalledVisibility == Visibility.Collapsed && detail.HomepageVisibility == Visibility.Visible, "Search results show a website link and no Installed badge until detected");
                detail.InstalledVersion = "1.2.8.0"; var unversioned = new SearchResult { InstalledVersion = "" }; var unsafeLink = new SearchResult { Homepage = "http://example.com" };
                Check(detail.InstalledText == "Installed v1.2.8.0" && unversioned.InstalledText == "Installed" && unsafeLink.HomepageVisibility == Visibility.Collapsed, "Installed badges show the version, and non-https links are hidden");
                Check(!Data.IsGitHubRepo("../etc") && !Data.IsGitHubRepo("owner/..") && Data.IsGitHubRepo("minimalhandle/Afterclip"), "GitHub repository IDs are validated");
                Check(all.Find(a => a.Id == "brave").Category == "Browsers" && !all.Find(a => a.Id == "brave").DefaultOn, "Catalog ::GROUP sections drive categories and new apps start unticked");
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
                Check(starter > 0 && !box.Items.Contains("List defaults") && ui.Window.FindName("BtnDefaults") == null, "Starter profiles are offered in the profile picker and there is no list-defaults selection");
                var tweaksBefore = Field<object>(ui, "debloat") == null ? 0 : ((System.Collections.ICollection)Field<object>(ui, "debloat").GetType().GetMethod("SelectedFeatures").Invoke(Field<object>(ui, "debloat"), null)).Count;
                Check(Get<Border>(ui, "StarterBanner").Visibility == Visibility.Visible && Get<WrapPanel>(ui, "StarterButtons").Children.Count == 4, "With nothing ticked, the Apps page offers the four starter profiles");
                box.SelectedIndex = starter;
                Check(Get<Border>(ui, "StarterBanner").Visibility == Visibility.Collapsed, "The starter banner hides once apps are ticked");
                var tweaksAfter = Field<object>(ui, "debloat") == null ? 0 : ((System.Collections.ICollection)Field<object>(ui, "debloat").GetType().GetMethod("SelectedFeatures").Invoke(Field<object>(ui, "debloat"), null)).Count;
                Check(all.Find(a => a.Id == "SumatraPDF.SumatraPDF").IsSelected && !all.Find(a => a.Id == "Valve.Steam").IsSelected && tweaksBefore == tweaksAfter, "Starter profile selects its apps and leaves Windows tweaks unchanged");
                var plugin = all.Find(a => a.Id == "OpenRGB.OpenRGBEffectsPlugin"); plugin.IsSelected = true;
                Check(all.Find(a => a.Id == "openrgb.install").IsSelected && all.Find(a => a.Id == "pawnio").IsSelected, "Ticking the OpenRGB Effects Plugin also ticks OpenRGB and PawnIO");
                all.Find(a => a.Id == "openrgb.install").IsSelected = false;
                Check(plugin.IsSelected && all.Find(a => a.Id == "pawnio").IsSelected, "Unticking a needed app does not cascade");
                foreach (string id in new[] { "OpenRGB.OpenRGBEffectsPlugin", "pawnio" }) all.Find(a => a.Id == id).IsSelected = false;
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
                int progressive = (int)Call(ui, "BeginSearch", "progressive", "all");
                var cancelledSearch = Field<System.Threading.CancellationTokenSource>(ui, "searchCancellation");
                Call(ui, "PublishSearch", progressive, "progressive", results, "", false);
                Call(ui, "PublishSearch", progressive, "progressive", results, "", true);
                Check(Field<ObservableCollection<SearchResult>>(ui, "searchResults").Count == 1, "Progressive completion replaces earlier rows without duplicates");
                var possible = new SearchResult { Source = "github", Id = "example/App", Name = "App", InstalledMatch = new InstalledMatch { Record = new InstalledRecord { Name = "App", Version = "1.0", RegistryPath = "fixture" }, Reason = "Name only" } };
                Call(ui, "PublishSearch", progressive, "progressive", new List<SearchResult> { possible }, "", true);
                var searchRoot = (FrameworkElement)ui.Window.Content;
                searchRoot.Measure(new Size(1160, 780)); searchRoot.Arrange(new Rect(0, 0, 1160, 780)); searchRoot.UpdateLayout();
                if (args.Length > 0 && args[0] == "--render") Render(ui, "search-identity-small", 1064, 641);
                var link = FindButton(Get<ItemsControl>(ui, "SearchResults"), "link-installed");
                if (link != null) link.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, link));
                Check(link != null && Get<TextBlock>(ui, "DlgTitle").Text == "Link this installed app?", "The actual search-result link button opens identity confirmation");
                Call(ui, "HideDialog"); SetField(ui, "dialogOk", null);
                Call(ui, "CloseSearch");
                Check(cancelledSearch.IsCancellationRequested, "Closing the search cancels the active source requests");

                var savedRecipe = (SetupRecipe)Call(ui, "CaptureRecipe");
                var appOnly = SetupRecipe.Parse("{\"Sources\":[{\"SourceDetails\":{\"Name\":\"winget\"},\"Packages\":[{\"PackageIdentifier\":\"Example.Tool\"}]}]}", savedRecipe.ListText);
                var priorTweaks = Field<Debloat>(ui, "debloat").SelectedFeatures();
                Call(ui, "PreviewRecipe", appOnly);
                if (args.Length > 0 && args[0] == "--render") Render(ui, "recipe-preview-small", 1064, 641);
                var previewRoot = (FrameworkElement)ui.Window.Content;
                previewRoot.Measure(new Size(1064, 641)); previewRoot.Arrange(new Rect(0, 0, 1064, 641)); previewRoot.UpdateLayout();
                var applyRecipeButton = Get<Button>(ui, "DlgOk"); var applyOrigin = applyRecipeButton.TranslatePoint(new Point(), previewRoot);
                Check(Get<TextBlock>(ui, "DlgTitle").Text == "Import setup recipe?" && applyOrigin.Y >= 0 && applyOrigin.Y + applyRecipeButton.ActualHeight <= 641 && !Field<bool>(ui, "running"), "Recipe preview keeps Apply visible at minimum size and leaves setup idle");
                Call(ui, "HideDialog"); SetField(ui, "dialogOk", null);
                Call(ui, "ApplyRecipe", appOnly);
                Check(Field<List<AppItem>>(ui, "apps").FindAll(a => a.IsSelected).Count == 1 && Field<List<AppItem>>(ui, "apps").Find(a => a.Id == "Example.Tool").IsSelected && priorTweaks.SequenceEqual(Field<Debloat>(ui, "debloat").SelectedFeatures()) && !Field<bool>(ui, "running"), "Applying a winget recipe changes selections without running setup or changing tweaks");
                Call(ui, "ApplyRecipe", savedRecipe);
                Check(Field<List<AppItem>>(ui, "apps").FindAll(a => a.IsSelected).Count == savedRecipe.Apps.Count, "Native recipe restores the captured app selection");
                bool recipeRejected = false; var badRecipe = (SetupRecipe)Call(ui, "CaptureRecipe"); badRecipe.Tweaks.Add("UnknownFeature");
                string sessionBeforeRecipe = Engine.SessionList;
                try { Call(ui, "ApplyRecipe", badRecipe); } catch (TargetInvocationException ex) { recipeRejected = ex.InnerException is InvalidDataException; }
                Check(recipeRejected && Engine.SessionList == sessionBeforeRecipe, "Unavailable recipe tweaks are rejected before the session changes");

                var optionItem = Field<List<AppItem>>(ui, "apps").Find(a => a.Id == "Mozilla.Firefox");
                int selectedBeforeOptions = Field<List<AppItem>>(ui, "apps").Count(a => a.IsSelected);
                var optionWindow = (Window)Call(ui, "BuildAppOptionsWindow", optionItem);
                var argumentBox = FindNamed<TextBox>(optionWindow, "AppArguments"); var saveOptions = FindNamed<Button>(optionWindow, "SaveAppOptions");
                argumentBox.Text = "--id Another.App";
                Check(!saveOptions.IsEnabled, "App options rejects package-selection arguments before saving");
                argumentBox.Text = "/S"; saveOptions.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, saveOptions));
                var afterOptions = Field<List<AppItem>>(ui, "apps").Find(a => a.Id == optionItem.Id);
                Check(afterOptions.Parameters == "/S" && Field<List<AppItem>>(ui, "apps").Count(a => a.IsSelected) == selectedBeforeOptions && Engine.CurrentListText(out customList).Contains("Mozilla.Firefox|Mozilla Firefox|/S"), "App options saves arguments into the session catalog while preserving selections");
                var recipeWithOptions = (SetupRecipe)Call(ui, "CaptureRecipe");
                Check(SetupRecipe.Parse(recipeWithOptions.Serialize(), recipeWithOptions.ListText).ListText.Contains("Mozilla.Firefox|Mozilla Firefox|/S"), "Edited app arguments survive recipe export and import");
                var localeWindow = (Window)Call(ui, "BuildAppOptionsWindow", afterOptions);
                var languageBox = FindNamed<ComboBox>(localeWindow, "AppLanguage");
                languageBox.SelectedItem = languageBox.Items.Cast<AppLanguage>().First(l => l.Code == "fr");
                Check(FindNamed<TextBlock>(localeWindow, "AppOptionsPreview").Text.Contains("Mozilla.Firefox.fr") && FindNamed<TextBox>(localeWindow, "AppArguments").Text == "/S", "Guided language selection previews the exact package without discarding advanced arguments");
                var localeSave = FindNamed<Button>(localeWindow, "SaveAppOptions"); localeSave.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, localeSave));
                var french = Field<List<AppItem>>(ui, "apps").Find(a => a.Id == "Mozilla.Firefox.fr");
                var frenchRecipe = (SetupRecipe)Call(ui, "CaptureRecipe");
                Check(french != null && french.IsSelected && !Field<List<AppItem>>(ui, "apps").Any(a => a.Id == "Mozilla.Firefox") && frenchRecipe.Apps.Contains("winget:Mozilla.Firefox.fr"), "Saving a guided language updates the selected package and exported recipe identity");
                Call(ui, "ApplyRecipe", recipeWithOptions);
                var git = Field<List<AppItem>>(ui, "apps").First(a => a.Source == "choco" && (a.Id == "git" || a.Id == "git.install"));
                var gitWindow = (Window)Call(ui, "BuildAppOptionsWindow", git); var gitInput = FindNamed<TextBox>(gitWindow, "AppArguments");
                gitInput.Text = "/SChannel"; FindNamed<CheckBox>(gitWindow, "GitShellIntegration").IsChecked = false; FindNamed<CheckBox>(gitWindow, "GitLfs").IsChecked = false;
                Check(gitInput.Text.Contains("/SChannel") && gitInput.Text.Contains("/NoShellIntegration") && gitInput.Text.Contains("/NoGitLfs"), "Guided component checkboxes preserve custom arguments while adding omission switches");
                var resetGuided = FindNamed<Button>(gitWindow, "ResetAppDefaults"); resetGuided.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, resetGuided));
                Check(gitInput.Text == "" && FindNamed<CheckBox>(gitWindow, "GitLfs").IsChecked == true, "Reset defaults restores guided components and clears advanced overrides");
                gitWindow.Close();
                var buildTools = Field<List<AppItem>>(ui, "apps").First(a => a.Id == "Microsoft.VisualStudio.BuildTools");
                var toolsWindow = (Window)Call(ui, "BuildAppOptionsWindow", buildTools); var toolsInput = FindNamed<TextBox>(toolsWindow, "AppArguments");
                Check(FindNamed<CheckBox>(toolsWindow, "WorkloadVCTools").IsChecked == true && FindNamed<CheckBox>(toolsWindow, "WorkloadWebBuildTools").IsChecked == false && FindNamed<CheckBox>(toolsWindow, "WorkloadRecommended").IsChecked == true, "Build Tools options show the catalog workloads as checkboxes");
                FindNamed<CheckBox>(toolsWindow, "WorkloadWebBuildTools").IsChecked = true; FindNamed<CheckBox>(toolsWindow, "WorkloadVCTools").IsChecked = false;
                Check(toolsInput.Text.Contains("--add Microsoft.VisualStudio.Workload.WebBuildTools") && !toolsInput.Text.Contains("VCTools") && toolsInput.Text.StartsWith(AppOptions.BuildToolsSilent), "Workload checkboxes rewrite the installer override: " + toolsInput.Text);
                var versionBox = FindNamed<ComboBox>(toolsWindow, "AppVersion");
                versionBox.SelectedItem = versionBox.Items.Cast<AppVersion>().First(v => v.PackageId == "Microsoft.VisualStudio.2022.BuildTools");
                Check(FindNamed<CheckBox>(toolsWindow, "WorkloadXamarinBuildTools") != null && FindNamed<CheckBox>(toolsWindow, "WorkloadNetCrossPlatBuildTools") == null && FindNamed<CheckBox>(toolsWindow, "WorkloadWebBuildTools").IsChecked == true && FindNamed<TextBlock>(toolsWindow, "AppOptionsPreview").Text.Contains("Microsoft.VisualStudio.2022.BuildTools"), "Choosing Build Tools 2022 rebuilds the workload list and keeps chosen workloads");
                versionBox.SelectedItem = versionBox.Items.Cast<AppVersion>().First(v => v.PackageId == "Microsoft.VisualStudio.2019.BuildTools");
                Check(FindNamed<CheckBox>(toolsWindow, "WorkloadVCTools") == null && toolsInput.Text.Contains("WebBuildTools"), "Build Tools 2019 shows no workload checkboxes and keeps the arguments");
                Check(AppIcons.For("Microsoft.VisualStudio.2022.BuildTools", "winget") != null, "Other versions use the family logo");
                toolsWindow.Close();
                bool oldRecipeCustom; string builtinList = Engine.CurrentListText(out oldRecipeCustom);
                var oldRecipe = new SetupRecipe { ListText = System.Text.RegularExpressions.Regex.Replace(builtinList, @"(?m)^::DISABLED Microsoft\.VisualStudio\.BuildTools\|.*\r?\n", ""), Apps = new List<string> { "winget:7zip.7zip" }, HasWindowsSelections = false };
                string savedSession = Engine.SessionList;
                Call(ui, "ApplyRecipe", oldRecipe);
                Check(Field<List<AppItem>>(ui, "apps").Exists(a => a.Id == "Microsoft.VisualStudio.BuildTools" && !a.IsSelected) && Field<List<AppItem>>(ui, "apps").Single(a => a.IsSelected).Id == "7zip.7zip", "Importing an older recipe keeps newer built-in apps available, unticked");
                Engine.SessionList = savedSession; Call(ui, "LoadList", false);

                Call(ui, "Navigate", "NavTweaks");
                var renderedRoot = (FrameworkElement)ui.Window.Content; renderedRoot.Measure(new Size(1264, 801)); renderedRoot.Arrange(new Rect(0, 0, 1264, 801)); renderedRoot.UpdateLayout();
                var sectionSelect = FindButton(Get<ItemsControl>(ui, "TweakGroups"), "tweak-select-all");
                var targetSection = sectionSelect == null ? null : sectionSelect.DataContext as TweakCategory;
                if (sectionSelect != null) sectionSelect.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, sectionSelect));
                Check(targetSection != null && targetSection.Toggles.All(t => t.IsSelected) && targetSection.Choices.All(c => c.SelectedIndex == 1), "The section Select all button routes through the tweak UI");
                Call(ui, "ApplyTweakQuickSelect", "AI");
                Check(Field<Debloat>(ui, "debloat").Categories.First(c => c.Id == "AI").Toggles.All(t => t.IsSelected), "AI quick select selects the vendor AI category in the UI");
                var removalFixture = Field<Debloat>(ui, "debloat").Apps[0]; removalFixture.IsSelected = true; removalFixture.Installed = false;
                Get<CheckBox>(ui, "OnlyInstalledRemovals").IsChecked = true;
                Get<CheckBox>(ui, "OnlyInstalledRemovals").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(removalFixture.Visibility == Visibility.Collapsed && removalFixture.IsSelected && Get<TextBlock>(ui, "RemovalCount").Text.Contains("hidden"), "Installed-only UI filtering preserves and reports hidden selected apps");
                Get<CheckBox>(ui, "OnlyInstalledRemovals").IsChecked = false; Call(ui, "FilterTweaks");

                var apps = Field<List<AppItem>>(ui, "apps");
                var tweakModel = Field<Debloat>(ui, "debloat");
                tweakModel.ApplyFeatures(new string[0]); tweakModel.ApplyApps(new string[0]); Get<CheckBox>(ui, "ForceEdgeSwitch").IsChecked = false;
                var desiredTweak = tweakModel.Categories[0].Toggles[0];
                tweakModel.ApplyFeatures(new[] { desiredTweak.FeatureId }); tweakModel.ApplyDetection(new Dictionary<string, string> { { desiredTweak.FeatureId, "Applied" } }); Call(ui, "RefreshTweakCounts");
                Check(!Get<Button>(ui, "BtnApplyTweaks").IsEnabled && ((SetupRecipe)Call(ui, "CaptureRecipe")).Tweaks.Contains(desiredTweak.FeatureId), "Already-current intent remains in recipes while Apply stays disabled for a no-op");
                foreach (AppItem a in apps) a.IsSelected = false;
                AppItem first = apps[0]; first.IsSelected = true;
                Call(ui, "OpenInstallPlan");
                Check(Get<TextBlock>(ui, "ReviewEstimate").Text.StartsWith("Estimate: about ") && Get<DockPanel>(ui, "ReviewEstimatePanel").Visibility == Visibility.Visible, "Setup review shows a time and download estimate");
                Check(((JobSpec)Call(ui, "BuildReviewedJob")).DebloatArgs == null && Get<TextBlock>(ui, "ReviewTweaksDetail").Text.Contains("Already applied"), "Setup review identifies skipped current settings and excludes them from the actual job");
                Call(ui, "CompleteTweakDetection", new Dictionary<string, string> { { desiredTweak.FeatureId, "Unknown" } }, new List<string>(), true, new List<string>(), true, new List<string>());
                Check(Get<CheckBox>(ui, "ReviewTweaks").IsEnabled && Get<CheckBox>(ui, "ReviewTweaks").IsChecked != true && ((JobSpec)Call(ui, "BuildReviewedJob")).DebloatArgs == null, "A detection refresh cannot silently enable newly pending work in an open review");
                Get<CheckBox>(ui, "ReviewTweaks").IsChecked = true;
                Check(((JobSpec)Call(ui, "BuildReviewedJob")).DebloatArgs.Contains("-" + desiredTweak.FeatureId), "Newly pending tweaks require explicit review selection before entering the job");
                Call(ui, "CloseReview");
                var secondDesiredTweak = tweakModel.Categories[0].Toggles[1];
                tweakModel.ApplyFeatures(new[] { desiredTweak.FeatureId, secondDesiredTweak.FeatureId });
                tweakModel.ApplyDetection(new Dictionary<string, string> { { desiredTweak.FeatureId, "Unknown" }, { secondDesiredTweak.FeatureId, "Applied" } });
                Call(ui, "OpenInstallPlan");
                Check(Get<CheckBox>(ui, "ReviewTweaks").IsChecked == true, "Review initially enables the explicitly selected pending tweak");
                Call(ui, "CompleteTweakDetection", new Dictionary<string, string>(), new List<string>(), true, new List<string>(), true, new List<string>());
                Check(Get<CheckBox>(ui, "ReviewTweaks").IsChecked != true && ((JobSpec)Call(ui, "BuildReviewedJob")).DebloatArgs == null, "Growing an already-enabled pending tweak set requires renewed review selection");
                Get<CheckBox>(ui, "ReviewTweaks").IsChecked = true;
                Check(((JobSpec)Call(ui, "BuildReviewedJob")).DebloatArgs.Contains("-" + secondDesiredTweak.FeatureId), "Explicitly re-enabling review accepts the refreshed pending set");
                Call(ui, "CloseReview");
                var debloat = Field<Debloat>(ui, "debloat");
                TweakTests.Run(Check);
                Check(debloat != null, "Embedded tweak data is available");
                debloat.ApplyFeatures(new string[0]); debloat.ApplyApps(new string[0]);
                Get<CheckBox>(ui, "ForceEdgeSwitch").IsChecked = false;
                Call(ui, "OpenInstallPlan");
                SetField(ui, "driverPreviewBusy", true); Call(ui, "UpdateReviewStart");
                Check(!Get<Button>(ui, "BtnReviewStart").IsEnabled, "Setup waits for a driver preview while driver installation is selected");
                Check(Call(ui, "BuildReviewedJob") == null && Get<TextBlock>(ui, "ReviewValidation").Text.Contains("driver"), "Job construction also blocks drivers while a preview refresh is pending");
                Get<CheckBox>(ui, "ReviewDrivers").IsChecked = false;
                Check(Get<Button>(ui, "BtnReviewStart").IsEnabled, "Disabling drivers allows setup to proceed independently of the driver scan");
                SetField(ui, "driverPreviewBusy", false); Get<CheckBox>(ui, "ReviewDrivers").IsChecked = true; Call(ui, "UpdateReviewStart");
                int generation = (int)Call(ui, "BeginReviewPreflight");
                Check(!Get<Button>(ui, "BtnReviewStart").IsEnabled && Call(ui, "BuildReviewedJob") == null, "Pending preflight blocks both the Start button and job construction");
                var readyFacts = new PreflightSnapshot { SystemFreeBytes = 100L * 1073741824, TempFreeBytes = 100L * 1073741824, RestartPending = true, WingetAvailable = true, RunningProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
                foreach (string source in new[] { "Chocolatey", "winget", "Store", "GitHub" }) readyFacts.Sources[source] = true;
                Call(ui, "CompleteReviewPreflight", generation - 1, readyFacts);
                Check(!Get<Button>(ui, "BtnReviewStart").IsEnabled, "Old asynchronous preflight completion cannot enable Start");
                Call(ui, "CompleteReviewPreflight", generation, readyFacts);
                Check(Get<Button>(ui, "BtnReviewStart").IsEnabled && Get<TextBlock>(ui, "ReviewPreflightSummary").Text.Contains("attention"), "Completed preflight exposes restart guidance while allowing reviewed warnings");
                readyFacts.CheckedUtc = DateTime.UtcNow.AddMinutes(-10); Call(ui, "UpdateReviewStart");
                Check(!Get<Button>(ui, "BtnReviewStart").IsEnabled && Call(ui, "BuildReviewedJob") == null, "Expired readiness facts cannot start a setup");
                readyFacts.CheckedUtc = DateTime.UtcNow; Call(ui, "UpdateReviewStart");
                SetField(ui, "driverPreview", PreviewFixture()); int continued = 0;
                var previewWindow = (Window)Call(ui, "BuildDriverPreviewWindow", (Action)delegate { continued++; });
                var previewContinue = FindNamed<Button>(previewWindow, "DriverPreviewContinue");
                Check(continued == 0 && previewContinue.IsEnabled && FindNamed<TextBlock>(previewWindow, "DriverPreviewStatus").Text.Contains("proposed installers"), "Constructing a populated driver preview is read-only and does not continue installation");
                previewContinue.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, previewContinue));
                previewContinue.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, previewContinue));
                Check(continued == 1, "Driver installation continuation requires an explicit click and runs only once");
                SetField(ui, "driverPreviewBusy", true);
                var busyPreview = (Window)Call(ui, "BuildDriverPreviewWindow", (Action)delegate { continued++; });
                Check(!FindNamed<Button>(busyPreview, "DriverPreviewContinue").IsEnabled, "A cached driver report cannot enable Continue while a fresh scan is pending"); busyPreview.Close(); SetField(ui, "driverPreviewBusy", false);
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
                var auditRecipe = (SetupRecipe)Call(ui, "CaptureRecipe");
                var auditTarget = new AuditTarget { Recipe = auditRecipe };
                var auditReport = new AuditReport { CheckedUtc = DateTime.UtcNow };
                auditReport.Rows.Add(new AuditRow { Category = "Apps", Key = "fixture-present", Name = "Installed fixture", Status = "Verified", Detail = "Exact package identity · version 1.0" });
                auditReport.Rows.Add(new AuditRow { Category = "Apps", Key = "fixture-missing", Name = "Missing fixture", Status = "Missing", Detail = "Not found in completed inventories. Review setup to install it." });
                string catalogBeforeAudit = Engine.SessionList, selectionsBeforeAudit = string.Join(",", apps.Where(a => a.IsSelected).Select(a => a.Key));
                var auditWindow = (Window)Call(ui, "BuildAuditWindow", auditTarget, auditReport, "Setup comparison", false);
                Check(FindNamed<StackPanel>(auditWindow, "AuditRows").Children.Count == 1, "Audit details initially show issues while preserving successful checks");
                FindNamed<CheckBox>(auditWindow, "AuditIssuesOnly").IsChecked = false;
                Check(FindNamed<StackPanel>(auditWindow, "AuditRows").Children.Count == 2, "Audit filter can reveal every verified result");
                Check(Engine.SessionList == catalogBeforeAudit && string.Join(",", apps.Where(a => a.IsSelected).Select(a => a.Key)) == selectionsBeforeAudit && !Field<bool>(ui, "running"), "Constructing and filtering setup comparison cannot apply recipes or start setup");
                auditWindow.Close();
                int auditGeneration = (int)Call(ui, "BeginPostAudit", auditTarget);
                int auditEpoch = Field<int>(ui, "tweakStateEpoch");
                Call(ui, "CompletePostAudit", auditGeneration - 1, auditEpoch, auditReport);
                Check(Field<AuditReport>(ui, "postAuditReport") == null, "Obsolete verification callbacks cannot populate current results");
                Call(ui, "CompletePostAudit", auditGeneration, auditEpoch, auditReport);
                Check(Get<TextBlock>(ui, "VerificationSummary").Text.Contains("need attention") && Get<Button>(ui, "BtnViewVerification").IsEnabled, "Completed verification appears separately on Progress");
                Call(ui, "InvalidateAuditResults");
                Call(ui, "CompletePostAudit", auditGeneration, auditEpoch, auditReport);
                Check(Field<AuditReport>(ui, "postAuditReport") == null, "A new system-changing job invalidates older verification results");
                var liveRun = new RunSnapshot { RunId = "ui-eta", Status = "Running", Current = "Fixture", Ok = 2, Queue = 2 };
                Call(ui, "UpdateTaskbar", true, liveRun);
                Check(ui.Window.TaskbarItemInfo != null && ui.Window.TaskbarItemInfo.ProgressState == System.Windows.Shell.TaskbarItemProgressState.Normal && Math.Abs(ui.Window.TaskbarItemInfo.ProgressValue - 0.5) < 0.001, "A live install drives the taskbar progress bar");
                Call(ui, "ShowSnapshot", liveRun, true, true);
                Check(Get<TextBlock>(ui, "RunDetail").Text == "2 of 4 done", "A live install shows no time estimate before there is evidence for one");
                Call(ui, "UpdateTaskbar", false, null);
                Check(ui.Window.TaskbarItemInfo.ProgressState == System.Windows.Shell.TaskbarItemProgressState.None && Field<object>(ui, "finishNotice") == null, "Idle Freshkit clears taskbar progress and shows no notification icon");
                if (args.Length > 0 && args[0] == "--render")
                {
                    var compareWindow = (Window)Call(ui, "BuildAuditWindow", auditTarget, auditReport, "Setup comparison", false);
                    RenderOptions(compareWindow, "setup-comparison", 920, 700); RenderOptions(compareWindow, "setup-comparison-small", 620, 450);
                    var compareRoot = (FrameworkElement)compareWindow.Content; var auditClose = FindNamed<Button>(compareWindow, "AuditClose"); var auditOrigin = auditClose.TranslatePoint(new Point(), compareRoot);
                    Check(auditOrigin.Y >= 0 && auditOrigin.Y + auditClose.ActualHeight <= 450, "Comparison keeps Close and Recheck visible at minimum size");
                    compareWindow.Close();
                    Call(ui, "InitProgress");
                    var completedJob = new JobSpec { Action = "Install", Apps = "fixture", DriverKit = "fixture drivers", WifiKit = "fixture wifi", DebloatArgs = new List<string> { "-Fixture" }, RestoreZip = "fixture.zip" };
                    var completedRecord = new SetupRecord { Recipe = completedJob, Steps = SetupProgress.Create(completedJob) };
                    foreach (var step in completedRecord.Steps) { step.Status = "Done"; step.Detail = "Completed successfully"; }
                    SetField(ui, "sessionSetup", completedRecord);
                    Call(ui, "ShowSnapshot", new RunSnapshot { Status = "Completed", When = DateTime.Now }, false, false); Call(ui, "ShowSetupSteps", false);
                    int shownGeneration = (int)Call(ui, "BeginPostAudit", auditTarget); Call(ui, "CompletePostAudit", shownGeneration, Field<int>(ui, "tweakStateEpoch"), auditReport);
                    foreach (string page in Field<string[]>(ui, "pages")) Get<FrameworkElement>(ui, page).Visibility = page == "PageProgress" ? Visibility.Visible : Visibility.Collapsed;
                    Render(ui, "verification-progress-small", 1064, 641);
                    Render(ui, "verification-progress", 1264, 801);
                    var progressRoot = (FrameworkElement)ui.Window.Content; var viewVerification = Get<Button>(ui, "BtnViewVerification"); var verificationOrigin = viewVerification.TranslatePoint(new Point(), progressRoot);
                    Check(verificationOrigin.Y >= 0 && verificationOrigin.Y + viewVerification.ActualHeight <= 641, "Completed setup keeps verification results accessible at minimum size");
                    SetField(ui, "sessionSetup", null);
                    foreach (string page in Field<string[]>(ui, "pages")) Get<FrameworkElement>(ui, page).Visibility = page == "PageSettings" ? Visibility.Visible : Visibility.Collapsed;
                    Render(ui, "settings-small", 1064, 641); Render(ui, "settings", 1264, 801);
                    Check(Get<Button>(ui, "BtnPrepareReinstall") != null && Get<Button>(ui, "BtnRestoreReinstall") != null && Get<Button>(ui, "BtnDailyBackupOn") != null && Get<TextBox>(ui, "DailyBackupFolder") != null, "Settings backup page offers daily backups and the reinstall folder");
                    var manyDevices = PreviewFixture();
                    for (int i = 0; i < 30; i++) manyDevices.Rows.Add(new DriverPreviewRow { Hardware = "AMD PCI device " + i, DeviceId = "PCI fixture chipset " + i, Kind = "Chipset", PackageId = "amd-chipset-drivers", PackageName = "AMD Chipset Drivers", Compatibility = "Candidate", Reason = "Detected AMD system devices; review manufacturer compatibility.", InstalledDriverVersion = "1.0." + i, AvailablePackageVersion = "7.1", MetadataStatus = "Available", SupportUrl = "https://www.amd.com/en/support/download/drivers.html" });
                    SetField(ui, "driverPreview", manyDevices);
                    var driverWindow = (Window)Call(ui, "BuildDriverPreviewWindow", (Action)delegate { });
                    Check(!FindNamed<Expander>(driverWindow, "DriverDevices").IsExpanded && FindNamed<TextBlock>(driverWindow, "DriverPreviewStatus").Text.Contains("3 proposed installers"), "Many-device preview defaults to three installer cards with collapsed device details");
                    RenderOptions(driverWindow, "driver-preview", 920, 700); RenderOptions(driverWindow, "driver-preview-small", 620, 450);
                    var driverContent = (FrameworkElement)driverWindow.Content; var continueButton = FindNamed<Button>(driverWindow, "DriverPreviewContinue"); var driverOrigin = continueButton.TranslatePoint(new Point(), driverContent);
                    Check(driverOrigin.Y >= 0 && driverOrigin.Y + continueButton.ActualHeight <= 450, "Driver preview retains the explicit Continue action at minimum size"); driverWindow.Close();
                    Call(ui, "Navigate", "NavTweaks");
                    var states = new Dictionary<string, string>();
                    states[debloat.Categories[0].Toggles[0].FeatureId] = "Applied";
                    Call(ui, "CompleteTweakDetection", states, new List<string> { debloat.Apps[0].AppId }, true, new List<string>(), true, new List<string>());
                    Render(ui, "tweaks-small", 1064, 641); Render(ui, "tweaks", 1264, 801);
                    Get<TextBox>(ui, "TweakSearch").Text = "remove apps";
                    Get<CheckBox>(ui, "OnlyInstalledRemovals").IsChecked = true; Call(ui, "FilterTweaks");
                    Render(ui, "removal-installed-small", 1064, 641);
                    Get<CheckBox>(ui, "OnlyInstalledRemovals").IsChecked = false; Get<TextBox>(ui, "TweakSearch").Text = "";
                    var optionsPreview = (Window)Call(ui, "BuildAppOptionsWindow", apps.First(a => a.Id == "Mozilla.Firefox"));
                    RenderOptions(optionsPreview); optionsPreview.Close();
                    foreach (AppItem a in apps) a.IsSelected = a.DefaultOn;
                    apps[0].Installed = true;
                    Field<ObservableCollection<AppGroup>>(ui, "groups")[0].Items[0].Installed = true;
                    Call(ui, "Navigate", "NavApps");
                    Render(ui, "apps", 1264, 801);
                    Render(ui, "apps-small", 1064, 641);
                    var sidebarRoot = (FrameworkElement)ui.Window.Content;
                    var pc = Get<Border>(ui, "ThisPcPanel"); var pcOrigin = pc.TranslatePoint(new Point(), sidebarRoot);
                    Check(pcOrigin.Y >= 0 && pcOrigin.Y + pc.ActualHeight <= 641 && Get<TextBlock>(ui, "SysBuild").ActualHeight > 0, "This PC shows a separate build line and all details at minimum size");
                    Get<Border>(ui, "ResumeBanner").Visibility = Visibility.Visible;
                    Get<TextBlock>(ui, "SysWindows").Text = "Windows 11 Enterprise multi-session 25H2";
                    Render(ui, "sidebar-resume-small", 1064, 641);
                    pcOrigin = pc.TranslatePoint(new Point(), sidebarRoot);
                    Check(pcOrigin.Y >= 0 && pcOrigin.Y + pc.ActualHeight <= 641 && Get<ScrollViewer>(ui, "SidebarNavigation").ActualHeight > 0, "Long OS details and a resume banner keep This PC visible and navigation scrollable");
                    Get<Border>(ui, "ResumeBanner").Visibility = Visibility.Collapsed;
                    Get<TextBlock>(ui, "SysWindows").Text = "Windows 11 Enterprise 25H2";
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
                    // README screenshots: a realistic starter selection, clean readiness results and a neutral status line.
                    Call(ui, "CloseReview");
                    foreach (AppItem a in apps) { a.IsSelected = false; a.Installed = false; }
                    foreach (var g in Field<ObservableCollection<AppGroup>>(ui, "groups")) foreach (var a in g.Items) a.Installed = false;
                    var profiles = Get<ComboBox>(ui, "ProfileBox");
                    for (int i = 0; i < profiles.Items.Count; i++) if ((profiles.Items[i] as string ?? "") == "Starter: essentials") profiles.SelectedIndex = i;
                    apps.First(a => a.Id == "Discord.Discord").IsSelected = true; apps.First(a => a.Id == "Valve.Steam").IsSelected = true;
                    Get<TextBlock>(ui, "StatusText").Text = "Ready";
                    Call(ui, "Navigate", "NavApps");
                    Render(ui, "readme-apps", 1264, 801);
                    Call(ui, "OpenInstallPlan");
                    var readinessRequest = (PreflightRequest)Call(ui, "ReviewPreflightRequest");
                    var readiness = new PreflightSnapshot { SystemFreeBytes = 412L << 30, TempFreeBytes = 412L << 30, RestartPending = false, WingetAvailable = true, RunningProcesses = new HashSet<string>() };
                    foreach (string s in readinessRequest.Sources) readiness.Sources[s] = true;
                    Call(ui, "CompleteReviewPreflight", (int)Call(ui, "BeginReviewPreflight"), readiness);
                    Get<ScrollViewer>(ui, "ReviewScroll").ScrollToTop();
                    Render(ui, "readme-review", 1264, 801);
                    Call(ui, "CloseReview");
                    var toolsPreview = (Window)Call(ui, "BuildAppOptionsWindow", apps.First(a => a.Id == "Microsoft.VisualStudio.BuildTools"));
                    RenderOptions(toolsPreview, "readme-app-options", 720, 760); toolsPreview.Close();
                    Get<TextBox>(ui, "TweakSearch").Text = "";
                    Call(ui, "Navigate", "NavTweaks"); Get<TextBlock>(ui, "StatusText").Text = "Ready";
                    Render(ui, "readme-tweaks", 1264, 801);
                    var backupTime = Get<ComboBox>(ui, "DailyBackupTime"); var backupKeep = Get<ComboBox>(ui, "DailyBackupKeep");
                    if (backupTime.Items.Count == 0) { backupTime.Items.Add("03:00"); backupTime.SelectedIndex = 0; }
                    if (backupKeep.Items.Count == 0) { backupKeep.Items.Add(new ComboBoxItem { Content = "Keep 7 backups" }); backupKeep.SelectedIndex = 0; }                    Call(ui, "Navigate", "NavSettings"); Get<TextBlock>(ui, "StatusText").Text = "Ready";
                    Render(ui, "readme-settings", 1264, 801);
                    Call(ui, "Navigate", "NavProgress");
                    SetField(ui, "sessionSetup", completedRecord);
                    Call(ui, "ShowSnapshot", new RunSnapshot { Status = "Completed", When = DateTime.Now }, false, false); Call(ui, "ShowSetupSteps", false);
                    var verified = new AuditReport { CheckedUtc = DateTime.UtcNow };
                    foreach (string name in new[] { "Mozilla Firefox", "7-Zip", "VLC media player", "Discord", "Steam" }) verified.Rows.Add(new AuditRow { Category = "Apps", Key = name, Name = name, Status = "Verified", Detail = "Installed" });
                    int readmeGeneration = (int)Call(ui, "BeginPostAudit", auditTarget); Call(ui, "CompletePostAudit", readmeGeneration, Field<int>(ui, "tweakStateEpoch"), verified);
                    Get<TextBlock>(ui, "StatusText").Text = "Setup finished";
                    Render(ui, "readme-progress", 1264, 801);
                    SetField(ui, "sessionSetup", null);
                }
                ui.Window.Close();
                Console.WriteLine(checks + " UI checks passed");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
