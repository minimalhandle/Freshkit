using System;
namespace Freshkit
{
    internal static class DriverPreviewTests
    {
        static int checks;
        static void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; }
        [STAThread] static void Main()
        {
            var report = DriverPreviewReport.Parse("{\"SchemaVersion\":1,\"InventoryComplete\":true,\"Error\":\"\",\"ScannedUtc\":\"2026-11-16T10:00:00Z\",\"Rows\":[{\"Hardware\":\"Intel Arc A770\",\"DeviceId\":\"PCI test\",\"Kind\":\"Graphics\",\"PackageId\":\"intel-arc-graphics-driver\",\"PackageName\":\"Intel Graphics\",\"Compatibility\":\"Candidate\",\"Reason\":\"Reviewed PCI model\",\"InstalledDriverVersion\":\"32.0.101.9175\",\"AvailablePackageVersion\":\"32.0.101.9175\",\"MetadataStatus\":\"Available\",\"SupportUrl\":\"https://www.intel.com/content/www/us/en/support/articles/000090440/graphics.html\"}]}");
            Check(report.InventoryComplete && report.Rows.Count == 1, "Parse GPU preview rows");
            Check(report.Rows[0].InstalledDriverVersion == "32.0.101.9175" && report.Rows[0].AvailablePackageVersion == "32.0.101.9175", "Preserve separately labelled driver and package versions");
            Check(report.Summary.Contains("Installed driver") && report.Summary.Contains("Available package") && !report.Summary.Contains("up to date"), "Equal numbering must never claim the installed driver is current");
            Check(report.Rows[0].SupportUri != null, "Official HTTPS support links are available");
            report.Rows[0].SupportUrl = "file:///C:/Windows/system32/cmd.exe";
            Check(report.Rows[0].SupportUri == null, "Reject non-web support links");
            report.Rows[0].SupportUrl = "https://unknown.example/";
            Check(report.Rows[0].SupportUri == null, "Reject unknown support hosts");
            var unknown = DriverPreviewReport.Parse("{\"SchemaVersion\":1,\"InventoryComplete\":false,\"Error\":\"CIM failed\",\"Rows\":[]}");
            Check(unknown.Summary.Contains("CIM failed") && !unknown.Summary.Contains("up to date"), "Incomplete inventory cannot imply current drivers");
            bool rejected = false; try { DriverPreviewReport.Parse("{\"SchemaVersion\":2}"); } catch { rejected = true; }
            Check(rejected, "Reject unsupported preview schema");
            rejected = false; try { DriverPreviewReport.Parse("{\"SchemaVersion\":1}"); } catch { rejected = true; }
            Check(rejected, "Reject incomplete schema instead of displaying a false empty scan");
            var failed = DriverPreviewReport.Failure("Timed out");
            Check(!failed.InventoryComplete && failed.Summary.Contains("Timed out"), "Timeout remains explicit");
            var chipset = new DriverPreviewReport { InventoryComplete = true };
            for (int i = 0; i < 30; i++) chipset.Rows.Add(new DriverPreviewRow { Hardware = "AMD PCI", DeviceId = "PCI fixture " + i, Kind = "Chipset", PackageId = i == 1 ? "AMD-CHIPSET" : "amd-chipset", PackageName = "AMD Chipset", InstalledDriverVersion = "1.0." + i, AvailablePackageVersion = "7.1", Compatibility = "Candidate", Reason = "Review OEM compatibility", MetadataStatus = "Available" });
            Check(chipset.Groups.Count == 1 && chipset.Groups[0].Devices.Count == 30, "One installer covers all chipset device instances, regardless of package-ID case");
            Check(chipset.Groups[0].Devices[29].InstalledDriverVersion == "1.0.29", "Grouping preserves each device driver version");
            Check(chipset.Summary.Length < 500 && chipset.Summary.Contains("30 devices"), "Setup review summarizes the package instead of repeating every device");
            chipset.Rows.Add(report.Rows[0]);
            chipset.Rows.Add(new DriverPreviewRow { Hardware = "Unreviewed GPU", PackageId = "", PackageName = "No reviewed automatic package" });
            chipset.Rows.Add(new DriverPreviewRow { Hardware = "Other GPU", PackageId = "", PackageName = "No reviewed automatic package" });
            Check(chipset.Groups.Count == 4, "Different installers and unmatched devices remain separate");
            Console.WriteLine("PASS " + checks + " driver preview model checks");
        }
    }
}
