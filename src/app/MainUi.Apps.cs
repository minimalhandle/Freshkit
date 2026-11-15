// Freshkit - Apps page: list, profiles, search & add, scan this PC, install plan.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Freshkit
{
    internal sealed partial class MainUi
    {
        readonly ObservableCollection<AppGroup> groups = new ObservableCollection<AppGroup>();
        readonly ObservableCollection<CategoryFilter> rail = new ObservableCollection<CategoryFilter>();
        string categoryFilter = "*";
        readonly List<AppItem> apps = new List<AppItem>();
        readonly Dictionary<string, bool> picks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> installedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly ObservableCollection<SearchResult> searchResults = new ObservableCollection<SearchResult>();
        readonly ObservableCollection<PickItem> pickItems = new ObservableCollection<PickItem>();
        List<SavedProfile> savedProfiles = new List<SavedProfile>();
        bool applyingProfile, online = true;
        int searchRequest;
        Action<List<PickItem>> pickOk;

        void InitApps()
        {
            Get<ItemsControl>("GroupList").ItemsSource = groups;
            Get<ItemsControl>("GroupList").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnGroupToggle));
            EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent, new RoutedEventHandler(OnMenuItem));

            Get<ItemsControl>("CategoryRail").ItemsSource = rail;
            Get<TextBox>("SearchBox").TextChanged += delegate
            {
                // Typing searches the whole catalog rather than only the open category.
                if (Get<TextBox>("SearchBox").Text.Trim().Length > 0 && categoryFilter != "*") PickCategory("*");
                ApplyFilter();
            };
            var scroll = Get<ScrollViewer>("AppScroll");
            scroll.ScrollChanged += delegate (object s, ScrollChangedEventArgs e) { if (e.ViewportWidthChange != 0) FitCards(scroll.ViewportWidth - 8); };
            Click("BtnAppsMore", delegate { OpenAppsMenu((Button)Get<Button>("BtnAppsMore")); });
            Click("BtnAll", delegate { Batch(delegate { foreach (AppItem a in apps) if (a.Visibility == Visibility.Visible) a.IsSelected = true; }); });
            Click("BtnNone", delegate { Batch(delegate { foreach (AppItem a in apps) if (a.Visibility == Visibility.Visible) a.IsSelected = false; }); });
            Click("BtnDefaults", delegate { Batch(delegate { foreach (AppItem a in apps) a.IsSelected = a.DefaultOn; }); });
            Click("BtnEditList", delegate { EditList(); });
            Click("BtnReload", delegate { Confirm("Reset this session?", "Restores the built-in list and removes session profiles. No installed apps are changed.", "Reset", delegate { Engine.SessionList = null; picks.Clear(); LoadList(true); }); });
            Act("BtnInstall", delegate { OpenInstallPlan(); });
            Act("BtnAddApps", delegate { OpenSearch(); });
            Act("BtnScan", delegate { ScanThisPc(); });

            var box = Get<ComboBox>("ProfileBox");
            box.SelectionChanged += delegate { if (!applyingProfile) ApplyProfileIndex(box.SelectedIndex); };
            Click("BtnSaveProfile", delegate { PromptProfileName(null); });
            Click("BtnDeleteProfile", delegate
            {
                int i = box.SelectedIndex - 2;
                if (i < 0 || i >= savedProfiles.Count) { Info("No saved profile selected", "Pick one of your saved profiles first."); return; }
                string name = savedProfiles[i].Name;
                Confirm("Delete profile \"" + name + "\"?", "Only the saved selection is removed. Your apps and list stay as they are.", "Delete", delegate
                {
                    try { ListFile.DeleteProfile(name); LoadList(false); Log("Deleted profile " + name + "."); }
                    catch (Exception ex) { Info("Couldn't delete the profile", ex.Message); }
                });
            });

            // search sheet
            Get<ItemsControl>("SearchResults").ItemsSource = searchResults;
            Get<ItemsControl>("SearchResults").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnAddResult));
            Click("BtnSearchClose", delegate { CloseSearch(); });
            Click("BtnSheetSearch", delegate { DoSearch(); });
            Click("BtnSearchRetry", delegate { DoSearch(); });
            InitReview();
            Get<TextBox>("SheetQuery").KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) DoSearch(); };

            // pick sheet
            Get<ItemsControl>("PickList").ItemsSource = pickItems;
            Click("BtnPickAll", delegate { foreach (PickItem p in pickItems) p.IsSelected = true; });
            Click("BtnPickNone", delegate { foreach (PickItem p in pickItems) p.IsSelected = false; });
            Click("PickCancel", delegate { Get<Grid>("PickSheet").Visibility = Visibility.Collapsed; pickOk = null; });
            Click("PickOk", delegate
            {
                Get<Grid>("PickSheet").Visibility = Visibility.Collapsed;
                var chosen = new List<PickItem>();
                foreach (PickItem p in pickItems) if (p.IsSelected) chosen.Add(p);
                var a = pickOk; pickOk = null;
                if (a != null) a(chosen);
            });
        }

        void Batch(Action a) { a(); MarkCustom(); }

        // ---------------------------------------------------------------- list
        void LoadList(bool announce)
        {
            ListFile lf; bool external;
            try { lf = ListFile.Parse(Engine.CurrentListText(out external)); }
            catch (Exception ex) { Info("The list couldn't be read", ex.Message); return; }

            foreach (AppItem a in apps) picks[a.Key] = a.IsSelected;
            apps.Clear();
            groups.Clear();
            var byCat = new Dictionary<string, AppGroup>(StringComparer.OrdinalIgnoreCase);
            var extraGroups = new List<string>();
            foreach (string c in Categories.Order) byCat[c] = new AppGroup { Name = c, Dot = Categories.Tile(c, 0xFF) };
            foreach (PackageEntry p in lf.Packages)
            {
                string cat = p.Category ?? Categories.For(p.Id, p.IsWinget);
                var item = new AppItem
                {
                    Id = p.Id, Name = p.Name, Parameters = p.Parameters, DefaultOn = p.Enabled, Category = cat, IsWinget = p.IsWinget, IsGitHub = p.IsGitHub,
                    TileBrush = Categories.Tile(cat, 0x2E), TileForeground = Categories.Tile(cat, 0xFF)
                };
                bool pick;
                item.IsSelected = picks.TryGetValue(item.Key, out pick) ? pick : p.Enabled;
                item.Installed = installedKeys.Contains(item.Key);
                AppGroup g;
                if (!byCat.TryGetValue(cat, out g)) { g = new AppGroup { Name = cat, Dot = Categories.Tile(cat, 0xFF) }; byCat[cat] = g; extraGroups.Add(cat); }
                item.Changed = delegate { g.Refresh(); UpdateCount(); };
                g.Items.Add(item);
                apps.Add(item);
            }
            var order = new List<string>(Categories.Order); order.InsertRange(order.Count - 1, extraGroups);
            // Alphabetical within each category, whichever source (Chocolatey, winget, Store, GitHub) an app comes from.
            foreach (AppGroup g in byCat.Values)
            {
                var sorted = new List<AppItem>(g.Items);
                sorted.Sort(delegate (AppItem a, AppItem b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
                g.Items.Clear(); foreach (AppItem a in sorted) g.Items.Add(a);
            }
            foreach (string c in order) if (byCat[c].Items.Count > 0) groups.Add(byCat[c]);
            BuildRail();

            settingsProfiles.Clear();
            foreach (BackupProfile p in BackupCatalog.Load(Engine.CurrentListText(out external)))
            {
                var paths = new List<string>(); foreach (BackupRule rule in p.Rules) paths.Add(rule.Root + ":" + rule.Path);
                settingsProfiles.Add(new ProfileItem { Id = p.Id, Name = p.Name, Tooltip = string.Join("\n", paths.ToArray()) });
            }
            RefreshSettingsAvailability();

            savedProfiles = lf.Saved;
            applyingProfile = true;
            var box = Get<ComboBox>("ProfileBox");
            object previous = box.SelectedItem;
            box.Items.Clear();
            box.Items.Add("Custom selection");
            box.Items.Add("List defaults");
            foreach (SavedProfile s in savedProfiles) box.Items.Add(s.Name);
            box.SelectedIndex = previous != null && box.Items.Contains(previous) ? box.Items.IndexOf(previous) : 0;
            ApplyFilter();
            UpdateCount();
            applyingProfile = false;
            if (announce) Log(external ? "Loaded this session's list." : "Loaded the built-in defaults.");
        }

        void ApplyFilter()
        {
            string f = Get<TextBox>("SearchBox").Text.Trim();
            bool any = false;
            foreach (AppGroup g in groups)
            {
                bool vis = false;
                foreach (AppItem a in g.Items)
                {
                    bool m = f.Length == 0 || a.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || a.Id.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || a.Category.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (categoryFilter == "selected") m &= a.IsSelected;
                    else if (categoryFilter != "*") m &= string.Equals(a.Category, categoryFilter, StringComparison.OrdinalIgnoreCase);
                    a.Visibility = m ? Visibility.Visible : Visibility.Collapsed;
                    vis |= m;
                }
                g.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                any |= vis;
            }
            Get<StackPanel>("NoResults").Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            Get<TextBlock>("NoResultsText").Text = categoryFilter == "selected" && f.Length == 0 ? "No apps are selected yet." : "No apps match your search.";
        }

        // ---------------------------------------------------------------- category rail, card sizing, more menu
        void BuildRail()
        {
            rail.Clear();
            rail.Add(NewFilter("*", "All apps", (Brush)Window.FindResource("Subtle")));
            rail.Add(NewFilter("selected", "Selected", (Brush)Window.FindResource("Accent")));
            foreach (AppGroup g in groups) rail.Add(NewFilter(g.Name, g.Name, g.Dot));
            bool found = false;
            foreach (CategoryFilter c in rail) if (c.Key == categoryFilter) found = true;
            if (!found) categoryFilter = "*";
            foreach (CategoryFilter c in rail) if (c.Key == categoryFilter) c.IsChecked = true;
            UpdateRailCounts();
        }

        CategoryFilter NewFilter(string key, string name, Brush dot)
        {
            var f = new CategoryFilter { Key = key, Name = name, Dot = dot };
            f.Picked = delegate
            {
                if (categoryFilter == key) return;
                categoryFilter = key;
                foreach (CategoryFilter other in rail) if (other != f) other.IsChecked = false;
                ApplyFilter();
                Get<ScrollViewer>("AppScroll").ScrollToTop();
            };
            return f;
        }

        void PickCategory(string key)
        {
            foreach (CategoryFilter c in rail) if (c.Key == key) { c.IsChecked = true; return; }
        }

        void UpdateRailCounts()
        {
            int all = 0;
            foreach (AppItem a in apps) if (a.IsSelected) all++;
            foreach (CategoryFilter c in rail)
            {
                if (c.Key == "*") { c.Count = all + "/" + apps.Count; continue; }
                if (c.Key == "selected") { c.Count = all.ToString(); continue; }
                int n = 0, of = 0;
                foreach (AppItem a in apps) if (string.Equals(a.Category, c.Key, StringComparison.OrdinalIgnoreCase)) { of++; if (a.IsSelected) n++; }
                c.Count = n + "/" + of;
            }
        }

        /// Cards stretch to fill the row: as many 240px-or-wider columns as fit.
        void FitCards(double width)
        {
            if (width <= 0) return;
            const double gap = 10, min = 230;
            // Each card carries a right margin of gap, so a row needs columns * (card + gap).
            int columns = Math.Max(1, (int)Math.Floor(width / (min + gap)));
            double card = Math.Floor(width / columns - gap - 1);
            object current = Window.Resources["AppCardWidth"];
            if (!(current is double) || Math.Abs((double)current - card) >= 1) Window.Resources["AppCardWidth"] = card;
        }

        void OpenAppsMenu(Button anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            Action<string, string> add = delegate (string header, string button)
            {
                var item = new MenuItem { Header = header };
                item.Click += delegate { Get<Button>(button).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); };
                menu.Items.Add(item);
            };
            add("Save selection as session profile...", "BtnSaveProfile");
            add("Delete selected profile", "BtnDeleteProfile");
            menu.Items.Add(new Separator { Background = (Brush)Window.FindResource("Line"), Margin = new Thickness(6, 4, 6, 4) });
            add("Select every app shown", "BtnAll");
            add("Edit list...", "BtnEditList");
            add("Reset to built-in defaults", "BtnReload");
            menu.IsOpen = true;
        }

        void OnGroupToggle(object sender, RoutedEventArgs e)
        {
            var b = e.OriginalSource as Button;
            if (b == null || !"group-toggle".Equals(b.Tag)) return;
            var g = b.DataContext as AppGroup;
            if (g == null) return;
            bool all = true;
            foreach (AppItem a in g.Items) if (!a.IsSelected) all = false;
            Batch(delegate { foreach (AppItem a in g.Items) a.IsSelected = !all; });
        }

        void OnMenuItem(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            if (mi == null || !"remove-app".Equals(mi.Tag)) return;
            var item = mi.DataContext as AppItem;
            if (item == null) return;
            Confirm("Remove " + item.Name + " from this session?", "The app stays installed. Reopening Freshkit restores the built-in list.", "Remove", delegate
            {
                try { ListFile.RemovePackage(item.Source, item.Id); picks.Remove(item.Key); LoadList(false); Log("Removed " + item.Name + " from the list."); }
                catch (Exception ex) { Info("Couldn't update the session list", ex.Message); }
            });
        }

        void UpdateCount()
        {
            int n = 0, detected = 0;
            foreach (AppItem a in apps) if (a.IsSelected) { n++; if (a.Installed) detected++; }
            Get<TextBlock>("SelCount").Text = n + " of " + apps.Count + " apps selected" + (detected > 0 ? " · " + detected + " already installed" : "");
            UpdateRailCounts();
            int tweaks = debloat == null ? 0 : debloat.SelectedFeatures().Count, removals = debloat == null ? 0 : debloat.SelectedApps().Count;
            var extra = new List<string>();
            if (tweaks > 0) extra.Add(tweaks + " Windows tweak" + (tweaks == 1 ? "" : "s"));
            if (removals > 0) extra.Add(removals + " preinstalled app" + (removals == 1 ? "" : "s") + " to remove");
            if (Engine.KitDriverCount() > 0 && !online) extra.Add("offline kit drivers");
            Get<TextBlock>("SelExtra").Text = extra.Count > 0 ? "Plus " + string.Join(" · ", extra.ToArray()) : "No Windows tweaks selected (see Windows tweaks)";
            bool edge = Get<CheckBox>("ForceEdgeSwitch").IsChecked == true;
            Get<TextBlock>("InstallText").Text = "Review setup";
            if (!running) Get<Button>("BtnInstall").IsEnabled = n > 0 || tweaks + removals > 0 || edge;
            MarkCustom();
        }

        void MarkCustom()
        {
            if (applyingProfile) return;
            var box = Get<ComboBox>("ProfileBox");
            if (box.SelectedIndex != 0) { applyingProfile = true; box.SelectedIndex = 0; applyingProfile = false; }
        }

        string Selection(bool require)
        {
            var ids = new List<string>();
            foreach (AppItem a in apps) if (a.IsSelected) ids.Add(a.Key);
            if (ids.Count == 0) { if (require) Info("Nothing selected", "Pick at least one app first."); return null; }
            return string.Join(",", ids.ToArray());
        }

        string AllKeys()
        {
            var ids = new List<string>();
            foreach (AppItem a in apps) ids.Add(a.Key);
            return string.Join(",", ids.ToArray());
        }

        void EditList()
        {
            bool custom;
            var editor = new Window { Title = "Edit session list", Owner = Window, Width = 820, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Frozen("#13161C"), Foreground = Frozen("#E8EAED") };
            var panel = new DockPanel { Margin = new Thickness(16) };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
            var apply = new Button { Content = "Apply to this session", Padding = new Thickness(16, 8, 16, 8) };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(10, 0, 0, 0) };
            footer.Children.Add(apply); footer.Children.Add(cancel);
            var note = Note("Changes last until Freshkit closes. Built-in backup profiles remain available unless explicitly disabled with NOCONFIG.");
            DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
            var text = new TextBox { Text = Engine.CurrentListText(out custom), AcceptsReturn = true, AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Background = Frozen("#0E1014"), Foreground = Frozen("#E8EAED"), Padding = new Thickness(10) };
            panel.Children.Add(text); editor.Content = panel;
            cancel.Click += delegate { editor.Close(); };
            apply.Click += delegate
            {
                try { ListFile.Parse(text.Text); BackupCatalog.Load(text.Text); Engine.SessionList = text.Text; editor.Close(); LoadList(true); }
                catch (Exception ex) { MessageBox.Show(editor, ex.Message, "Check the list"); }
            };
            editor.ShowDialog();
        }

        // ---------------------------------------------------------------- profiles
        void ApplyProfileIndex(int index)
        {
            if (index <= 0) return;
            applyingProfile = true;
            try
            {
                if (index == 1) { foreach (AppItem a in apps) a.IsSelected = a.DefaultOn; Log("Selection reset to the list defaults."); return; }
                SavedProfile p = savedProfiles[index - 2];
                var set = new HashSet<string>(p.Apps, StringComparer.OrdinalIgnoreCase);
                foreach (AppItem a in apps) a.IsSelected = set.Contains(a.Key);
                if (debloat != null) { if (p.HasTweaks) debloat.ApplyFeatures(p.Tweaks); if (p.HasRemove) debloat.ApplyApps(p.Remove); RefreshTweakCounts(); }
                int missing = 0;
                foreach (string k in p.Apps) { bool found = false; foreach (AppItem a in apps) if (string.Equals(a.Key, k, StringComparison.OrdinalIgnoreCase)) found = true; if (!found) missing++; }
                Log("Applied profile " + p.Name + ": " + p.Apps.Count + " apps" + (p.HasTweaks || p.HasRemove ? ", " + p.Tweaks.Count + " tweaks, " + p.Remove.Count + " app removals" : " (Windows tweaks unchanged)") + (missing > 0 ? " (" + missing + " apps no longer in the list)" : "") + ".");
            }
            finally { UpdateCount(); applyingProfile = false; }
        }

        void PromptProfileName(Action after)
        {
            var box = new TextBox { Style = StyleOf("Input"), Tag = "Profile name, e.g. Gaming PC" };
            int sel = Get<ComboBox>("ProfileBox").SelectedIndex;
            if (sel >= 2 && sel - 2 < savedProfiles.Count) box.Text = savedProfiles[sel - 2].Name;
            int nTweaks = debloat == null ? 0 : debloat.SelectedFeatures().Count, nRemove = debloat == null ? 0 : debloat.SelectedApps().Count, nApps = 0;
            foreach (AppItem a in apps) if (a.IsSelected) nApps++;
            ShowDialog("Save session profile", "Remembers " + nApps + " apps, " + nTweaks + " tweaks and " + nRemove + " app removals until Freshkit closes. Reopening restores built-in defaults.", "Save", "Cancel", delegate
            {
                string name = box.Text.Trim();
                if (name.Length == 0) { Info("Name needed", "Give the profile a name."); return; }
                var p = new SavedProfile { Name = name };
                foreach (AppItem a in apps) if (a.IsSelected) p.Apps.Add(a.Key);
                if (debloat != null) { p.Tweaks = debloat.SelectedFeatures(); p.Remove = debloat.SelectedApps(); }
                try
                {
                    ListFile.SaveProfile(p);
                    LoadList(false);
                    var cb = Get<ComboBox>("ProfileBox");
                    applyingProfile = true;
                    for (int i = 2; i < cb.Items.Count; i++) if (string.Equals(cb.Items[i] as string, p.Name, StringComparison.OrdinalIgnoreCase)) cb.SelectedIndex = i;
                    applyingProfile = false;
                    Log("Saved profile " + p.Name + ".");
                    if (after != null) after();
                }
                catch (Exception ex) { Info("Couldn't save the session profile", ex.Message); }
            }, box);
            box.Focus();
        }

        // ---------------------------------------------------------------- search & add
        void OpenSearch()
        {
            Get<Grid>("SearchSheet").Visibility = Visibility.Visible;
            UpdateModalSurfaces();
            if (searchResults.Count == 0) SetSearchState("Search for an app", "Enter a name or package ID above. Add results to your selection for this session.", false, false);
            Get<TextBlock>("SheetStatus").Text = "Choose a source, then search. Adding an app selects it for setup; it does not install it.";
            Get<TextBox>("SheetQuery").Focus();
        }

        void CloseSearch()
        {
            searchRequest++;
            searchResults.Clear();
            Get<Button>("BtnSheetSearch").IsEnabled = true;
            Get<ProgressBar>("SearchBusy").Visibility = Visibility.Collapsed;
            Get<Grid>("SearchSheet").Visibility = Visibility.Collapsed;
            UpdateModalSurfaces();
            Get<Button>("BtnAddApps").Focus();
        }

        void SetSearchState(string title, string detail, bool busy, bool retry)
        {
            Get<StackPanel>("SearchState").Visibility = Visibility.Visible;
            Get<TextBlock>("SearchStateTitle").Text = title;
            Get<TextBlock>("SearchStateDetail").Text = detail;
            Get<ProgressBar>("SearchBusy").Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("BtnSearchRetry").Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
            Get<TextBlock>("SearchStateGlyph").Text = retry ? "\uE783" : "\uE721";
        }

        static string SourceName(string source)
        {
            return source == "all" ? "Chocolatey, winget, the Microsoft Store and GitHub" : new SearchResult { Source = source }.SourceLabel;
        }

        int BeginSearch(string query, string source)
        {
            int request = ++searchRequest;
            searchResults.Clear();
            Get<Button>("BtnSheetSearch").IsEnabled = false;
            SetSearchState("Searching " + SourceName(source), "Looking for \"" + query + "\"…", true, false);
            Get<TextBlock>("SheetStatus").Text = "Searching for \"" + query + "\". Press Enter to search again.";
            return request;
        }

        void CompleteSearch(int request, string query, string source, List<SearchResult> results, string error, string notes)
        {
            if (request != searchRequest || Get<Grid>("SearchSheet").Visibility != Visibility.Visible) return;
            Get<Button>("BtnSheetSearch").IsEnabled = true;
            Get<ProgressBar>("SearchBusy").Visibility = Visibility.Collapsed;
            if (error != null)
            {
                SetSearchState("Couldn't search " + SourceName(source), error, false, true);
                Get<TextBlock>("SheetStatus").Text = "Check your connection and try again.";
                return;
            }
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AppItem a in apps) known.Add(a.Key);
            foreach (SearchResult r in results) { r.Added = known.Contains(r.Key); searchResults.Add(r); }
            if (results.Count == 0) SetSearchState("No apps found", "No results for \"" + query + "\". Try a shorter name, a package ID or a GitHub owner/repo.", false, false);
            else Get<StackPanel>("SearchState").Visibility = Visibility.Collapsed;
            Get<TextBlock>("SheetStatus").Text = results.Count + " app" + (results.Count == 1 ? "" : "s") + " for \"" + query + "\", each from the source with its newest release. " +
                (string.IsNullOrEmpty(notes) ? "Added apps are selected on the Apps page." : notes);
        }

        void DoSearch()
        {
            string q = Get<TextBox>("SheetQuery").Text.Trim();
            if (q.Length < 2)
            {
                searchRequest++; searchResults.Clear();
                Get<Button>("BtnSheetSearch").IsEnabled = true;
                SetSearchState("Keep typing", "Enter at least two characters to search for an app.", false, false);
                Get<TextBlock>("SheetStatus").Text = "Search by app name or package ID.";
                return;
            }
            const string source = "all";
            int request = BeginSearch(q, source);
            new Thread(delegate ()
            {
                List<SearchResult> results = null; string error = null, notes = "";
                try
                {
                    results = Sources.SearchAll(q, out notes);
                }
                catch (Exception ex) { error = ex.Message; }
                Ui(delegate
                {
                    CompleteSearch(request, q, source, results, error, notes);
                });
            }) { IsBackground = true }.Start();
        }

        void OnAddResult(object sender, RoutedEventArgs e)
        {
            var b = e.OriginalSource as Button;
            if (b != null && "open-homepage".Equals(b.Tag))
            {
                var site = b.DataContext as SearchResult;
                Uri url;
                if (site != null && Uri.TryCreate(site.Homepage, UriKind.Absolute, out url) && url.Scheme == Uri.UriSchemeHttps)
                    try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception ex) { Get<TextBlock>("SheetStatus").Text = "Couldn't open the website: " + ex.Message; }
                return;
            }
            if (b == null || !"add-result".Equals(b.Tag)) return;
            var r = b.DataContext as SearchResult;
            if (r == null || r.Added) return;
            try
            {
                ListFile.AddPackage(r.Source, r.Id, r.Name);
                r.Added = true;
                picks[r.Key] = true;
                LoadList(false);
                Log("Added " + r.Name + " to this session's list.");
            }
            catch (Exception ex) { Get<TextBlock>("SheetStatus").Text = "Couldn't add it: " + ex.Message; }
        }

        // ---------------------------------------------------------------- pick sheet
        void ShowPick(string title, string text, IEnumerable<PickItem> items, string ok, Action<List<PickItem>> onOk)
        {
            pickItems.Clear();
            foreach (PickItem p in items) pickItems.Add(p);
            Get<TextBlock>("PickTitle").Text = title;
            Get<TextBlock>("PickText").Text = text;
            Get<Button>("PickOk").Content = ok;
            pickOk = onOk;
            Get<Grid>("PickSheet").Visibility = Visibility.Visible;
        }

        // ---------------------------------------------------------------- copy this PC
        void ScanThisPc()
        {
            Get<Button>("BtnScan").IsEnabled = false;
            Get<TextBlock>("ScanSummary").Text = "Scanning this PC...";
            SetStatus("Scanning installed apps (Chocolatey, winget, Windows)...", DotBusy);
            new Thread(delegate ()
            {
                Dictionary<string, string> choco = null; List<InstalledApp> winget = null; List<string> programs = null; string error = null;
                try { choco = Sources.ChocoInstalled(); } catch (Exception ex) { error = "Chocolatey: " + ex.Message; choco = new Dictionary<string, string>(); }
                try { winget = Sources.WingetInstalled(); } catch (Exception ex) { error = "winget: " + ex.Message; winget = new List<InstalledApp>(); }
                try { programs = Sources.RegistryProgramNames(); } catch { programs = new List<string>(); }
                Ui(delegate { ScanDone(choco, winget, programs, error); });
            }) { IsBackground = true }.Start();
        }

        void ScanDone(Dictionary<string, string> choco, List<InstalledApp> winget, List<string> programs, string error)
        {
            Get<Button>("BtnScan").IsEnabled = true;
            var normalized = new HashSet<string>();
            foreach (string p in programs) normalized.Add(Data.Norm(p));
            foreach (InstalledApp w in winget) normalized.Add(Data.Norm(w.Name));
            var wingetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InstalledApp w in winget) wingetIds.Add(w.Id);

            installedKeys.Clear();
            int hit = 0;
            foreach (AppItem a in apps)
            {
                bool inst = a.IsWinget ? wingetIds.Contains(a.Id) : choco.ContainsKey(a.Id);
                if (!inst && !"Runtimes".Equals(a.Category)) inst = Sources.NameMatches(a.Name, a.Id, normalized);
                a.Installed = inst;
                if (inst) { installedKeys.Add(a.Key); hit++; }
            }
            Get<TextBlock>("ScanSummary").Text = hit + " of " + apps.Count + " listed apps are installed here";
            UpdateCount();

            // winget packages on this PC that aren't in the list yet
            var listNames = new HashSet<string>();
            foreach (AppItem a in apps) { listNames.Add(Data.Norm(a.Name)); if (a.IsWinget) listNames.Add(a.Id.ToLowerInvariant()); }
            var others = new List<PickItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InstalledApp w in winget)
            {
                if (w.Id.Length == 0 || !seen.Add(w.Id) || listNames.Contains(w.Id.ToLowerInvariant())) continue;
                string n = Data.Norm(w.Name);
                bool known = false;
                foreach (AppItem a in apps) { string an = Data.Norm(a.Name); if (n.Length >= 4 && (n == an || (an.Length >= 5 && n.StartsWith(an)) || (n.Length >= 5 && an.StartsWith(n)))) { known = true; break; } }
                if (known) continue;
                bool systemish = System.Text.RegularExpressions.Regex.IsMatch(w.Id, @"^(Microsoft\.(VCRedist|DotNet|WindowsAppRuntime|EdgeWebView2|AppInstaller|UI\.Xaml|VCLibs|Edge$|Teams|OneDrive)|Nvidia\.|AMD\.|Intel\.|Realtek\.|Dell\.|HP\.|Lenovo\.|ASUS\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                others.Add(new PickItem { Name = w.Name, Detail = w.Id + (w.Version.Length > 0 ? "  ·  v" + w.Version : "") + (systemish ? "  ·  system/driver component" : ""), Tag = w, IsSelected = !systemish });
            }
            others.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
            SetStatus("Scan complete: " + hit + " of " + apps.Count + " listed apps installed, " + others.Count + " other winget apps found.", DotOk);
            if (error != null) Log("Scan note: " + error);

            var selectInstalled = Switch("Select only the apps that are installed on this PC (" + hit + ")", true);
            var reviewOthers = Switch("Review the " + others.Count + " other apps found and add them to the list", others.Count > 0);
            reviewOthers.IsEnabled = others.Count > 0;
            ShowDialog("Copy this PC",
                hit + " of your " + apps.Count + " listed apps are installed here (marked \"installed\"). Turn that into a profile you can install on the next PC.",
                "Continue", "Close", delegate
                {
                    if (selectInstalled.IsChecked == true) Batch(delegate { foreach (AppItem a in apps) a.IsSelected = a.Installed; });
                    if (reviewOthers.IsChecked == true && others.Count > 0)
                        ShowPick("Add apps found on this PC", "These winget apps are installed here but aren't in your list. The ticked ones are added (as winget apps) and selected.", others, "Add to list", delegate (List<PickItem> chosen)
                        {
                            int added = 0;
                            foreach (PickItem p in chosen)
                            {
                                var w = (InstalledApp)p.Tag;
                                if (!System.Text.RegularExpressions.Regex.IsMatch(w.Id, @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z")) continue;
                                try { if (ListFile.AddPackage(true, w.Id, w.Name)) added++; picks["winget:" + w.Id] = true; installedKeys.Add("winget:" + w.Id); }
                                catch (Exception ex) { Log("Couldn't add " + w.Id + ": " + ex.Message); break; }
                            }
                            LoadList(false);
                            Log("Added " + added + " apps from this PC to the list.");
                            OfferProfileSave();
                        });
                    else OfferProfileSave();
                }, selectInstalled, reviewOthers);
        }

        void OfferProfileSave()
        {
            Confirm("Save this selection as a session profile?", "You can reuse it while Freshkit stays open. Closing Freshkit discards session profiles.", "Save profile", delegate { PromptProfileName(null); });
        }

        // ---------------------------------------------------------------- install plan
        string RestoreCandidate()
        {
            try
            {
                string typed = Get<TextBox>("ArchiveBox").Text.Trim().Trim('"');
                if (typed.Length > 0 && File.Exists(typed)) return typed;
                FileInfo newest = null;
                foreach (FileInfo f in new DirectoryInfo(Engine.ExeDir).GetFiles("settings-backup-*.zip"))
                    if (!f.Name.Contains("preview") && (newest == null || f.LastWriteTimeUtc > newest.LastWriteTimeUtc)) newest = f;
                return newest == null ? null : newest.FullName;
            }
            catch { return null; }
        }

    }
}
