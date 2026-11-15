# Freshkit portable reliability

User scope: implement recommendations 1–4. Ship one standalone EXE. Custom lists and profiles are session-only; restarting loads embedded defaults (explicit user choice).

Approach: retain WPF/C# 5 and embedded PowerShell. Do not read or create automatic companion lists or kit folders. Edit lists in memory; choose an external folder only when explicitly exporting/importing an optional driver kit. Backup ZIPs remain explicit outputs. Preserve existing exported files outside the release folder.

Backup definitions: embedded JSON is the baseline independent of package selections. Session CONFIG rules explicitly override a named profile; missing profiles retain built-in definitions. NOCONFIG explicitly disables a profile. Show discovery status, included file count/estimated size, running apps and scan errors; apply the same exclusions and path safeguards as backup.

Jobs: persist a seven-stage checklist in the existing temporary local log area. Record native exit codes and failures; do not report success after an earlier failure. Stop before subsequent setup stages on prerequisite failure. Display remaining stages as blocked. Preserve a recipe and the session list snapshot for retries after reopening. Engine driver/app progress derives from protected resumable engine state and final reports. Restore has its own outcome, including partial skips. Retry only failed stages/items and retain successful outcomes.

Constraints: no installed runtime beyond in-box .NET Framework/Windows PowerShell. Temporary extracted files, local reboot recovery and failure logs remain necessary while executing; successful jobs clean up. No installs, registry modifications, driver changes or reboot calls during tests.

Validation: compile, native-command failure harness, pure C# session/catalog/progress/discovery tests, synthetic backup round trip, read-only CLI from an EXE-only folder, and WPF runtime layout/control validation.
