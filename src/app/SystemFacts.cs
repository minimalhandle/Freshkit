using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace Freshkit
{
    internal sealed class WindowsFacts
    {
        public string Edition, Build;
    }
    internal static class SystemFacts
    {
        public static WindowsFacts FormatWindows(string product, string release, string build, object revision)
        {
            product = string.IsNullOrWhiteSpace(product) ? "Windows" : product.Trim();
            int number;
            if (int.TryParse(build, out number) && number >= 22000) product = product.Replace("Windows 10", "Windows 11");
            string version = string.IsNullOrWhiteSpace(release) ? "" : " " + release.Trim();
            string fullBuild = string.IsNullOrWhiteSpace(build) ? "Build unavailable" : "Build " + build.Trim();
            if (!string.IsNullOrWhiteSpace(build) && revision != null) fullBuild += "." + Convert.ToString(revision, CultureInfo.InvariantCulture);
            return new WindowsFacts { Edition = product + version, Build = fullBuild };
        }
        public static WindowsFacts ReadWindows()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (key != null) return FormatWindows(key.GetValue("ProductName") as string, key.GetValue("DisplayVersion") as string, key.GetValue("CurrentBuild") as string, key.GetValue("UBR"));
            }
            catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
            return FormatWindows(null, null, null, null);
        }
        public static string SystemDrive()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
                var drive = new DriveInfo(root);
                return root.TrimEnd('\\') + " · " + (drive.AvailableFreeSpace / 1073741824d).ToString("0", CultureInfo.CurrentCulture) + " GB free of " + (drive.TotalSize / 1073741824d).ToString("0", CultureInfo.CurrentCulture) + " GB";
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
            return "System drive unavailable";
        }
    }
}
