using System;
namespace Freshkit
{
    internal static class SystemFactsTests
    {
        public static void Run(Action<bool, string> check)
        {
            var facts = SystemFacts.FormatWindows("Windows 10 Enterprise", "25H2", "26200", 7171);
            check(facts.Edition == "Windows 11 Enterprise 25H2" && facts.Build == "Build 26200.7171", "Windows edition and full build revision occupy separate fields");
            var missing = SystemFacts.FormatWindows("Windows", "", "", null);
            check(missing.Edition == "Windows" && missing.Build == "Build unavailable", "Missing OS metadata is explicit instead of a misleading build number");
        }
    }
}
