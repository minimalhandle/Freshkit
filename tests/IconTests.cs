using System;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace Freshkit
{
    internal static class IconTests
    {
        public static void Run(Action<bool, string> check)
        {
            byte[] svg = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='32' height='32' fill='#ff0000'/></svg>");
            var image = AppIcons.Decode(svg);
            check(image != null && image.IsFrozen && image.PixelWidth == 96, "Downloaded SVG icons render as frozen thumbnails");
            byte[] pixel = new byte[4]; image.CopyPixels(new System.Windows.Int32Rect(48, 48, 1, 1), pixel, 4, 0);
            check(pixel[2] == 255 && pixel[3] == 255, "SVG thumbnail contains the rendered logo");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            byte[] png; using (var output = new MemoryStream()) { encoder.Save(output); png = output.ToArray(); }
            string metadata = "<feed xmlns:d='urn:test'><entry><d:IconUrl>https://example.test/icon.svg</d:IconUrl><d:ProjectUrl>https://example.test/app</d:ProjectUrl></entry></feed>";
            var remote = AppIcons.ResolveRemote("Custom.App", false, delegate(string url) {
                if (url.Contains("api/v2")) {
                    if (!Uri.UnescapeDataString(url).Contains("tolower(Id) eq 'custom.app'")) throw new IOException("Case-sensitive package lookup");
                    return Encoding.UTF8.GetBytes(metadata);
                }
                return svg;
            }, null);
            check(remote != null, "New Chocolatey packages use their own metadata icon");
            string requested = "";
            remote = AppIcons.ResolveRemote("Example.App", true, delegate(string url) { requested = url; return png; }, delegate(string id) { return "Homepage: https://example.test/download\n"; });
            check(remote != null && requested == "https://example.test/favicon.ico", "Winget search apps resolve a website icon from package details");
            remote = AppIcons.ResolveRemote("custom.app", false, delegate(string url) {
                if (url.Contains("api/v2")) return Encoding.UTF8.GetBytes(metadata);
                if (url.EndsWith(".svg")) throw new IOException("unavailable");
                requested = url; return png;
            }, null);
            check(remote != null && requested == "https://example.test/favicon.ico", "Broken package icon falls back to its project website");
            remote = AppIcons.ResolveRemote("Example.App", true, delegate(string url) {
                requested = url;
                return url.EndsWith("/app/") ? Encoding.UTF8.GetBytes("<link href='logo.svg' rel='shortcut icon'>") : svg;
            }, delegate(string id) { return "Homepage: https://example.test/app/\n"; });
            check(remote != null && requested == "https://example.test/app/logo.svg", "Website icons resolve relative SVG links on nested homepages");
            string folder = Path.Combine(Path.GetTempPath(), "Freshkit-icons-" + Guid.NewGuid().ToString("N"));
            try
            {
                string choco = AppIcons.CachePath("Example.App", false, folder), winget = AppIcons.CachePath("Example.App", true, folder);
                AppIcons.Save(image, choco);
                check(AppIcons.ReadFile(choco) != null && choco != winget, "Icons survive a disk-cache roundtrip and keep package sources separate");
                check(Path.GetFileName(choco).StartsWith("v2-", StringComparison.Ordinal), "The identity-safe icon cache invalidates legacy misattributed images");
                check(AppIcons.CachePath("../../escape", true, folder).StartsWith(folder + Path.DirectorySeparatorChar), "Package IDs cannot escape the icon cache");
                File.WriteAllText(choco, "invalid image");
                check(AppIcons.ReadFile(choco) == null, "Corrupt cached icons fall back without breaking the app list");
                string ico = Path.Combine(folder, "app, icon.ico");
                using (var stream = File.Create(ico)) System.Drawing.SystemIcons.Information.Save(stream);
                check(AppIcons.ExtractInstalledIcon("\"" + ico + "\",0") != null, "Scanned program icons handle quoted paths, commas and resource indexes");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            var search = new SearchResult { Id = "synthetic invalid id", Name = "Example", Source = "winget" };
            var added = new AppItem { Id = search.Id, Name = search.Name, IsWinget = true };
            var scanned = new PickItem { Tag = new InstalledApp { Id = search.Id, Name = search.Name } };
            check(object.ReferenceEquals(search.Artwork, added.Artwork) && object.ReferenceEquals(added.Artwork, scanned.Artwork), "Search, scan and added cards share the same live icon");
            bool notified = false;
            added.Artwork.PropertyChanged += delegate(object sender, System.ComponentModel.PropertyChangedEventArgs args) { notified |= args.PropertyName == "Image"; };
            search.Artwork.Image = image;
            check(notified && scanned.Artwork.Image == image, "Background icon completion refreshes all views of the package");
        }
    }
}
