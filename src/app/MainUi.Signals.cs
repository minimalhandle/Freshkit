using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace Freshkit
{
    // Signals for setups that run while the user is elsewhere: taskbar progress, an estimate of time left, and a
    // Windows notification plus taskbar flash when a setup finishes in the background.
    internal sealed partial class MainUi
    {
        SetupEta setupEtaState;
        SetupEta setupEta { get { return setupEtaState ?? (setupEtaState = new SetupEta()); } }
        System.Windows.Forms.NotifyIcon finishNotice;

        [StructLayout(LayoutKind.Sequential)]
        struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags; public uint Count; public uint Timeout; }
        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FlashInfo info);

        string EstimateText(RunSnapshot s)
        {
            string text = SetupEta.Describe(setupEta.Observe(s.RunId, s.Done, s.Queue, DateTime.UtcNow));
            return text.Length > 0 ? "  ·  " + text : "";
        }

        void UpdateTaskbar(bool busy, RunSnapshot active)
        {
            if (Window.TaskbarItemInfo == null) Window.TaskbarItemInfo = new TaskbarItemInfo();
            double value;
            TaskbarSignal signal = SetupSignals.Taskbar(busy, active != null, active == null ? "" : active.Status, active == null ? 0 : active.Done, active == null ? 0 : active.Total, out value);
            TaskbarItemProgressState state;
            switch (signal)
            {
                case TaskbarSignal.Normal: state = TaskbarItemProgressState.Normal; break;
                case TaskbarSignal.Paused: state = TaskbarItemProgressState.Paused; break;
                case TaskbarSignal.Error: state = TaskbarItemProgressState.Error; break;
                case TaskbarSignal.Indeterminate: state = TaskbarItemProgressState.Indeterminate; break;
                default: state = TaskbarItemProgressState.None; break;
            }
            Window.TaskbarItemInfo.ProgressState = state;
            Window.TaskbarItemInfo.ProgressValue = value;
            if (!busy) setupEta.Reset();
        }

        void NotifyFinished(string title, string text, bool warning)
        {
            if (!SetupSignals.ShouldNotify(Window.IsActive, Window.WindowState == WindowState.Minimized)) return;
            try
            {
                IntPtr handle = new WindowInteropHelper(Window).Handle;
                // Flash the taskbar button until Freshkit is brought to the front (FLASHW_TRAY | FLASHW_TIMERNOFG).
                if (handle != IntPtr.Zero) { var info = new FlashInfo { Window = handle, Flags = 2 | 12 }; info.Size = (uint)Marshal.SizeOf(info); FlashWindowEx(ref info); }
                HideFinishNotice();
                // A temporary notification-area icon shows the balloon, which Windows 10/11 present as a toast.
                var icon = new System.Windows.Forms.NotifyIcon { Text = "Freshkit" };
                try { using (var stream = Engine.Resource("app.ico")) icon.Icon = new System.Drawing.Icon(stream); } catch { icon.Icon = System.Drawing.SystemIcons.Application; }
                Action open = delegate { Ui(delegate { if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal; Window.Activate(); Navigate("NavProgress"); HideFinishNotice(); }); };
                icon.BalloonTipClicked += delegate { open(); };
                icon.MouseClick += delegate { open(); };
                icon.BalloonTipClosed += delegate { Ui(HideFinishNotice); };
                icon.Visible = true;
                finishNotice = icon;
                icon.ShowBalloonTip(15000, title, text, warning ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception ex) { Log("Couldn't show the finish notification: " + ex.Message); }
        }

        void HideFinishNotice()
        {
            if (finishNotice == null) return;
            finishNotice.Visible = false; finishNotice.Dispose(); finishNotice = null;
        }
    }
}
