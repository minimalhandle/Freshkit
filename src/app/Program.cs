// Freshkit - portable bulk installer for fresh Windows installs. Single-file WPF app (.NET Framework 4.x, C# 5).
// Build with build.ps1. Engine (originally InstallApps.bat) and Win11Debloat are embedded and extracted to %TEMP% per action.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

[assembly: AssemblyTitle("Freshkit")]
[assembly: AssemblyProduct("Freshkit")]
[assembly: AssemblyDescription("Freshkit - bulk install apps, drivers, tweaks and settings on a fresh Windows install")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Freshkit
{
    internal static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern bool AllocConsole();
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);
        [DllImport("kernel32.dll")] static extern int GetFileType(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        [STAThread]
        static int Main(string[] args)
        {
            bool progress = args.Length == 1 && args[0].Equals("--progress", StringComparison.OrdinalIgnoreCase);
            if (args.Length > 0 && !progress) return RunCli(args);
            // One window at a time (the reopen-after-restart entry can race a manual launch).
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "Local\\Freshkit-UI", out created))
            {
                if (!created) return 0;
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += delegate (object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Freshkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };
                var ui = new MainUi(progress);
                return app.Run(ui.Window);
            }
        }

        static bool Is(string a, params string[] names)
        {
            foreach (string n in names) if (a.Equals(n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool IsRedirected(int which)
        {
            IntPtr h = GetStdHandle(which);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) return false;
            int t = GetFileType(h);
            return t == 1 || t == 3;
        }

        // Same command line as the original BAT: Freshkit.exe [Mode] [ArchivePath|RunId] [Profiles|ALL]
        static int RunCli(string[] args)
        {
            string mode = args[0];
            bool help = Is(mode, "/?", "-?", "help", "/help", "--help", "-h");
            bool settingsMode = Is(mode, "Backup", "Restore", "BackupPlan", "RestorePlan", "Inspect");
            bool interactive = Is(mode, "Home", "Restore") || (settingsMode && args.Length < (Is(mode, "Inspect") ? 2 : 3));
            bool redirected = IsRedirected(-11);
            bool ownConsole = !redirected && (interactive || !AttachConsole(-1));
            if (ownConsole) { AllocConsole(); Console.Title = "Freshkit"; }
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            if (!redirected)
            {
                var conout = new FileStream(CreateFile("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero), FileAccess.Write);
                Console.SetOut(new StreamWriter(conout, new UTF8Encoding(false)) { AutoFlush = true });
            }
            else Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.WriteLine();
            if (help)
            {
                Console.WriteLine("Freshkit - bulk install apps on a fresh Windows install");
                Console.WriteLine();
                Console.WriteLine("  Freshkit.exe                 Open the app");
                Console.WriteLine("  Freshkit.exe Install <recipe.json>   Unattended install of a recipe's apps (Freshkit recipe or winget export)");
                Console.WriteLine("  Freshkit.exe Plan | List <recipe.json>   Preview a recipe's apps without installing");
                Console.WriteLine("  Freshkit.exe Home            Text menu");
                Console.WriteLine("  Freshkit.exe Plan | List | Drivers | DriverPlan | Bootstrap | SettingsList");
                Console.WriteLine("  Freshkit.exe Resume | CancelResume [RunId]");
                Console.WriteLine("  Freshkit.exe Backup | BackupPlan | Restore | RestorePlan | Inspect [ZipPath] [ALL|id,id]");
                Console.WriteLine();
                Console.WriteLine("Packages and settings rules are built into this standalone EXE. Companion lists are ignored.");
                Console.WriteLine("UI changes and profiles last for the current session only. Optional exports are chosen in the app.");
                return Finish(ownConsole, 0);
            }
            var values = new Dictionary<string, string>();
            bool resume = Is(mode, "Resume", "CancelResume");
            if (args.Length > 1) values[resume ? "RunId" : "ArchivePath"] = args[1];
            if (args.Length > 2) values["Profiles"] = args[2];
            List<string> switches = null;
            if (CliRecipe.Accepts(mode, args))
            {
                // Install|Plan|List <recipe.json>: the recipe's catalog and app selection for this run only.
                values.Remove("ArchivePath");
                try
                {
                    var recipe = CliRecipe.Load(args[1], Engine.ReadResourceText(Engine.ListFileName));
                    Engine.SessionList = recipe.ListText;
                    values["Apps"] = string.Join(",", recipe.Apps.ToArray());
                    switches = new List<string> { "IncludeDisabled" };
                    Console.WriteLine(CliRecipe.Notice(recipe));
                }
                catch (Exception ex) { Console.WriteLine("ERROR: " + ex.Message); return Finish(ownConsole, 1); }
            }
            string work = null, engine = null;
            int rc = 1;
            try
            {
                work = Engine.ExtractWork(); engine = Engine.ExtractEngine();
                var psi = new ProcessStartInfo(Engine.PowerShellPath, Engine.RunnerArguments(work, engine, mode, values, switches, !interactive));
                psi.UseShellExecute = false;
                if (interactive) { using (Process p = Process.Start(psi)) { p.WaitForExit(); rc = p.ExitCode; } }
                else rc = Proc.Stream(psi, delegate (string line) { lock (Console.Out) Console.Out.WriteLine(line); }, null);
            }
            catch (Exception ex) { Console.WriteLine("ERROR: " + ex.Message); }
            finally { Engine.Cleanup(work); Engine.Cleanup(engine); }
            Console.WriteLine("Setup exit code: " + rc);
            return Finish(ownConsole, rc);
        }

        static int Finish(bool ownConsole, int rc)
        {
            if (ownConsole)
            {
                Console.WriteLine();
                Console.Write("Press Enter to close.");
                try { using (var conin = new StreamReader(new FileStream(CreateFile("CONIN$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero), FileAccess.Read))) conin.ReadLine(); } catch { }
            }
            else Console.WriteLine();
            FreeConsole();
            return rc;
        }
    }
}
