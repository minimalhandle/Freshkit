using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Freshkit
{
    internal sealed partial class MainUi
    {
        DriverPreviewReport driverPreview;
        bool driverPreviewBusy;
        readonly List<Action> driverPreviewCallbacks = new List<Action>();
        bool DriverPreviewBusy { get { return driverPreviewBusy; } }
        bool DriverPreviewReady { get { return !driverPreviewBusy && driverPreview != null; } }
        string DriverPreviewSummary { get { return driverPreviewBusy ? "Scanning hardware, installed Windows drivers and available packages…" : driverPreview == null ? "Scan hardware to preview matching driver packages and installed driver versions." : driverPreview.Summary; } }

        void InitDriverPreview() { }

        void RefreshDriverPreview(Action done = null)
        {
            if (done != null) driverPreviewCallbacks.Add(done);
            if (driverPreviewBusy) return;
            driverPreviewBusy = true;
            driverPreview = null;
            RefreshDriverReview();
            new Thread(delegate()
            {
                DriverPreviewReport report = DriverPreviewReport.Scan();
                if (Window.Dispatcher.HasShutdownStarted) return;
                Ui(delegate
                {
                    driverPreview = report; driverPreviewBusy = false;
                    RefreshDriverReview();
                    var callbacks = driverPreviewCallbacks.ToArray(); driverPreviewCallbacks.Clear();
                    foreach (Action callback in callbacks) callback();
                });
            }) { IsBackground = true }.Start();
        }

        void RefreshDriverReview()
        {
            if (Get<Grid>("ReviewSheet").Visibility != Visibility.Visible) return;
            Get<TextBlock>("ReviewDriversDetail").Text = DriverPreviewSummary;
            UpdateReviewStart();
        }

        void OpenDriverPreview(Action onContinue = null)
        {
            if (running) return;
            BuildDriverPreviewWindow(onContinue).ShowDialog();
        }

        // Construction alone is read-only and does not scan the test machine.
        // A visible modal starts its scan on Loaded; closed windows ignore updates.
        Window BuildDriverPreviewWindow(Action onContinue = null)
        {
            var preview = new Window { Title = "Driver preview", Width = 920, Height = 700, MinWidth = 620, MinHeight = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Frozen("#13161C"), Foreground = Frozen("#E8EAED"), FontSize = 13, FontFamily = Window.FontFamily };
            if (Window.IsVisible || new System.Windows.Interop.WindowInteropHelper(Window).Handle != IntPtr.Zero) preview.Owner = Window;
            var dock = new DockPanel { Margin = new Thickness(24) }; preview.Content = dock;
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
            var refresh = new Button { Name = "DriverPreviewRefresh", Content = "Refresh", Style = StyleOf("Secondary"), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) }; footer.Children.Add(refresh);
            var close = new Button { Content = onContinue == null ? "Close" : "Cancel", Style = StyleOf("Secondary"), IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) }; close.Click += delegate { preview.Close(); }; footer.Children.Add(close);
            bool closed = false;
            Button proceed = null;
            if (onContinue != null)
            {
                proceed = new Button { Name = "DriverPreviewContinue", Content = "Continue to install drivers", Style = StyleOf("Primary"), IsEnabled = false, Padding = new Thickness(16, 8, 16, 8) };
                proceed.Click += delegate { if (closed || !DriverPreviewReady || running) return; closed = true; proceed.IsEnabled = false; preview.Close(); onContinue(); }; footer.Children.Add(proceed);
            }
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
            header.Children.Add(new TextBlock { Text = "Driver preview", FontSize = 25, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = "Read-only scan of this PC. Installed Windows driver versions and available Chocolatey package versions use different numbering; these values do not establish whether a driver is up to date.", TextWrapping = TextWrapping.Wrap, Foreground = Frozen("#A7B0BF"), Margin = new Thickness(0, 8, 0, 12) });
            var status = new TextBlock { Name = "DriverPreviewStatus", Text = DriverPreviewSummary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) }; header.Children.Add(status);
            var cards = new StackPanel(); dock.Children.Add(new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            preview.Closed += delegate { closed = true; };
            Action render = delegate
            {
                if (closed) return;
                cards.Children.Clear();
                int installers = driverPreview == null ? 0 : driverPreview.Groups.FindAll(g => !string.IsNullOrEmpty(g.Package.PackageId)).Count;
                status.Text = driverPreviewBusy ? "Scanning hardware, installed Windows drivers and available packages…" : driverPreview == null ? "The scan has not run yet." : (driverPreview.Error.Length > 0 ? driverPreview.Error + " " : "") + installers + " proposed installer" + (installers == 1 ? "" : "s") + " · " + driverPreview.Rows.Count + " detected devices" + (!driverPreview.InventoryComplete ? ". Hardware detection is incomplete; compatibility may be unknown." : "");
                refresh.IsEnabled = !driverPreviewBusy;
                if (proceed != null) proceed.IsEnabled = DriverPreviewReady && !running;
                if (driverPreview == null || driverPreviewBusy) return;
                if (driverPreview.Rows.Count == 0) cards.Children.Add(new TextBlock { Text = driverPreview.Summary, TextWrapping = TextWrapping.Wrap });
                foreach (DriverPreviewGroup group in driverPreview.Groups)
                {
                    DriverPreviewRow row = group.Package;
                    var panel = new StackPanel();
                    panel.Children.Add(new TextBlock { Text = group.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                    panel.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(row.PackageId) ? "No reviewed automatic installer" : "Available package version: " + row.AvailablePackageVersion + " · " + row.PackageId, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
                    panel.Children.Add(new TextBlock { Text = group.Guidance, TextWrapping = TextWrapping.Wrap });
                    panel.Children.Add(new TextBlock { Text = row.MetadataDetail, TextWrapping = TextWrapping.Wrap, Foreground = Frozen("#A7B0BF"), Margin = new Thickness(0, 6, 0, 0) });
                    var devices = new StackPanel();
                    foreach (DriverPreviewRow device in group.Devices)
                    {
                        var detail = new StackPanel { Margin = new Thickness(4, 10, 0, 4) };
                        detail.Children.Add(new TextBlock { Text = device.Hardware + " · " + device.Kind, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                        detail.Children.Add(new TextBlock { Text = "Installed Windows driver: " + device.InstalledDriverVersion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
                        detail.Children.Add(new TextBlock { Text = device.DeviceId, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Frozen("#A7B0BF"), Margin = new Thickness(0, 4, 0, 0) });
                        devices.Children.Add(detail);
                    }
                    panel.Children.Add(new Expander { Name = "DriverDevices", Header = (string.IsNullOrEmpty(row.PackageId) ? "Device details" : "Devices covered by this proposal") + " · " + group.Devices.Count, Content = devices, Style = StyleOf("ReviewExpander"), Foreground = Frozen("#A7B0BF"), Margin = new Thickness(0, 12, 0, 0) });
                    if (row.SupportUri != null)
                    {
                        Uri target = row.SupportUri;
                        var link = new Hyperlink(new Run("Manufacturer / driver support")) { NavigateUri = target, Foreground = Frozen("#5EEAD4") };
                        link.RequestNavigate += delegate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e) { e.Handled = true; try { Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { Info("Support link unavailable", ex.Message); } };
                        var text = new TextBlock { Margin = new Thickness(0, 10, 0, 0) }; text.Inlines.Add(link); panel.Children.Add(text);
                    }
                    cards.Children.Add(new Border { Child = panel, Background = Frozen("#1B2029"), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) });
                }
            };
            Action scan = delegate { RefreshDriverPreview(render); render(); };
            refresh.Click += delegate { scan(); };
            preview.Loaded += delegate { scan(); };
            render();
            return preview;
        }
    }
}
