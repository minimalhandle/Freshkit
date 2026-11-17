// Freshkit - package search, installed-app detection and available updates (Chocolatey + winget).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using Microsoft.Win32;

namespace Freshkit
{
    public sealed class SearchResult : Notifier
    {
        AppIcon artwork;
        // Store apps share the winget icon cache because they are added as winget entries.
        // Only the result attributed as installed (see AttributeInstalled) may borrow the installed app's icon.
        public AppIcon Artwork { get { return artwork ?? (artwork = AppIcons.Get(Id, Name, Source == "store" ? "winget" : Source, Source != "github" && InstalledVersion != null)); } }
        bool added;
        public string Source { get; set; }   // choco | winget | store | github
        public string Key { get { return Data.KeyFor(Source, Id); } }
        /// Ranking inputs for "All sources": Chocolatey downloads or GitHub stars.
        public long Popularity { get; set; }
        public double Score { get; set; }
        public bool IsBest { get; set; }
        public Visibility BestVisibility { get { return IsBest ? Visibility.Visible : Visibility.Collapsed; } }
        public string SourceTip
        {
            get
            {
                return Source == "github" ? "Installs the latest release's installer directly from GitHub. GitHub releases aren't reviewed by a package repository, so add projects you trust."
                    : Source == "store" ? "Installed from the Microsoft Store (through winget), which always delivers the publisher's current version."
                    : Source == "winget" ? "Installed with winget from Microsoft's community repository." : "Installed with Chocolatey from the community repository.";
            }
        }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Meta { get; set; }
        public string Summary { get; set; }
        /// Publisher or author, when the source provides one.
        public string Publisher { get; set; }
        /// https link to the app's website or listing.
        public string Homepage { get; set; }
        internal InstalledMatch InstalledMatch;
        public string IdentityNote { get { return InstalledMatch == null ? "" : InstalledMatch.Reason; } }
        public Visibility LinkVisibility { get { return Source == "github" && InstalledMatch != null && InstalledMatch.Record != null && !InstalledMatch.Confirmed ? Visibility.Visible : Visibility.Collapsed; } }
        string installedVersion;
        /// Version found on this PC ("" when installed but unversioned; null when not installed).
        public string InstalledVersion { get { return installedVersion; } set { installedVersion = value; Raise("InstalledText"); Raise("InstalledVisibility"); } }
        public string InstalledText { get { return string.IsNullOrEmpty(installedVersion) ? "Installed" : "Installed v" + installedVersion.TrimStart('v', 'V'); } }
        public Visibility InstalledVisibility { get { return installedVersion != null ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility HomepageVisibility { get { return (Homepage ?? "").StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility PublisherVisibility { get { return string.IsNullOrEmpty(Publisher) ? Visibility.Collapsed : Visibility.Visible; } }
        public string SourceLabel { get { return Source == "github" ? "GitHub" : Source == "store" ? "Microsoft Store" : Source == "winget" ? "winget" : "Chocolatey"; } }
        public bool Added { get { return added; } set { added = value; Raise("ButtonText"); Raise("CanAdd"); } }
        public string ButtonText { get { return added ? "Added" : "Add"; } }
        public bool CanAdd { get { return !added; } }
    }

    public sealed class UpdateItem : Notifier
    {
        bool selected = true;
        public string Source { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Current { get; set; }
        public string Available { get; set; }
        public string VersionText { get { return Current + "  →  " + Available; } }
        public string SourceLabel { get { return Source == "github" ? "GitHub" : Source == "store" ? "Microsoft Store" : Source == "winget" ? "winget" : "Chocolatey"; } }
        public bool IsSelected { get { return selected; } set { selected = value; Raise("IsSelected"); } }
    }

    internal sealed class InstalledApp { public string Id, Name, Version; }

    internal static class Sources
    {
        const string WingetCommon = " --source winget --accept-source-agreements --disable-interactivity";

        // ---------------------------------------------------------------- Chocolatey community repository
        public static List<SearchResult> SearchChocolatey(string query)
        {
            string term = "'" + query.Replace("'", "''") + "'";
            string url = "https://community.chocolatey.org/api/v2/Search()?$filter=IsLatestVersion&$orderby=DownloadCount%20desc&$top=40" +
                         "&searchTerm=" + Uri.EscapeDataString(term) + "&targetFramework=%27%27&includePrerelease=false";
            Data.EnsureModernTls();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Freshkit/2.0";
            request.Timeout = 30000;
            var doc = new XmlDocument { XmlResolver = null };
            using (SearchIO.AbortOnCancel(request))
            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc.Load(reader);
            var list = new List<SearchResult>();
            foreach (XmlNode entry in doc.GetElementsByTagName("entry"))
            {
                string id = Child(entry, "title");
                XmlNode props = null;
                foreach (XmlNode c in entry.ChildNodes) if (c.LocalName == "properties") props = c;
                if (id.Length == 0 || props == null) continue;
                string title = Child(props, "Title"), version = Child(props, "Version"), summary = Child(entry, "summary");
                long downloads; long.TryParse(Child(props, "DownloadCount"), out downloads);
                list.Add(new SearchResult
                {
                    Source = "choco", Id = id, Name = title.Length > 0 ? title : id, Version = version, Popularity = downloads,
                    Publisher = Child(props, "Authors"),
                    Homepage = Child(props, "ProjectUrl").StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? Child(props, "ProjectUrl") : "https://community.chocolatey.org/packages/" + Uri.EscapeDataString(id),
                    Meta = id + "  ·  v" + version + "  ·  " + Downloads(downloads) + " downloads",
                    Summary = summary.Replace('\n', ' ').Replace('\r', ' ')
                });
            }
            return list;
        }

        static string Child(XmlNode node, string localName)
        {
            foreach (XmlNode c in node.ChildNodes) if (c.LocalName == localName) return c.InnerText.Trim();
            return "";
        }

        static string Downloads(long n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (n >= 1000) return (n / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- GitHub releases
        static object GitHubJson(string url)
        {
            Data.EnsureModernTls();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Freshkit/3.0"; request.Accept = "application/vnd.github+json"; request.Timeout = 20000;
            try
            {
                using (SearchIO.AbortOnCancel(request))
            using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return Engine.Json.DeserializeObject(reader.ReadToEnd());
            }
            catch (WebException ex)
            {
                var http = ex.Response as HttpWebResponse;
                if (http != null && (int)http.StatusCode == 404) return null;
                if (http != null && ((int)http.StatusCode == 403 || (int)http.StatusCode == 429))
                    throw new InvalidOperationException("GitHub's search limit for this network was reached. Wait a minute, then try again.");
                throw;
            }
        }

        /// Same choice the engine makes: an x64/neutral MSI, then an installer-looking EXE.
        internal static string PickInstallerAsset(IEnumerable<string> names)
        {
            string best = null; int bestRank = int.MaxValue;
            foreach (string name in names)
            {
                if (!Regex.IsMatch(name, @"\.(msi|exe)\z", RegexOptions.IgnoreCase)) continue;
                if (Regex.IsMatch(name, @"(arm64|aarch64|i[36]86|ia32|win32|x86(?![-_]?64)|portable|debug|symbols|uninstall)", RegexOptions.IgnoreCase)) continue;
                int rank = (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? 0 : Regex.IsMatch(name, "(setup|install)", RegexOptions.IgnoreCase) ? 10 : 20) +
                           (Regex.IsMatch(name, "(x64|amd64|win64|x86_64)", RegexOptions.IgnoreCase) ? 0 : 1);
                if (rank < bestRank) { best = name; bestRank = rank; }
            }
            return best;
        }

        internal sealed class GitHubRelease { public DateTime FetchedUtc = DateTime.UtcNow; public string Tag; public List<string> Assets = new List<string>(); }
        static readonly Dictionary<string, GitHubRelease> gitHubReleases = new Dictionary<string, GitHubRelease>(StringComparer.OrdinalIgnoreCase);

        /// Latest release from github.com's own pages (the /releases/latest redirect and the expanded_assets list).
        /// Unlike api.github.com, these don't count against the 60-requests-an-hour limit per network. Cached per session.
        internal static GitHubRelease GitHubLatest(string repo, bool refresh = false)
        {
            if (!Data.IsGitHubRepo(repo)) return null;
            lock (gitHubReleases) { GitHubRelease cached; if (!refresh && gitHubReleases.TryGetValue(repo, out cached) && DateTime.UtcNow - cached.FetchedUtc < TimeSpan.FromMinutes(5)) return cached; }
            Data.EnsureModernTls();
            var request = (HttpWebRequest)WebRequest.Create("https://github.com/" + repo + "/releases/latest");
            request.AllowAutoRedirect = false; request.UserAgent = "Freshkit/3.0"; request.Timeout = 20000;
            string location = null;
            try { using (SearchIO.AbortOnCancel(request)) using (var response = (HttpWebResponse)request.GetResponse()) location = response.Headers["Location"]; }
            catch (WebException ex) { SearchIO.Token.ThrowIfCancellationRequested(); var http = ex.Response as HttpWebResponse; if (http == null || http.StatusCode != HttpStatusCode.NotFound) throw; }
            GitHubRelease release = null;
            Match tag = Regex.Match(location ?? "", @"/releases/tag/(?<tag>[^/?#]+)\z");
            if (tag.Success)
            {
                release = new GitHubRelease { Tag = Uri.UnescapeDataString(tag.Groups["tag"].Value) };
                string html = HttpText("https://github.com/" + repo + "/releases/expanded_assets/" + Uri.EscapeDataString(release.Tag), null);
                string prefix = "/" + repo + "/releases/download/";
                foreach (Match link in Regex.Matches(html, "href=\"(?<href>/[^\"]+/releases/download/[^\"]+)\""))
                {
                    string href = WebUtility.HtmlDecode(link.Groups["href"].Value);
                    if (href.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) release.Assets.Add(Uri.UnescapeDataString(href.Substring(href.LastIndexOf('/') + 1)));
                }
            }
            if (release != null) lock (gitHubReleases) gitHubReleases[repo] = release;
            return release;
        }

        static SearchResult GitHubResult(object repo)
        {
            string full = J.S(repo, "full_name");
            if (!Data.IsGitHubRepo(full) || J.B(repo, "archived")) return null;
            GitHubRelease release = GitHubLatest(full);
            if (release == null) return null;
            string asset = PickInstallerAsset(release.Assets);
            if (asset == null) return null;
            long stars; long.TryParse(J.S(repo, "stargazers_count"), out stars);
            string tag = release.Tag;
            return new SearchResult
            {
                Source = "github", Id = full, Name = J.S(repo, "name"), Version = tag.TrimStart('v', 'V'), Popularity = stars,
                Publisher = full.Substring(0, full.IndexOf('/')), Homepage = "https://github.com/" + full,
                Meta = full + "  ·  " + tag + (stars > 0 ? "  ·  " + Downloads(stars) + " stars" : "") + "  ·  " + asset,
                Summary = J.S(repo, "description").Replace('\n', ' ').Replace('\r', ' ')
            };
        }

        /// Finds GitHub projects whose latest release has a Windows installer. Accepts a name, owner/repo or a github.com URL.
        public static List<SearchResult> SearchGitHub(string query)
        {
            var list = new List<SearchResult>();
            Match direct = Regex.Match(query.Trim(), @"\A(?:https?://)?(?:www\.)?(?:github\.com/)?(?<repo>[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9._-]+?)(?:\.git)?/?\z");
            var repos = new List<object>();
            if (direct.Success && direct.Groups["repo"].Value.Contains("/"))
            {
                // A pasted owner/repo needs no search. Stars and description are optional extras from the API;
                // without them (rate limit) the release page alone is enough.
                string full = direct.Groups["repo"].Value;
                object repo = null;
                try { repo = GitHubJson("https://api.github.com/repos/" + full); } catch (Exception) { }
                if (repo == null && GitHubLatest(full) != null)
                    repo = new Dictionary<string, object> { { "full_name", full }, { "name", full.Substring(full.IndexOf('/') + 1) } };
                if (repo != null) repos.Add(repo);
            }
            if (repos.Count == 0)
            {
                object found = GitHubJson("https://api.github.com/search/repositories?per_page=8&sort=stars&order=desc&q=" + Uri.EscapeDataString(query.Replace("/", " ") + " in:name"));
                var items = J.Get(found, "items") as object[];
                if (items != null) foreach (object r in items) { if (repos.Count >= 6) break; if (!J.B(r, "fork")) repos.Add(r); }
            }
            // Bounded child lookups inherit the search cancellation token.
            var results = new SearchResult[repos.Count]; var token = SearchIO.Token;
            var errors = new List<Exception>();
            Parallel.For(0, repos.Count, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = token }, delegate(int index) {
                try { results[index] = SearchIO.Run(token, delegate { return GitHubResult(repos[index]); }); }
                catch (Exception ex) { lock (errors) errors.Add(ex); }
            });
            token.ThrowIfCancellationRequested();
            foreach (SearchResult r in results) if (r != null) list.Add(r);
            if (errors.Count > 0) throw new PartialSearchException(list, errors.Count + " release lookup(s) failed: " + errors[0].Message);
            return list;
        }

        // ---------------------------------------------------------------- Microsoft Store
        static readonly Dictionary<string, string> storeLogos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> storeFamilies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string StoreMarket()
        {
            try { string region = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName; return Regex.IsMatch(region, "^[A-Z]{2}$") ? region : "US"; }
            catch (Exception) { return "US"; }
        }

        static string HttpText(string url, string postJson)
        {
            Data.EnsureModernTls();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Freshkit/3.0"; request.Timeout = 20000;
            using (SearchIO.AbortOnCancel(request))
            {
            if (postJson != null)
            {
                request.Method = "POST"; request.ContentType = "application/json";
                byte[] body = Encoding.UTF8.GetBytes(postJson);
                using (Stream s = request.GetRequestStream()) s.Write(body, 0, body.Length);
            }
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        /// Store details from the display catalog: title, publisher, description, newest package version and logo.
        internal static List<SearchResult> StoreDetails(IList<string> ids)
        {
            var list = new List<SearchResult>();
            if (ids.Count == 0) return list;
            string json = HttpText("https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=" + Uri.EscapeDataString(string.Join(",", ids.ToArray())) +
                "&market=" + StoreMarket() + "&languages=en-US,neutral", null);
            var products = J.Get(Engine.Json.DeserializeObject(json), "Products") as object[];
            if (products == null) return list;
            foreach (object product in products)
            {
                string id = J.S(product, "ProductId");
                var locales = J.Get(product, "LocalizedProperties") as object[];
                if (!Data.IsStoreId(id) || locales == null || locales.Length == 0) continue;
                object l = locales[0];
                // Package names carry versions, e.g. AppleInc.iCloud_15.8.118.0_x64__nzyj5cx40ttqa. Keep the newest desktop build.
                string version = "";
                foreach (Match m in Regex.Matches(Engine.Json.Serialize(J.Get(product, "DisplaySkuAvailabilities")), @"_(\d+\.\d+\.\d+\.\d+)_(?:x64|neutral|x86)_"))
                {
                    int? c = Data.CompareVersions(m.Groups[1].Value, version);
                    if (version.Length == 0 || (c.HasValue && c.Value > 0)) version = m.Groups[1].Value;
                }
                string logo = ""; int logoWidth = 0;
                var images = J.Get(l, "Images") as object[];
                if (images != null)
                    foreach (object image in images)
                    {
                        string purpose = J.S(image, "ImagePurpose"); int width = J.I(image, "Width", 0);
                        if (purpose != "Logo" && purpose != "Tile" && purpose != "BoxArt") continue;
                        // Prefer a logo near 100-300px; tiles and box art only when there is no logo.
                        int rank = (purpose == "Logo" ? 0 : 1000) + (width >= 96 && width <= 300 ? 0 : 500) + Math.Abs(150 - width);
                        if (logo.Length == 0 || rank < logoWidth) { logo = "https:" + J.S(image, "Uri"); logoWidth = rank; }
                    }
                if (logo.StartsWith("https://")) lock (storeLogos) storeLogos[id] = logo;
                string family = J.S(J.Get(product, "Properties"), "PackageFamilyName");
                if (family.Length > 0) lock (storeFamilies) storeFamilies[id] = family;
                string publisher = J.S(l, "PublisherName"), title = J.S(l, "ProductTitle");
                list.Add(new SearchResult
                {
                    Source = "store", Id = id, Name = title.Length > 0 ? title : id, Version = version,
                    Publisher = publisher, Homepage = "https://apps.microsoft.com/detail/" + id,
                    Meta = id + (version.Length > 0 ? "  ·  v" + version : "") + (publisher.Length > 0 ? "  ·  " + publisher : ""),
                    Summary = Regex.Replace(J.S(l, "ShortDescription"), @"\s+", " ").Trim()
                });
            }
            return list;
        }

        /// Logo for a Store product (cached per session); null when the Store has none.
        internal static string StoreLogoUrl(string id)
        {
            string url;
            lock (storeLogos) if (storeLogos.TryGetValue(id, out url)) return url;
            try { StoreDetails(new[] { id }); } catch (Exception) { }
            lock (storeLogos) return storeLogos.TryGetValue(id, out url) ? url : null;
        }

        /// Searches the Microsoft Store catalog directly (no winget needed) and adds versions and logos.
        public static List<SearchResult> SearchStore(string query)
        {
            string body = "{\"Query\":{\"KeyWord\":" + Engine.Json.Serialize(query) + ",\"MatchType\":\"Substring\"},\"MaximumResults\":12}";
            var data = J.Get(Engine.Json.DeserializeObject(HttpText("https://storeedgefd.dsx.mp.microsoft.com/v9.0/manifestSearch", body)), "Data") as object[];
            var ids = new List<string>();
            if (data != null) foreach (object d in data) { string id = J.S(d, "PackageIdentifier"); if (Data.IsStoreId(id) && !ids.Contains(id)) ids.Add(id); }
            var details = StoreDetails(ids);
            // Keep the Store's own relevance order.
            details.Sort(delegate (SearchResult a, SearchResult b) { return ids.IndexOf(a.Id).CompareTo(ids.IndexOf(b.Id)); });
            return details;
        }

        // ---------------------------------------------------------------- all sources
        /// One search across Chocolatey, winget, the Microsoft Store and GitHub. Each app is shown once, from the
        /// source with its newest release.
        public static List<SearchResult> SearchAll(string query, out string notes)
        {
            List<SearchResult> result = null; string detail = "";
            SearchAll(query, CancellationToken.None, delegate(List<SearchResult> items, string state, bool final) { result = items; detail = state; });
            notes = detail; return result ?? new List<SearchResult>();
        }

        internal static void SearchAll(string query, CancellationToken token, Action<List<SearchResult>, string, bool> publish)
        {
            var snapshot = InstalledSnapshot.Capture();
            SearchCoordinator.Run(new Dictionary<string, Func<CancellationToken, List<SearchResult>>> {
                { "Chocolatey", delegate { return SearchChocolatey(query); } },
                { "winget", delegate { return SearchWinget(query); } },
                { "Microsoft Store", delegate { return SearchStore(query); } },
                { "GitHub", delegate { return SearchGitHub(query); } }
            }, 30000, token, delegate(SearchBatch batch) {
                var copy = new List<SearchResult>();
                foreach (var r in batch.Results) copy.Add(new SearchResult { Source = r.Source, Id = r.Id, Name = r.Name, Version = r.Version, Meta = r.Meta, Summary = r.Summary, Publisher = r.Publisher, Homepage = r.Homepage, Popularity = r.Popularity });
                var ranked = Rank(query, copy); MarkInstalled(ranked, snapshot); PickBest(ranked);
                if (!batch.IsFinal) foreach (var r in ranked) r.IsBest = false;
                var errors = new List<string>(); foreach (var pair in batch.Errors) errors.Add(pair.Key + ": " + pair.Value);
                string notes = errors.Count > 0 ? "Search incomplete. " + string.Join("; ", errors.ToArray()) : "";
                publish(ranked, notes, batch.IsFinal);
            });
        }
        /// Same app across sources: drops architecture/installer qualifiers but keeps words such as "(Unofficial)".
        internal static string AppKey(string name)
        {
            string s = Regex.Replace((name ?? "").ToLowerInvariant(), @"\((?:x64|x86|64-bit|32-bit|install|portable|msi)[^)]*\)", " ");
            return Regex.Replace(s, "[^a-z0-9]", "");
        }

        static string ChocoBase(string id) { return Regex.Replace((id ?? "").ToLowerInvariant(), @"\.(install|portable|app|commandline)\z", ""); }

        static int Trust(SearchResult r) { return r.Source == "winget" ? 3 : r.Source == "choco" ? 2 : r.Source == "github" ? 1 : 0; }

        /// The Microsoft Store is a last resort: it is used only when it is the only source, or when its release is
        /// provably newer at the major/minor level (e.g. iCloud 15.8 vs 7.21). Same or unknown versions, and
        /// build-number differences such as GIMP's Store packaging 3.2.80 vs 3.2.8, keep the regular installer.
        static bool StoreClearlyNewer(SearchResult store, SearchResult other)
        {
            int[] s = Data.VersionParts(store.Version), o = Data.VersionParts(other.Version);
            if (s == null || o == null || !Data.CompareVersions(store.Version, other.Version).HasValue) return false;
            for (int i = 0; i < 2; i++)
            {
                int a = i < s.Length ? s[i] : 0, b = i < o.Length ? o[i] : 0;
                if (a != b) return a > b;
            }
            return false;
        }

        /// True when candidate should replace current as the source shown for the same app.
        static bool Newer(SearchResult candidate, SearchResult current)
        {
            if (candidate.Source == "store" && current.Source != "store") return StoreClearlyNewer(candidate, current);
            if (current.Source == "store" && candidate.Source != "store") return !StoreClearlyNewer(current, candidate);
            bool hasC = Data.VersionParts(candidate.Version) != null, hasP = Data.VersionParts(current.Version) != null;
            int? c = Data.CompareVersions(candidate.Version, current.Version);
            if (c.HasValue && c.Value != 0) return c.Value > 0;
            if (!c.HasValue && hasC != hasP) return hasC;          // a known version beats an unknown one
            return Trust(candidate) > Trust(current) || (Trust(candidate) == Trust(current) && candidate.Score > current.Score);
        }

        internal static List<SearchResult> Rank(string query, List<SearchResult> all)
        {
            string q = Data.Norm(query);
            foreach (SearchResult r in all) r.Score = ScoreOf(q, r);
            all.Sort(delegate (SearchResult a, SearchResult b) { int c = b.Score.CompareTo(a.Score); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            // Group the best-scoring result per source for each app; extra same-source namesakes are different projects.
            var groups = new Dictionary<string, List<SearchResult>>();
            var order = new List<object>();
            foreach (SearchResult r in all)
            {
                string key = AppKey(r.Name);
                List<SearchResult> g;
                // GitHub projects often share names with unrelated apps, so they are never merged with other sources.
                if (key.Length == 0 || r.Source == "github") { order.Add(r); continue; }
                if (!groups.TryGetValue(key, out g)) { g = new List<SearchResult>(); groups[key] = g; order.Add(g); }
                SearchResult same = g.Find(x => x.Source == r.Source);
                if (same == null) g.Add(r);
                // Chocolatey's vlc / vlc.install / vlc.portable are one app: keep the best-scoring variant.
                else if (r.Source == "choco" && ChocoBase(r.Id) == ChocoBase(same.Id)) continue;
                else order.Add(r);
            }
            var ranked = new List<SearchResult>();
            foreach (object entry in order)
            {
                var single = entry as SearchResult;
                if (single != null) { ranked.Add(single); continue; }
                var g = (List<SearchResult>)entry;
                SearchResult pick = g[0];
                foreach (SearchResult r in g) if (r != pick && Newer(r, pick)) pick = r;
                pick.Score = g[0].Score;   // the app ranks by its best name match
                foreach (SearchResult r in g)
                    if (r != pick) pick.Meta += "  ·  also on " + r.SourceLabel + (r.Version != null && r.Version.Length > 0 ? " (v" + r.Version.TrimStart('v', 'V') + ")" : "");
                ranked.Add(pick);
            }
            ranked.Sort(delegate (SearchResult a, SearchResult b) { int c = b.Score.CompareTo(a.Score); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            if (ranked.Count > 40) ranked.RemoveRange(40, ranked.Count - 40);
            PickBest(ranked);
            return ranked;
        }

        /// Marks the top result as the best match, unless a same-named project from the same source makes it a guess
        /// (GitHub namesakes such as panzergen/AfterClip and minimalhandle/Afterclip). When exactly one of the
        /// namesakes is installed on this PC, that one is moved to the top and marked instead.
        internal static void PickBest(List<SearchResult> ranked)
        {
            foreach (SearchResult r in ranked) r.IsBest = false;
            if (ranked.Count == 0 || ranked[0].Score < 80) return;
            string key = NameKey(ranked[0].Name);
            List<SearchResult> namesakes = ranked.FindAll(r => r.Score >= 80 && NameKey(r.Name) == key);
            List<SearchResult> installed = namesakes.FindAll(r => r.InstalledVersion != null);
            if (namesakes.Count > 1 && installed.Count == 1)
            {
                ranked.Remove(installed[0]); ranked.Insert(0, installed[0]);
                installed[0].IsBest = true;
                return;
            }
            SearchResult top = ranked[0];
            if (!namesakes.Exists(r => r != top && r.Source == top.Source)) top.IsBest = true;
        }

        static double ScoreOf(string q, SearchResult r)
        {
            string name = Data.Norm(r.Name), id = Data.Norm(r.Id.Contains("/") ? r.Id.Substring(r.Id.LastIndexOf('/') + 1) : r.Id);
            // The app's name counts slightly more than its package or repository ID.
            if (Data.Norm(r.Id) == q && r.Id.Contains("/")) id = q;   // pasted owner/repo or link
            // An exact name must match including qualifiers like "(Unofficial)", which Norm drops.
            bool exactName = name == q && AppKey(r.Name) == q;
            double score = exactName ? 100 : id == q ? 92 : name.StartsWith(q) ? 80 : id.StartsWith(q) ? 75 : name.Contains(q) || id.Contains(q) ? 60 : 30;
            // Package repositories and the Store review submissions; GitHub releases are trusted only as far as their project.
            score += r.Source == "winget" ? 6 : r.Source == "choco" ? 4 : r.Source == "store" ? 2 : 0;
            if (r.Source == "choco") score += Math.Min(12, Math.Log10(r.Popularity + 1) * 2);
            else if (r.Source == "github") score += Math.Min(12, Math.Log10(r.Popularity + 1) * 3);
            else score += 6;
            return score;
        }

        // ---------------------------------------------------------------- installed versions and Store/GitHub updates
        static string NameKey(string name) { return Regex.Replace(Regex.Replace((name ?? "").ToLowerInvariant(), @"\([^)]*\)", " "), "[^a-z0-9]", ""); }

        /// Version of an app in Installed apps, matched by display name (trailing version numbers ignored), the same way
        /// the engine checks GitHub apps. "" when installed without a version; null when not installed.
        internal static string InstalledVersion(string name)
        {
            string wanted = NameKey(name);
            if (wanted.Length < 3) return null;
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey keys = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (keys == null) continue;
                            foreach (string sub in keys.GetSubKeyNames())
                                using (RegistryKey key = keys.OpenSubKey(sub))
                                {
                                    string display = key == null ? null : key.GetValue("DisplayName") as string;
                                    if (string.IsNullOrEmpty(display)) continue;
                                    if (NameKey(Regex.Replace(display, @"\s+v?\d+(\.\d+)+.*$", "")) == wanted) return (key.GetValue("DisplayVersion") as string) ?? "";
                                }
                        }
                    }
                    catch (Exception) { }
                }
            return null;
        }

        /// Installed version of a Store (MSIX) app, from the per-user package repository; null when not installed.
        internal static string StoreInstalledVersion(string family)
        {
            if (string.IsNullOrEmpty(family) || family.IndexOf('_') < 0) return null;
            string name = family.Substring(0, family.IndexOf('_')), publisherId = family.Substring(family.LastIndexOf('_') + 1), best = null;
            try
            {
                using (RegistryKey keys = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                    if (keys != null)
                        foreach (string full in keys.GetSubKeyNames())
                        {
                            // Name_Version_Architecture_ResourceId_PublisherId
                            string[] parts = full.Split('_');
                            if (parts.Length != 5 || !parts[0].Equals(name, StringComparison.OrdinalIgnoreCase) || !parts[4].Equals(publisherId, StringComparison.OrdinalIgnoreCase)) continue;
                            int? c = Data.CompareVersions(parts[1], best ?? "0");
                            if (best == null || (c.HasValue && c.Value > 0)) best = parts[1];
                        }
            }
            catch (Exception) { }
            return best;
        }

        internal static void MarkInstalled(List<SearchResult> results)
        { MarkInstalled(results, InstalledSnapshot.Capture()); }

        internal static void MarkInstalled(List<SearchResult> results, InstalledSnapshot snapshot)
        {
            foreach (SearchResult r in results)
            {
                string family = null;
                if (r.Source == "store") lock (storeFamilies) storeFamilies.TryGetValue(r.Id, out family);
                r.InstalledMatch = snapshot.Resolve(r.Source, r.Id, r.Name);
                r.InstalledVersion = r.Source == "store" ? StoreInstalledVersion(family) : r.InstalledMatch.Confirmed ? r.InstalledMatch.Record.Version : null;
            }
        }

        /// Installed apps are matched by display name, so same-named results all look installed. When one of them
        /// releases exactly the installed version, only that one keeps the badge (Afterclip 1.5.0 is
        /// minimalhandle/Afterclip v1.5.0, not panzergen/AfterClip v0.3.0).
        internal static void AttributeInstalled(List<SearchResult> results)
        {
            foreach (SearchResult r in results) if (r.Source == "github" && (r.InstalledMatch == null || !r.InstalledMatch.Confirmed)) r.InstalledVersion = null;
        }

        /// Store apps from the list that are installed and have a newer Store release.
        public static List<UpdateItem> StoreUpdates(List<AppItem> storeApps)
        {
            var list = new List<UpdateItem>();
            var ids = new List<string>(); foreach (AppItem a in storeApps) ids.Add(a.Id);
            if (ids.Count == 0) return list;
            foreach (SearchResult s in StoreDetails(ids))
            {
                string family; lock (storeFamilies) storeFamilies.TryGetValue(s.Id, out family);
                string installed = StoreInstalledVersion(family);
                int? newer = Data.CompareVersions(s.Version, installed);
                if (installed == null || !newer.HasValue || newer.Value <= 0) continue;
                AppItem app = storeApps.Find(a => a.Id == s.Id);
                list.Add(new UpdateItem { Source = "store", Id = s.Id, Name = app != null ? app.Name : s.Name, Current = installed, Available = s.Version });
            }
            return list;
        }

        /// GitHub-release apps from the list that are installed and have a newer release.
        public static List<UpdateItem> GitHubUpdates(List<AppItem> gitHubApps)
        {
            var list = new List<UpdateItem>();
            var errors = new List<string>();
            var snapshot = InstalledSnapshot.Capture();
            foreach (AppItem a in gitHubApps)
            {
                var match = snapshot.Resolve("github", a.Id, a.Name);
                if (!match.Confirmed)
                {
                    if (match.Record != null || match.Reason.StartsWith("Multiple", StringComparison.Ordinal)) errors.Add(a.Name + ": confirm the repository match in Add apps before updating");
                    continue;
                }
                string installed = match.Record.Version;
                if (string.IsNullOrEmpty(installed)) continue;
                GitHubRelease release;
                try { release = GitHubLatest(a.Id, true); }
                catch (Exception ex) { errors.Add(a.Name + ": " + ex.Message); continue; }
                if (release == null || PickInstallerAsset(release.Assets) == null) { errors.Add(a.Name + ": no usable release found"); continue; }
                int? newer = Data.CompareVersions(release.Tag, installed);
                if (newer.HasValue && newer.Value > 0)
                    list.Add(new UpdateItem { Source = "github", Id = a.Id, Name = a.Name, Current = installed, Available = release.Tag.TrimStart('v', 'V') });
            }
            if (errors.Count > 0) throw new PartialUpdatesException(list, string.Join("; ", errors.ToArray()));
            return list;
        }

        // ---------------------------------------------------------------- winget
        static string Winget(string args, int timeoutMs)
        {
            string exe = Engine.WingetExe();
            if (exe == null) throw new InvalidOperationException("winget (App Installer) isn't available on this PC.");
            string output;
            int code = Proc.Capture(exe, args, out output, timeoutMs, SearchIO.Token);
            if (code == -1) throw new TimeoutException("winget timed out.");
            if (code != 0 && code != -1978335212 && code != -1978335189) throw new InvalidOperationException("winget failed: " + output.Trim());
            return output;
        }

        internal static string WingetIconDetails(string id)
        {
            if (!Regex.IsMatch(id ?? "", @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z")) return "";
            return Winget("show --id \"" + id + "\" --exact --locale en-US" + WingetCommon, 15000);
        }

        /// Parses winget's fixed-width tables (header line followed by a line of dashes).
        public static List<Dictionary<string, string>> ParseTable(string output)
        {
            var rows = new List<Dictionary<string, string>>();
            var lines = new List<string>();
            foreach (string raw in output.Split('\n'))
            {
                string l = raw.TrimEnd('\r');
                int cr = l.LastIndexOf('\r'); if (cr >= 0) l = l.Substring(cr + 1);
                lines.Add(l);
            }
            for (int i = 1; i < lines.Count; i++)
            {
                if (!Regex.IsMatch(lines[i], "^-{10,}\\s*$")) continue;
                string header = lines[i - 1];
                // Spinner characters can precede the header on the same line.
                int nameAt = header.IndexOf("Name", StringComparison.Ordinal);
                if (nameAt < 0) continue;
                header = header.Substring(nameAt);
                int shift = nameAt;
                var cols = new List<KeyValuePair<string, int>>();
                foreach (Match m in Regex.Matches(header, @"\S+")) cols.Add(new KeyValuePair<string, int>(m.Value, m.Index));
                for (int j = i + 1; j < lines.Count; j++)
                {
                    string line = lines[j];
                    if (line.Trim().Length == 0) break;
                    if (line.Length > 0 && shift > 0 && line.Length > shift) { }
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int c = 0; c < cols.Count; c++)
                    {
                        int start = cols[c].Value, end = c + 1 < cols.Count ? cols[c + 1].Value : int.MaxValue;
                        string cell = start < line.Length ? line.Substring(start, Math.Min(line.Length, end) - start) : "";
                        row[cols[c].Key] = cell.Trim();
                    }
                    string id;
                    if (row.TryGetValue("Id", out id) && id.Length > 0 && !id.Contains(" ")) rows.Add(row);
                }
                break;
            }
            return rows;
        }

        static string Cell(Dictionary<string, string> row, string key) { string v; return row.TryGetValue(key, out v) ? v : ""; }

        public static List<SearchResult> SearchWinget(string query)
        {
            string output = Winget("search --query \"" + query.Replace("\"", "") + "\"" + WingetCommon, 90000);
            var list = new List<SearchResult>();
            foreach (var row in ParseTable(output))
            {
                string id = Cell(row, "Id");
                if (id.EndsWith("…") || id.EndsWith("...")) continue;   // truncated
                list.Add(new SearchResult
                {
                    Source = "winget", Id = id, Name = Cell(row, "Name"), Version = Cell(row, "Version"),
                    Publisher = id.Contains(".") ? id.Substring(0, id.IndexOf('.')) : "",
                    // Microsoft's own winget-pkgs manifest folder, e.g. manifests/s/Spotify/Spotify.
                    Homepage = Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z") ?"https://github.com/microsoft/winget-pkgs/tree/master/manifests/" + char.ToLowerInvariant(id[0]) + "/" + id.Replace('.', '/') : "",
                    Meta = id + (Cell(row, "Version").Length > 0 ? "  ·  v" + Cell(row, "Version") : "") + "  ·  winget", Summary = ""
                });
                if (list.Count >= 40) break;
            }
            return list;
        }

        public static List<InstalledApp> WingetInstalled()
        {
            var list = new List<InstalledApp>();
            if (Engine.WingetExe() == null) return list;
            foreach (var row in ParseTable(Winget("list" + WingetCommon, 120000)))
                list.Add(new InstalledApp { Id = Cell(row, "Id"), Name = Cell(row, "Name"), Version = Cell(row, "Version") });
            return list;
        }

        public static List<UpdateItem> WingetUpdates()
        {
            var list = new List<UpdateItem>();
            if (Engine.WingetExe() == null) return list;
            foreach (var row in ParseTable(Winget("upgrade" + WingetCommon, 120000)))
            {
                string available = Cell(row, "Available");
                if (available.Length == 0) continue;
                list.Add(new UpdateItem { Source = "winget", Id = Cell(row, "Id"), Name = Cell(row, "Name"), Current = Cell(row, "Version"), Available = available });
            }
            return list;
        }

        // ---------------------------------------------------------------- Chocolatey local
        public static Dictionary<string, string> ChocoInstalled()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(Engine.ChocoExe)) return map;
            string output;
            Proc.Capture(Engine.ChocoExe, "list -r", out output, 60000);
            foreach (string line in output.Split('\n'))
            {
                string[] p = line.Trim().Split('|');
                if (p.Length >= 2 && Regex.IsMatch(p[0], @"^[A-Za-z0-9][A-Za-z0-9.-]*$")) map[p[0]] = p[1];
            }
            return map;
        }

        public static List<UpdateItem> ChocoUpdates()
        {
            if (!File.Exists(Engine.ChocoExe)) return new List<UpdateItem>();
            string output;
            int code = Proc.Capture(Engine.ChocoExe, "outdated -r --ignore-unfound --ignore-pinned", out output, 180000);
            return ParseChocolateyUpdates(code, output);
        }

        internal static List<UpdateItem> ParseChocolateyUpdates(int code, string output)
        {
            if (code == -1) throw new TimeoutException("Chocolatey timed out.");
            if (code != 0 && code != 2) throw new InvalidOperationException("Chocolatey failed: " + output.Trim());
            var list = new List<UpdateItem>();
            foreach (string line in output.Split('\n'))
            {
                string[] p = line.Trim().Split('|');
                if (p.Length >= 3 && Regex.IsMatch(p[0], @"^[A-Za-z0-9][A-Za-z0-9.-]*$") && p[2].Length > 0)
                    list.Add(new UpdateItem { Source = "choco", Id = p[0], Name = p[0], Current = p[1], Available = p[2] });
            }
            return list;
        }

        // ---------------------------------------------------------------- Windows' installed-programs list
        public static List<string> RegistryProgramNames()
        {
            var names = new List<string>();
            string[] paths = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
            foreach (RegistryKey hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                foreach (string path in paths)
                {
                    try
                    {
                        using (RegistryKey k = hive.OpenSubKey(path))
                        {
                            if (k == null) continue;
                            foreach (string sub in k.GetSubKeyNames())
                                using (RegistryKey s = k.OpenSubKey(sub))
                                {
                                    if (s == null) continue;
                                    string n = s.GetValue("DisplayName") as string;
                                    object sys = s.GetValue("SystemComponent");
                                    if (!string.IsNullOrEmpty(n) && !(sys is int && (int)sys == 1)) names.Add(n);
                                }
                        }
                    }
                    catch { }
                }
            return names;
        }

        /// True when a listed app looks installed by name (Chocolatey/winget aside).
        public static bool NameMatches(string appName, string appId, ICollection<string> normalizedPrograms)
        {
            string a = Data.Norm(appName);
            string idBase = Data.Norm(Regex.Replace(appId, @"\.(install|portable)$", "", RegexOptions.IgnoreCase));
            if (appId.Contains(".") && !Regex.IsMatch(appId, @"\.(install|portable)$", RegexOptions.IgnoreCase))
            {
                string[] parts = appId.Split('.');
                idBase = Data.Norm(parts[parts.Length - 1]);
            }
            foreach (string p in normalizedPrograms)
            {
                if (p.Length < 3) continue;
                if (p == a || p == idBase) return true;
                if (a.Length >= 5 && p.StartsWith(a)) return true;
                if (p.Length >= 5 && a.StartsWith(p)) return true;
                if (idBase.Length >= 5 && p.StartsWith(idBase)) return true;
            }
            return false;
        }
    }
}
