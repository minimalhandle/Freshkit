using System;

namespace Freshkit
{
    // Time-remaining estimate for a live install. It measures how fast packages actually finish in this run,
    // as observed by this window, so an estimate only appears once there is real evidence for it.
    internal sealed class SetupEta
    {
        public const int MinFinished = 2;
        public static readonly TimeSpan MinElapsed = TimeSpan.FromSeconds(30);
        string runId;
        int baseDone;
        DateTime baseTime;

        public void Reset() { runId = null; }

        /// Records an observation and returns the estimated time left, or null while evidence is insufficient.
        public TimeSpan? Observe(string id, int done, int queue, DateTime now)
        {
            if (runId != id || done < baseDone) { runId = id; baseDone = done; baseTime = now; return null; }
            if (queue <= 0) return null;
            int finished = done - baseDone;
            TimeSpan elapsed = now - baseTime;
            if (finished < MinFinished || elapsed < MinElapsed) return null;
            return TimeSpan.FromSeconds(elapsed.TotalSeconds / finished * queue);
        }

        /// Deliberately coarse, because install times vary a lot between packages.
        public static string Describe(TimeSpan? left)
        {
            if (left == null) return "";
            double minutes = left.Value.TotalMinutes;
            if (minutes < 1) return "less than a minute left";
            if (minutes < 60) { int m = (int)Math.Ceiling(minutes); return "about " + m + " minute" + (m == 1 ? "" : "s") + " left"; }
            if (minutes >= 180) return "more than 3 hours left";
            double hours = Math.Round(minutes / 30) / 2;
            return "about " + hours.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " hour" + (hours == 1 ? "" : "s") + " left";
        }
    }

    internal enum TaskbarSignal { None, Indeterminate, Normal, Paused, Error }

    internal static class SetupSignals
    {
        /// Maps Progress page state to the taskbar button's progress state and fraction.
        public static TaskbarSignal Taskbar(bool busy, bool live, string status, int done, int total, out double value)
        {
            value = total > 0 ? Math.Min(1, (double)done / total) : 0;
            if (!busy) { value = 0; return TaskbarSignal.None; }
            if (live && status == "NeedsReview") return TaskbarSignal.Error;
            if (live && status == "AwaitingReboot") return TaskbarSignal.Paused;
            if (live && total > 0) return TaskbarSignal.Normal;
            return TaskbarSignal.Indeterminate;
        }

        /// The finish notice is only useful when the user isn't already looking at Freshkit.
        public static bool ShouldNotify(bool windowActive, bool windowMinimized) { return !windowActive || windowMinimized; }

        /// Notification text for an install that ran to completion and produced a report.
        public static void InstallNotice(int ok, int bad, int skip, bool stageFailures, out string title, out string text, out bool warning)
        {
            warning = bad > 0 || stageFailures;
            title = warning ? "Freshkit setup needs attention" : "Freshkit setup finished";
            text = ok + " installed, " + bad + " failed, " + skip + " skipped." + (stageFailures && bad == 0 ? " A setup step failed." : "") +
                (warning ? " Open Freshkit to review and retry." : "");
        }

        /// Notification text for a job that ended without an install report (tweaks, drivers or offline kits only, or a failure).
        /// Returns false when no notice should be shown: a restart is continuing the job or administrator permission was declined.
        public static bool JobNotice(string label, int rc, out string title, out string text, out bool warning)
        {
            title = text = null; warning = false;
            if (rc == 3010 || rc == Proc.UacDeclined) return false;
            warning = rc != 0;
            title = rc == 0 ? "Freshkit: " + label + " finished" : "Freshkit: " + label + " needs attention";
            text = rc == 0 ? "All selected steps are complete." : rc == 2 ? "Finished, but some items were skipped. Open Freshkit for details." : "Finished with errors (exit code " + rc + "). Open Freshkit to review and retry.";
            return true;
        }
    }
}
