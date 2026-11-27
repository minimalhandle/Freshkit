using System;

namespace Freshkit
{
    internal static class SignalsTests
    {
        public static void Run(Action<bool, string> check)
        {
            var eta = new SetupEta(); var t0 = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
            check(eta.Observe("run1", 3, 10, t0) == null, "The first observation of a run gives no estimate");
            check(eta.Observe("run1", 4, 9, t0.AddMinutes(2)) == null, "One finished package is not enough evidence for an estimate");
            TimeSpan? left = eta.Observe("run1", 5, 8, t0.AddMinutes(4));
            check(left.HasValue && Math.Abs(left.Value.TotalMinutes - 16) < 0.01, "The estimate uses this run's observed pace (2 per 4 minutes, 8 left = 16 minutes)");
            check(eta.Observe("run1", 7, 0, t0.AddMinutes(6)) == null, "No estimate once nothing is queued");
            check(eta.Observe("run2", 9, 4, t0.AddMinutes(7)) == null && eta.Observe("run2", 11, 2, t0.AddMinutes(7).AddSeconds(10)) == null, "A new run restarts measurement and ignores bursts shorter than 30 seconds");
            var restarted = new SetupEta(); restarted.Observe("r", 6, 4, t0);
            check(restarted.Observe("r", 2, 8, t0.AddMinutes(5)) == null, "A lower done count (retry or reset) restarts measurement instead of producing a negative pace");
            check(SetupEta.Describe(null) == "" && SetupEta.Describe(TimeSpan.FromSeconds(40)) == "less than a minute left" && SetupEta.Describe(TimeSpan.FromSeconds(61)) == "about 2 minutes left" && SetupEta.Describe(TimeSpan.FromMinutes(1)) == "about 1 minute left", "Short estimates round up to whole minutes");
            check(SetupEta.Describe(TimeSpan.FromMinutes(95)).StartsWith("about 1.5 hour") && SetupEta.Describe(TimeSpan.FromHours(5)) == "more than 3 hours left", "Long estimates stay coarse");

            string stateJson = "{\"Status\":\"Running\",\"Failure\":\"\",\"UpdatedUtc\":\"2026-11-17T12:00:00.0000000Z\",\"InFlight\":{\"Phase\":\"App\",\"PackageId\":\"msiafterburner\"}," +
                "\"DriverQueue\":[],\"DriverResults\":[],\"AppResults\":[{\"PackageId\":\"winget-discord\",\"Name\":\"Discord\",\"Status\":\"PackageInstalled\"}]," +
                "\"AppQueue\":[{\"packageId\":\"msiafterburner\",\"name\":\"MSI Afterburner\"},{\"packageId\":\"steam\",\"name\":\"Steam\"}]}";
            object state = Engine.Json.DeserializeObject(stateJson);
            RunSnapshot alive = Runs.Parse("r1", state, true, t0.AddMinutes(1));
            check(alive.Status == "Running" && alive.Current == "MSI Afterburner" && alive.Rows[0].Outcome == "Running", "A run whose engine is alive shows its in-flight app as installing");
            RunSnapshot gone = Runs.Parse("r1", state, false, t0.AddMinutes(1));
            check(gone.Status == "Interrupted" && gone.Current == "MSI Afterburner" && gone.Rows[0].Outcome == "Pending" && gone.Rows[0].StatusText == "Interrupted" && gone.Queue == 2 && gone.Ok == 1,
                "A Running run whose engine window was closed is shown as interrupted, not installing, and the stopped app stays queued");
            check(!Runs.WorkerAlive("00000000000000000000000000000000"), "A run with no engine mutex has no live worker");
            object ready = Engine.Json.DeserializeObject(stateJson.Replace("\"Running\"", "\"Ready\""));
            check(Runs.Parse("r1", ready, false, t0.AddMinutes(1)).Status == "Ready" && Runs.Parse("r1", ready, false, t0.AddMinutes(5)).Status == "Interrupted", "A just-created run is still starting; one left Ready with no engine for minutes is interrupted");

            double value;
            check(SetupSignals.Taskbar(false, true, "Running", 3, 6, out value) == TaskbarSignal.None && value == 0, "The taskbar shows no progress when nothing is running");
            check(SetupSignals.Taskbar(true, true, "Running", 3, 6, out value) == TaskbarSignal.Normal && Math.Abs(value - 0.5) < 0.001, "A live install shows its fraction on the taskbar");
            check(SetupSignals.Taskbar(true, false, "", 0, 0, out value) == TaskbarSignal.Indeterminate, "Preparation stages show indeterminate taskbar progress");
            check(SetupSignals.Taskbar(true, true, "Interrupted", 3, 6, out value) == TaskbarSignal.Paused, "An interrupted install is paused on the taskbar");
            check(SetupSignals.Taskbar(true, true, "AwaitingReboot", 3, 6, out value) == TaskbarSignal.Paused && SetupSignals.Taskbar(true, true, "NeedsReview", 3, 6, out value) == TaskbarSignal.Error, "Waiting for restart is paused and a stopped install is an error on the taskbar");

            check(SetupSignals.ShouldNotify(false, false) && SetupSignals.ShouldNotify(true, true) && !SetupSignals.ShouldNotify(true, false), "Finish notices appear only when Freshkit isn't in front");
            string title, text; bool warning;
            SetupSignals.InstallNotice(12, 0, 3, false, out title, out text, out warning);
            check(!warning && title == "Freshkit setup finished" && text.StartsWith("12 installed, 0 failed, 3 skipped."), "A clean install notice reports its counts");
            SetupSignals.InstallNotice(12, 0, 3, true, out title, out text, out warning);
            check(warning && text.Contains("A setup step failed") && text.Contains("retry"), "A failed stage makes the notice a warning even with no failed packages");
            check(!SetupSignals.JobNotice("Install", 3010, out title, out text, out warning) && !SetupSignals.JobNotice("Install", Proc.UacDeclined, out title, out text, out warning), "No notice for a restart that continues setup or a declined administrator prompt");
            check(SetupSignals.JobNotice("Windows tweaks", 1, out title, out text, out warning) && warning && text.Contains("exit code 1"), "Failed jobs produce a warning notice with the exit code");
        }
    }
}
