// Read-only driver scan data. Driver and package versions are deliberately never
// compared: Windows INF and distribution package numbering are different.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Freshkit
{
    internal sealed class DriverPreviewRow
    {
        public string Hardware { get; set; }
        public string DeviceId { get; set; }
        public string Kind { get; set; }
        public string PackageId { get; set; }
        public string PackageName { get; set; }
        public string Compatibility { get; set; }
        public string Reason { get; set; }
        public string InstalledDriverVersion { get; set; }
        public string AvailablePackageVersion { get; set; }
        public string MetadataStatus { get; set; }
        public string MetadataDetail { get; set; }
        public string SupportUrl { get; set; }
        public Uri SupportUri
        {
            get
            {
                Uri uri;
                if (!Uri.TryCreate(SupportUrl, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return null;
                foreach (string host in new[] { "www.intel.com", "www.amd.com", "www.nvidia.com", "www.dell.com", "pcsupport.lenovo.com", "support.hp.com", "www.asus.com", "www.acer.com", "www.msi.com", "support.microsoft.com" })
                    if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)) return uri;
                return null;
            }
        }
    }

    internal sealed class DriverPreviewGroup
    {
        public readonly List<DriverPreviewRow> Devices = new List<DriverPreviewRow>();
        public DriverPreviewRow Package { get { return Devices[0]; } }
        public string Title { get { return string.IsNullOrEmpty(Package.PackageId) ? Package.Hardware : Package.PackageName; } }
        public string Guidance
        {
            get
            {
                var lines = new List<string>();
                foreach (var row in Devices)
                {
                    string line = row.Compatibility + ": " + row.Reason;
                    if (!lines.Contains(line)) lines.Add(line);
                }
                return string.Join("\n", lines.ToArray());
            }
        }
    }

    internal sealed class DriverPreviewReport
    {
        public bool InventoryComplete;
        public string Error = "", ScannedUtc = "";
        public readonly List<DriverPreviewRow> Rows = new List<DriverPreviewRow>();
        public List<DriverPreviewGroup> Groups
        {
            get
            {
                var result = new List<DriverPreviewGroup>();
                var packages = new Dictionary<string, DriverPreviewGroup>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in Rows)
                {
                    DriverPreviewGroup group;
                    if (string.IsNullOrEmpty(row.PackageId) || !packages.TryGetValue(row.PackageId, out group))
                    {
                        group = new DriverPreviewGroup(); result.Add(group);
                        if (!string.IsNullOrEmpty(row.PackageId)) packages.Add(row.PackageId, group);
                    }
                    group.Devices.Add(row);
                }
                return result;
            }
        }
        public string Summary
        {
            get
            {
                var text = new StringBuilder();
                if (!string.IsNullOrEmpty(Error)) text.AppendLine(Error);
                if (!InventoryComplete) text.AppendLine("Hardware detection is incomplete. Driver compatibility and installed versions may be unknown.");
                if (Rows.Count == 0 && InventoryComplete) text.AppendLine("No graphics or AMD chipset devices were returned by this scan.");
                foreach (var group in Groups)
                {
                    var row = group.Package;
                    if (text.Length > 0) text.AppendLine();
                    text.AppendLine(group.Title + " · " + group.Devices.Count + (group.Devices.Count == 1 ? " device" : " devices"));
                    text.Append("Available package: " + row.AvailablePackageVersion + ". Installed driver versions are in device details.");
                    if (string.IsNullOrEmpty(row.PackageId)) text.Append(" No reviewed automatic installer.");
                    if (row.MetadataStatus != "Available") text.Append(" Package metadata: Unknown.");
                }
                return text.ToString().TrimEnd();
            }
        }
        public static DriverPreviewReport Failure(string error) { return new DriverPreviewReport { Error = error, InventoryComplete = false }; }
        public static DriverPreviewReport Parse(string json)
        {
            object value = Engine.Json.DeserializeObject(json.Trim().TrimStart('\uFEFF'));
            if (J.I(value, "SchemaVersion", 0) != 1 || !(J.Get(value, "InventoryComplete") is bool) || J.Get(value, "Rows") == null)
                throw new InvalidDataException("The driver scan returned incomplete or unsupported data.");
            var report = new DriverPreviewReport { InventoryComplete = J.B(value, "InventoryComplete"), Error = J.S(value, "Error"), ScannedUtc = J.S(value, "ScannedUtc") };
            foreach (object row in J.A(value, "Rows"))
            {
                string installed = J.S(row, "InstalledDriverVersion"), available = J.S(row, "AvailablePackageVersion");
                report.Rows.Add(new DriverPreviewRow {
                    Hardware = J.S(row, "Hardware"), DeviceId = J.S(row, "DeviceId"), Kind = J.S(row, "Kind"),
                    PackageId = J.S(row, "PackageId"), PackageName = J.S(row, "PackageName"), Compatibility = J.S(row, "Compatibility"), Reason = J.S(row, "Reason"),
                    InstalledDriverVersion = installed.Length == 0 ? "Unknown" : installed,
                    AvailablePackageVersion = available.Length == 0 ? "Unknown" : available,
                    MetadataStatus = J.S(row, "MetadataStatus"), MetadataDetail = J.S(row, "MetadataDetail"), SupportUrl = J.S(row, "SupportUrl")
                });
            }
            return report;
        }
        public static DriverPreviewReport Scan()
        {
            string work = null;
            try
            {
                work = Engine.ExtractWork();
                string module = Path.Combine(work, "Drivers.Core.psm1");
                File.WriteAllText(module, Engine.ReadResourceText("payload/Drivers.Core.psm1"), new UTF8Encoding(true));
                string output;
                int code = Proc.Capture(Engine.PowerShellPath, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Engine.Q(Path.Combine(work, "DriverPreview.ps1")) + " -DriversModule " + Engine.Q(module), out output, 45000);
                if (code == -1) return Failure("Driver scan timed out. Hardware, installed driver versions and package availability are unknown. Retry the scan.");
                if (code != 0) return Failure("Driver scan could not finish. Hardware, installed driver versions and package availability are unknown. Retry the scan.");
                return Parse(output);
            }
            catch (Exception ex) { return Failure("Driver preview is unavailable. " + ex.Message); }
            finally { Engine.Cleanup(work); }
        }
    }
}
