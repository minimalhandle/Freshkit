using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;

namespace Freshkit
{
    public sealed class AppIcon : Notifier
    {
        BitmapSource image;
        internal readonly Dispatcher Dispatcher = Dispatcher.CurrentDispatcher;
        public string Monogram { get; internal set; }
        public BitmapSource Image { get { return image; } internal set { image = value; Raise("Image"); } }
    }

    // Two background workers share source-qualified, persistent thumbnails.
    internal static class AppIcons
    {
        sealed class Work { public string Id, Name, Source; public AppIcon Icon; }
        static readonly Dictionary<string, AppIcon> cache = new Dictionary<string, AppIcon>(StringComparer.OrdinalIgnoreCase);
        static readonly Queue<Work> queue = new Queue<Work>();
        static readonly Dictionary<string, Assembly> libraries = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        static int workers;
        const int MaxBytes = 2 * 1024 * 1024;

        static AppIcons()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                string name = new AssemblyName(args.Name).Name;
                lock (libraries)
                {
                    Assembly library;
                    if (libraries.TryGetValue(name, out library)) return library;
                    using (Stream stream = Engine.Resource("icon-libs/" + name + ".dll"))
                    {
                        if (stream == null) return null;
                        using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); library = Assembly.Load(buffer.ToArray()); }
                    }
                    libraries[name] = library; return library;
                }
            };
        }

        public static AppIcon Get(string id, string name, bool winget) { return Get(id, name, winget ? "winget" : "choco"); }

        /// source: choco, winget or github (owner/repo ids).
        public static AppIcon Get(string id, string name, string source)
        {
            string key = source + ":" + id;
            lock (queue)
            {
                AppIcon icon;
                if (cache.TryGetValue(key, out icon)) return icon;
                string initial = "?";
                foreach (char c in name ?? "") if (char.IsLetterOrDigit(c)) { initial = char.ToUpperInvariant(c).ToString(); break; }
                icon = new AppIcon { Monogram = initial, Image = For(id, source) }; cache[key] = icon;
                if (icon.Image == null && !string.IsNullOrEmpty(id))
                {
                    queue.Enqueue(new Work { Id = id, Name = name, Source = source, Icon = icon });
                    if (workers < 2) { workers++; ThreadPool.QueueUserWorkItem(delegate { RunQueue(); }); }
                }
                return icon;
            }
        }

        static void RunQueue()
        {
            while (true)
            {
                Work work;
                lock (queue) { if (queue.Count == 0) { workers--; return; } work = queue.Dequeue(); }
                try
                {
                    string path = CachePath(work.Id, work.Source, Path.Combine(Engine.DataDir, "icons"));
                    BitmapSource image = ReadFile(path) ?? InstalledIcon(work.Name);
                    // Scan rows can have ARP/GUID identifiers. They can still use a
                    // local DisplayIcon, even though they are not package lookup IDs.
                    if (image == null && Data.IsStoreId(work.Id))
                    {
                        // Store apps: the listing's official logo.
                        string logo = Sources.StoreLogoUrl(work.Id);
                        if (logo != null) image = TryDownload(logo, Download);
                    }
                    else if (image == null && work.Source == "github" && Data.IsGitHubRepo(work.Id))
                        image = TryDownload("https://github.com/" + work.Id.Split('/')[0] + ".png?size=96", Download);
                    else if (image == null && Regex.IsMatch(work.Id, @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z"))
                        image = ResolveRemote(work.Id, work.Source == "winget", Download, Sources.WingetIconDetails);
                    if (image == null) continue;
                    try { Save(image, path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    if (!work.Icon.Dispatcher.HasShutdownStarted) work.Icon.Dispatcher.BeginInvoke(new Action(delegate { work.Icon.Image = image; }));
                }
                catch (Exception) { /* A missing icon must not break searching or adding apps. */ }
            }
        }

        public static BitmapSource For(string id, bool winget) { return For(id, winget ? "winget" : "choco"); }

        public static BitmapSource For(string id, string source)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                // Built-in logos: icons/<choco-id>.png, icons/winget-<id>.png, or icons/github-<owner>-<repo>.png.
                string file = source == "github" ? Data.GitHubEngineKey(id) : (source == "winget" ? "winget-" : "") + id.ToLowerInvariant();
                using (Stream stream = Engine.Resource("icons/" + file + ".png"))
                {
                    if (stream == null) return null;
                    var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); bitmap.Freeze(); return bitmap;
                }
            }
            catch (Exception) { return null; }
        }

        public static string CachePath(string id, bool winget, string folder) { return CachePath(id, winget ? "winget" : "choco", folder); }

        public static string CachePath(string id, string source, string folder)
        {
            using (var hash = SHA256.Create())
                return Path.Combine(folder, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(source + ":" + id.ToLowerInvariant()))).Replace("-", "") + ".png");
        }

        public static BitmapSource ReadFile(string path)
        {
            try { return File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null; } catch (Exception) { return null; }
        }

        public static void Save(BitmapSource image, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }

        public static BitmapSource Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes) return null;
            try
            {
                using (var stream = new MemoryStream(bytes))
                {
                    string prefix = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512));
                    if (prefix.Contains("<svg") || prefix.Contains("<?xml") || prefix.Contains("<!DOCTYPE")) return DecodeSvg(stream);
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 96; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
                }
            }
            catch (Exception) { return null; }
        }

        static BitmapSource DecodeSvg(Stream stream)
        {
            Svg.SvgDocument.ResolveExternalXmlEntites = Svg.ExternalType.None;
            Svg.SvgDocument.ResolveExternalImages = Svg.ExternalType.None;
            Svg.SvgDocument.ResolveExternalElements = Svg.ExternalType.None;
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = MaxBytes })) document.Load(reader);
            if (document.DocumentElement == null || document.DocumentElement.LocalName != "svg") return null;
            foreach (XmlNode node in document.SelectNodes("//processing-instruction() | //*[local-name()='script' or local-name()='foreignObject']")) node.ParentNode.RemoveChild(node);
            var svg = Svg.SvgDocument.Open(document);
            using (var bitmap = svg.Draw(96, 96))
            using (var output = new MemoryStream())
            {
                if (bitmap == null) return null;
                bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png); output.Position = 0;
                var result = BitmapFrame.Create(output, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); result.Freeze(); return result;
            }
        }

        public static BitmapSource ResolveRemote(string id, bool winget, Func<string, byte[]> download, Func<string, string> wingetDetails)
        {
            string icon = "", homepage = "", publisher = "";
            try
            {
                if (winget)
                {
                    foreach (string line in wingetDetails(id).Split('\n'))
                    {
                        Match match = Regex.Match(line.Trim(), @"^(Homepage|Publisher Url|Icon Url):\s*(https://\S+)", RegexOptions.IgnoreCase);
                        if (!match.Success) continue;
                        if (match.Groups[1].Value.Equals("Icon Url", StringComparison.OrdinalIgnoreCase)) icon = match.Groups[2].Value;
                        else if (match.Groups[1].Value.Equals("Homepage", StringComparison.OrdinalIgnoreCase)) homepage = match.Groups[2].Value;
                        else publisher = match.Groups[2].Value;
                    }
                }
                else
                {
                    string url = "https://community.chocolatey.org/api/v2/Packages()?$filter=" + Uri.EscapeDataString("tolower(Id) eq '" + id.ToLowerInvariant().Replace("'", "''") + "' and IsLatestVersion");
                    var doc = new XmlDocument { XmlResolver = null };
                    using (var stream = new MemoryStream(download(url)))
                    using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
                    XmlNode node = doc.SelectSingleNode("//*[local-name()='IconUrl']"); if (node != null) icon = node.InnerText.Trim();
                    node = doc.SelectSingleNode("//*[local-name()='ProjectUrl']"); if (node != null) homepage = node.InnerText.Trim();
                }
            }
            catch (Exception) { return null; }
            BitmapSource result = TryDownload(icon, download); if (result != null) return result;
            return WebsiteIcon(homepage, download) ?? WebsiteIcon(publisher, download);
        }

        static BitmapSource WebsiteIcon(string homepage, Func<string, byte[]> download)
        {
            Uri site;
            if (!Uri.TryCreate(homepage, UriKind.Absolute, out site) || site.Scheme != "https") return null;
            try
            {
                string html = Encoding.UTF8.GetString(download(site.AbsoluteUri));
                // Prefer large icons: apple-touch-icon, then the biggest declared size, then the rest in page order.
                var candidates = new List<KeyValuePair<int, string>>();
                foreach (Match link in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
                {
                    Match rel = Regex.Match(link.Value, "\\brel\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);
                    if (!Regex.IsMatch(rel.Groups[1].Value, @"\bicon\b", RegexOptions.IgnoreCase)) continue;
                    Match href = Regex.Match(link.Value, "\\bhref\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);
                    Uri url;
                    if (!href.Success || !Uri.TryCreate(site, WebUtility.HtmlDecode(href.Groups[1].Value), out url)) continue;
                    int size = 0; Match sizes = Regex.Match(link.Value, "\\bsizes\\s*=\\s*['\"](\\d+)x\\d+", RegexOptions.IgnoreCase);
                    if (sizes.Success) int.TryParse(sizes.Groups[1].Value, out size);
                    if (rel.Groups[1].Value.IndexOf("apple-touch", StringComparison.OrdinalIgnoreCase) >= 0) size = Math.Max(size, 180);
                    candidates.Add(new KeyValuePair<int, string>(size * 1000 - candidates.Count, url.AbsoluteUri));
                }
                candidates.Sort(delegate (KeyValuePair<int, string> a, KeyValuePair<int, string> b) { return b.Key.CompareTo(a.Key); });
                foreach (var candidate in candidates) { BitmapSource image = TryDownload(candidate.Value, download); if (image != null) return image; }
            }
            catch (Exception) { }
            return TryDownload(site.GetLeftPart(UriPartial.Authority) + "/favicon.ico", download);
        }

        static BitmapSource TryDownload(string url, Func<string, byte[]> download)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https") return null;
            try { return Decode(download(url)); } catch (Exception) { return null; }
        }

        static byte[] Download(string url)
        {
            Data.EnsureModernTls();
            for (int redirects = 0; redirects < 5; redirects++)
            {
                var uri = new Uri(url); if (uri.Scheme != "https") throw new IOException("HTTPS required");
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.UserAgent = "Freshkit/3.0"; request.Timeout = 8000; request.ReadWriteTimeout = 8000; request.AllowAutoRedirect = false;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    {
                        var next = new Uri(uri, response.Headers["Location"]);
                        // Some CDNs redirect https to http on the same host; ask for the https form instead.
                        if (next.Scheme == "http" && string.Equals(next.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                            next = new UriBuilder(next) { Scheme = "https", Port = -1 }.Uri;
                        url = next.AbsoluteUri; continue;
                    }
                    if (response.ContentLength > MaxBytes) throw new IOException("Icon too large");
                    using (var stream = response.GetResponseStream())
                    using (var output = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192]; int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { if (output.Length + read > MaxBytes) throw new IOException("Icon too large"); output.Write(buffer, 0, read); }
                        return output.ToArray();
                    }
                }
            }
            throw new IOException("Too many icon redirects");
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);

        internal static BitmapSource InstalledIcon(string name)
        {
            string wanted = Data.Norm(Regex.Replace(name ?? "", @"\s*\([^)]*\)", "").TrimEnd('…', '.'));
            if (wanted.Length < 3) return null;
            string candidate = null; int matches = 0;
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (string root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                {
                    try
                    {
                        using (RegistryKey keys = hive.OpenSubKey(root))
                        {
                            if (keys == null) continue;
                            foreach (string sub in keys.GetSubKeyNames())
                                using (RegistryKey key = keys.OpenSubKey(sub))
                                {
                                    if (key == null) continue;
                                    string display = Data.Norm(Regex.Replace(key.GetValue("DisplayName") as string ?? "", @"\s*\([^)]*\)", ""));
                                    string value = key.GetValue("DisplayIcon") as string; if (string.IsNullOrEmpty(value)) continue;
                                    if (display == wanted) { var exact = ExtractInstalledIcon(value); if (exact != null) return exact; }
                                    else if (wanted.Length >= 8 && display.StartsWith(wanted, StringComparison.Ordinal)) { candidate = value; matches++; }
                                }
                        }
                    }
                    catch (Exception) { }
                }
            return matches == 1 ? ExtractInstalledIcon(candidate) : null;
        }

        internal static BitmapSource ExtractInstalledIcon(string value)
        {
            try
            {
                Match match = Regex.Match(value.Trim(), @"^(.*?)(?:,\s*(-?\d+))?$");
                string path = Environment.ExpandEnvironmentVariables(match.Groups[1].Value.Trim().Trim('"'));
                if (!Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(path)) return null;
                int index = 0; if (match.Groups[2].Success) int.TryParse(match.Groups[2].Value, out index);
                var large = new IntPtr[1]; var small = new IntPtr[1]; ExtractIconEx(path, index, large, small, 1);
                try
                {
                    IntPtr handle = large[0] != IntPtr.Zero ? large[0] : small[0]; if (handle == IntPtr.Zero) return ReadFile(path);
                    var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(96, 96)); image.Freeze(); return image;
                }
                finally { if (large[0] != IntPtr.Zero) DestroyIcon(large[0]); if (small[0] != IntPtr.Zero) DestroyIcon(small[0]); }
            }
            catch (Exception) { return null; }
        }
    }
}
