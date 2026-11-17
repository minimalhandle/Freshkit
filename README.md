# Freshkit

A standalone portable Windows 11 setup app. Copy **only `dist\Freshkit.exe`** to another PC or USB drive. The UI, app catalog, backup definitions, install engine, helper scripts, Win11Debloat and icon are embedded. It uses the .NET Framework 4.x and Windows PowerShell 5.1 supplied with Windows; app installation needs internet access.

## Built-in catalog and defaults

`src/Freshkit.lists.txt` holds the catalog that is embedded at build time. `::PACKAGE` lines are ticked by default, `::DISABLED` lines are listed but unticked, and a `::GROUP Name` line files the packages below it under that Apps page category. Apps added from search go above the first group and are categorised automatically.

- **Ticked by default (41):** Freshkit's original curated list, minus the end-of-life .NET 7 Desktop Runtime, plus Sleekmeeter and Afterclip.
- **Unticked (125):** every other app from ninite.com. Architecture variants are folded into one x64 entry (Freshkit targets Windows 11 x64), and the Node.js 22/24 entries become *Node.js LTS* next to the existing *Node.js Current*.
- **Sources, winget first:** each app uses winget when the publisher's winget package is as new as Chocolatey's (80 apps, e.g. Firefox via Mozilla's `Mozilla.Firefox.en-GB`), otherwise Chocolatey (83). Chocolatey is kept where it is newer, where versions can't be compared, for all runtimes, for install-order pairs (OpenRGB/PawnIO, Streamlink/Twitch GUI), for Spotify (its installer refuses to run as administrator) and for Python 3 (winget pins one minor version). The reviewed mapping is recorded in `tools/winget-migration.json`. Within each category apps are listed alphabetically, whatever their source.
- **Microsoft Store apps:** apps that are Store-only or clearly newer there (WhatsApp, Blip, iCloud) are `::WINGET` entries using Store product IDs (`9…` or `XP…`); the engine installs those from winget's `msstore` source. Search reads Store versions and logos from Microsoft's public Store catalog, and version comparison skips date-style versions (2026.1109) against semantic ones (11.42) so a lookalike or differently-versioned listing isn't wrongly preferred. The Store is a last resort: search uses it only when it is the only source, or when its release is newer at the major/minor level; same or unknown versions keep the regular installer (e.g. Spotify, ChatGPT and Windows App come from winget). GitHub projects are never merged with same-named apps from other sources.
- **GitHub releases:** `::GITHUB owner/repo|Display name|Optional silent arguments` installs the latest release's installer. Freshkit prefers an x64 or architecture-neutral `.msi`, then a `setup`/`install` `.exe`, skipping ARM, 32-bit and portable builds. It verifies the download's size and GitHub's SHA-256 digest when one is published, and records the Authenticode status in the log. MSI, NSIS and Inno Setup installers are run silently automatically; any other installer type needs its silent switches as the third field. The display name must match Installed apps, which is how Freshkit detects an existing install. GitHub releases aren't reviewed by a package repository, so add projects you trust. Sleekmeeter and Afterclip are included this way and ticked by default.
- **Starter profiles:** *essentials*, *gaming*, *developer* and *office & family* in the Profile picker. They omit `tweaks=`/`remove=`, so applying one changes only the app selection.

## Session-only customization

Freshkit never automatically reads or creates `Freshkit.lists.txt` or `FreshkitKit` beside the EXE. App additions/removals, edited rules and saved profiles last until the app closes. Reopening loads embedded defaults. The **⋯** menu next to Profile holds **Edit list** (an in-app session editor), **Reset** (restores defaults) and session profile commands.

An active install keeps a temporary snapshot of its exact selection so it can continue after a restart and retry failures. That snapshot is job recovery data, not persistent preferences for the next setup.

**Explicit setup recipes:** use **⋯ → Export setup recipe…** to save the current catalog and selected apps, Windows tweaks/removals, restore-point/Edge options and backup-profile selections as a `.freshkit.json` file. **Import setup recipe…** validates the file and previews the selected apps, source IDs and installer arguments before **Apply to session**. Import only changes selections; use **Review setup** to start installation. Recipes contain definitions and selections, not settings-backup contents or driver/Wi-Fi kits. They can include custom paths and installer arguments, so review them before sharing. Freshkit never automatically reads a recipe on startup.

The same import action accepts winget export JSON from the built-in `winget` and `msstore` sources. It replaces the app selection and adds missing IDs to the session catalog, preserving current Windows and backup selections. Version pins are ignored; setup uses current releases. Custom package sources and malformed files are rejected before changing the session.

**Per-app installation options:** right-click an app card and choose **Installation options…**. The editor distinguishes Chocolatey package parameters, winget installer overrides and GitHub installer arguments. Documented Chocolatey Firefox language and Git component presets are included; other apps accept their installer's documented options. Winget/GitHub EXE overrides must include the installer's silent switches. GitHub MSI properties are appended to the fixed silent invocation. Store overrides are unavailable. Arguments are saved to this session's catalog, appear in setup review, and travel with recipes and job/restart snapshots. Package IDs, sources and download URLs cannot be changed through this editor.

**Integrated tweak controls:** opening **Windows tweaks** starts a read-only background check of the embedded Win11Debloat registry definitions. Each control separates its **Current** state from the selected **Next setup** action. Detection never selects changes for you. Confirmed current settings stay selected in profiles and recipes but are skipped when building a job; unknown or different settings remain pending. The setup review lists what will apply and what will be skipped. Registry state cannot prove every action's side effects: telemetry, Bing, Copilot, optional Windows features, Widgets and Store search suggestions remain Unknown when the complete action cannot be verified. **Refresh detection** also refreshes removable-app installation state without elevation.

**Guided app options:** right-click an app in Apps to edit its installation options. Firefox offers verified language choices, including separate winget locale packages. Chocolatey Firefox offers shortcut and maintenance-service options; Chocolatey Git offers shell-integration and Git LFS options. The editor previews the package and arguments that will be saved, preserves custom advanced arguments, and provides **Reset defaults**. Locale changes also update selected session profiles; duplicate package identities are rejected before changing the catalog. These options configure the next installation request; an existing installation may retain its language or components.

**Driver preview:** **Drivers → Scan hardware** opens a read-only preview of detected hardware, installed Windows driver versions, proposed packages, available package versions and manufacturer support links. Driver installation opens the same preview before explicit continuation. Setup review also scans before starting with drivers enabled. Windows driver and Chocolatey package versions use different numbering and are never treated as an update comparison. Missing inventory or metadata is shown explicitly; execution still validates supported hardware.

The preview shows one card per proposed installer, so AMD chipset devices sharing a package appear together. Expand **Devices covered by this proposal** to see every device, hardware ID and installed driver version. Different installers and hardware without a reviewed automatic package remain separate. Setup review summarizes installers and device counts.

**Preflight checks:** setup review runs read-only checks for system/temporary drive free space, standard Windows pending-restart signals, required source HTTPS endpoints, winget availability and running apps named in the chosen settings backup. Results show warnings and suggested actions, with **Recheck** and a Windows Storage settings link when space needs attention. Passed checks collapse into a short list. Checks never reboot, close apps or repair sources. Starting waits for checks to finish; results older than two minutes require rechecking. Warnings can be reviewed and continued. Source connectivity does not guarantee individual package availability; disk guidance (5 GB system / 2 GB temporary space) is a heuristic, not a download or installed-size estimate. The install/restore engine retains its own validation.

**Setup comparison:** **Apps → Compare** checks the current selected apps, Windows settings/removals and backup profiles against this PC. **Profile ⋯ → Compare recipe with this PC…** reads a saved Freshkit recipe or supported winget export without importing it. Results distinguish verified presence, missing apps, different settings, unavailable/running backup settings and unknown evidence. Comparison uses the loaded recipe's own backup definitions and never changes the active catalog, selections or Windows settings. **Only show items needing attention or confirmation** filters results; clear it to see verified checks. **Recheck** captures fresh evidence.

**Post-setup verification:** after setup and any settings restoration finish, Progress automatically checks the original requested apps, actual Windows changes, present-device problem codes and pending restart signals. Terminal failures before an engine report also receive verification. Original targets survive targeted retries and restart recovery. **View verification** opens the results; **Verify again** repeats the scan. Verification is separate from installer results and never adds apps to the retry queue or repairs anything. Reports remain available for the current session and do not create a persistent setup history.

App verification checks local package/uninstall registration, not whether an app launches, is the latest release, or accepted its installer arguments. GitHub needs a confirmed repository identity. Name-only desktop matches require a complete registry inventory. Store products lacking an exact local identity remain Unknown. Unsupported tweak effects remain Unknown; app-removal checks cover the current user's inventory. Device checks label disabled hardware separately from missing drivers and do not compare INF and installer package versions. Partial, inaccessible, translated or truncated inventories are explained rather than treated as successful checks.

Each tweak section offers **Select all** and **Clear** for its displayed controls. Mutually exclusive settings use the first supported option when selected, matching Win11Debloat's bulk-selection behavior. **Quick select** offers the vendor's **Privacy and suggested content** and **AI features** categories without changing other categories. Under Remove preinstalled apps, **Only show installed apps** matches exact Appx package names or winget IDs, including aliases. Filtering preserves selections; the count warns about hidden selections. Unknown inventory results remain visible and are explained in the detection status.

**Graphics drivers:** AMD Adrenalin support is retained and tested; reviewed Intel Arc A/B and Core Ultra Series 1/2 device IDs now use the current Intel graphics package. Hybrid systems can select multiple matching vendors. Older or unreviewed Intel adapters receive OEM/Intel driver-selection guidance. AMD's existing vendor-based detection does not establish compatibility for every legacy/workstation/OEM GPU. See [driver support and verification limits](docs/driver-support.md).

The selected **Modular F** logo is used by the sidebar and multi-size Windows icon. Artwork, alternatives and generation prompts are retained under `src/freshkit-logo.png` and `docs/logo-concepts/`; `tools/make-icon.ps1` packages the selected PNG into `src/app.ico`.

The sidebar slogan is **Fresh start. Your setup.** Its **This PC** panel shows Windows edition/release, a separate build/revision line, architecture, system-drive free space, Chocolatey, winget availability and connection state. Details wrap, and navigation can scroll in shorter windows. Builds notify Explorer about the changed executable; use `powershell -ExecutionPolicy Bypass -File build.ps1 -RefreshIconCache` when Windows has retained an older cached icon. This sends Shell notifications without deleting cache files or restarting Explorer.

**Installed app identity:** GitHub projects require their repository URL in Windows' uninstall metadata or an explicit association with a particular uninstall entry. A name or release-version coincidence is insufficient. Search displays **Possible match · Link installed app** for an unambiguous name-only match; confirm the publisher/version and exact repository before linking. Links are stored on this PC in `%LOCALAPPDATA%\Freshkit\installed-identities.json`, separately from session selections, and are invalidated if the registration's stable metadata changes. Successful GitHub installs record this association for later updates. Unconfirmed projects are excluded from automatic updates and reported as needing confirmation.

Search displays results as each source completes, cancels outstanding requests when replaced or closed, and uses a shared 30-second deadline. Unavailable or timed-out sources produce an incomplete-search notice. Update sources run concurrently; failed checks remain visible even when no updates were found. Successful results from a partially failed source are retained. GitHub release metadata expires after five minutes and update checks fetch it afresh. The versioned icon cache ignores older images that may have been attributed by display name.

Large weekly-update payloads are compressed inside the scheduled command, retaining GitHub updates without companion scripts. Re-enable weekly updates to replace an existing task with the current updater; building the EXE does not modify scheduled tasks.

**Background progress:** while a setup runs, the Freshkit taskbar button shows its progress. The bar is green with the share of apps and drivers finished, moves continuously during preparation stages, turns yellow while waiting for a restart and red if the install stops. Once at least two packages have finished and 30 seconds have passed, Progress adds a rough time left (for example *about 6 minutes left*). The estimate uses the pace of the current run, so it starts again after a restart or retry. When a setup finishes while Freshkit isn't the active window, the taskbar button flashes and a Windows notification gives the result (installed/failed/skipped, or that it needs attention). Clicking the notification opens Progress. No notice is shown for a restart that continues setup or for a declined administrator prompt. The notification icon is temporary and is removed when the notification closes.

## Pages

| Page | Features |
|---|---|
| Apps | 168 built-in apps (Freshkit's curated list, Sleekmeeter and Afterclip from GitHub, and every app Ninite offers) in a category rail with live counts, a Selected view, responsive cards with logos and installed badges, an installed-app scan, starter and session profiles, and a setup review before starting. **Add apps** is one search box across Chocolatey, winget, the Microsoft Store and GitHub releases: each app appears once, from the source with its newest release, with a source badge, publisher, an Installed badge (with version) when it's already on the PC, and a Website link (https only). Other sources and their versions are noted; owner/repo or a github.com link finds a project directly. |
| Windows tweaks | Embedded Win11Debloat settings, removable preinstalled apps, recommended presets and optional restore point. |
| Drivers & offline kit | Hardware scan and driver installation. Optional driver/Wi-Fi exports go to a folder you explicitly choose; choose a saved kit to use it in the current session. |
| Progress | Seven-stage checklist: offline drivers, Wi-Fi, tweaks, Chocolatey setup, hardware drivers, apps/runtimes and settings restore. Per-app results, restart recovery, failed-step details and targeted retries. Taskbar progress, a time-left estimate and a finish notification when Freshkit is in the background. |
| Updates | Update checks for Chocolatey, winget, Microsoft Store apps (installed MSIX version vs the Store catalog) and GitHub-release apps (Installed apps version vs the latest release), selected updates and optional weekly automatic updates. Store and GitHub updates only touch apps that are already installed; the weekly task carries its own copy of the GitHub updater so it still works without the EXE. |
| Settings backup | Settings found, app running, nothing to back up or scan error, with included file counts and estimated uncompressed size. Refresh after closing apps. Back up, inspect, preview and restore ZIPs. |

Native driver, Wi-Fi, tweak and update failures are checked. A failed prerequisite blocks later stages until retried. Driver/app failures remain visible while independent package work can finish. Retry preserves completed stages and narrows package work using the report belonging to that setup. Interrupted settings restoration is marked failed when reopening and can be retried.

**Review setup** lists selected apps, tweaks and removals and lets you choose driver installation, app upgrades, an available offline kit and optional settings restore. Edit links return to your selections. Opening or closing the review never starts a job; **Start setup** begins the reviewed work.

## Portability and outputs

Built-in apps ship with bundled 96px logos (`src/icons`, provenance in `src/icons/sources.json`); `tools/add-missing-icons.ps1` fetches logos for new catalog entries using only Windows PowerShell. App logos load in the background in search results, the installed-app scan, and app cards. Freshkit uses bundled logos for its built-in apps, then cached or installed-program icons, then Chocolatey metadata or a winget package's website icon. PNG, ICO and SVG images are supported. Downloaded icons are cached under `%LOCALAPPDATA%\Freshkit\icons` for offline reuse; unavailable icons keep the app's initial. Icon downloads never block adding an app. SVG rendering libraries and their license notices are embedded in the EXE (see `vendor/icons`).

- **The EXE has no required companion files.** Copying it alone is sufficient.
- **Optional user outputs:** settings backup ZIPs, pre-restore safety ZIPs and driver/Wi-Fi exports. These are your data; they are not needed to launch Freshkit. Driver exports may be several GB. Wi-Fi exports contain passwords in plain text.
- **Temporary execution files:** embedded scripts extract to uniquely named temp folders and are cleaned up. During a setup, `%LOCALAPPDATA%\Freshkit` contains job state, stage results and logs; `%ProgramData%\FreshWindowsSetupResume` contains protected reboot snapshots and a temporary scheduled task. Successful jobs clean their traces. Failed jobs retain logs and a retry recipe until deleted on Progress.
- **Optional weekly updates:** the scheduled task is self-contained and keeps working when the EXE/USB is removed. Its logs are retained on failure.
- **Win11Debloat:** integrated tweaks use `-SkipRegistryBackup`. Its temporary data is removed unless it existed before Freshkit. Use the optional restore point if you want Windows recovery protection.

Existing companion lists and kits from earlier releases can be kept as archived data. Their automatic loading is intentionally disabled.

## Settings backup rules

Built-in definitions are independent of the selected packages. An incomplete session list cannot hide newly shipped backup profiles. An explicit `CONFIG` block overrides that named profile; `NOCONFIG` explicitly disables it for the session.

Use **Edit list** to add session rules between `::BEGIN_CONFIG` and `::END_CONFIG`:

```
::CONFIG app-id|Display name|Root|Relative path|processes to close (optional)
::CONFIG myapp|My App|APPDATA|MyApp\settings.json|myapp
::CONFIG myapp|My App|APPDATA|MyApp\profiles
```

Roots: `APPDATA`, `LOCALAPPDATA`, `USERPROFILE`, `DOCUMENTS` or app-specific `HKCU` keys. Paths must remain within their root and cannot follow links. Display names should match Windows Installed Apps; metadata uses Chocolatey registration where available. Running apps are skipped, not force-closed. Restore validates the archive and its checksums, saves existing settings to a safety ZIP and skips incompatible major versions unless explicitly allowed.

Included profiles cover 7-Zip, Git, GitHub CLI preferences, Notepad++, OBS Studio, OpenRGB, PuTTY, VLC, WinSCP, Chatterino, Streamlink and:

- Logitech G HUB: settings database plus WAL/SHM files, Lua scripts and window state. Quit G HUB from its tray icon first.
- Afterclip: settings and library metadata; excludes API token files.
- Sleekmeeter: settings, audio events, scenes and sounds.
- mpv: `%APPDATA%\mpv`, including configuration, scripts and shaders; excludes logs, caches and playback resume state.

Discovery estimates the same selected paths/exclusions; file sizes can change before backup. Registry values are counted separately from file bytes.

## Command line

```
Freshkit.exe Install
Freshkit.exe Home
Freshkit.exe Plan | List | Drivers | DriverPlan | Bootstrap | SettingsList
Freshkit.exe Resume | CancelResume [RunId]
Freshkit.exe Backup | BackupPlan | Restore | RestorePlan | Inspect [ZipPath] [ALL|id,id]
```

CLI runs use embedded defaults and ignore companion lists. Install targets Windows 11 x64; preview commands are read-only.

## Source layout

| Path | Responsibility |
|---|---|
| `src/app/*.cs` | C# 5 WPF UI, CLI, session state, backup discovery and job progress/retry models. |
| `src/MainWindow.xaml` | Embedded runtime-loaded WPF layout/theme. |
| `src/Runner.ps1` | Session catalog snapshot and engine bridge; explicit backup-rule overrides. |
| `src/payload/` | Install, driver, reboot recovery, settings backup and structured status engine. |
| `src/scripts/` | Elevated job, updates, schedule and kit export helpers. |
| `src/Freshkit.lists.txt` | Build-time embedded defaults; not a release companion file. |
| `vendor/Win11Debloat/` | Vendored MIT source; provenance in `FRESHKIT-VENDOR.txt`. |
| `build.ps1` | Compiles the standalone EXE using in-box `csc.exe`. |
| `tests/` | Safe synthetic checks; no package installation or Windows settings changes. |

## Build and verify

```
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File tests\run-core.ps1
powershell -ExecutionPolicy Bypass -File tests\run-ui.ps1
powershell -ExecutionPolicy Bypass -File tests\job-status.ps1
powershell -ExecutionPolicy Bypass -File tests\engine-progress.ps1
powershell -ExecutionPolicy Bypass -File tests\settings-roundtrip.ps1
powershell -ExecutionPolicy Bypass -File tests\identity.ps1
powershell -ExecutionPolicy Bypass -File tests\schedule-payload.ps1
powershell -ExecutionPolicy Bypass -File tests\run-app-options.ps1
powershell -ExecutionPolicy Bypass -File tests\app-options-payload.ps1
powershell -ExecutionPolicy Bypass -File tests\drivers.ps1
powershell -ExecutionPolicy Bypass -File tests\run-driver-preview.ps1
powershell -ExecutionPolicy Bypass -File tests\run-preflight.ps1
powershell -ExecutionPolicy Bypass -File tests\run-audit.ps1
powershell -ExecutionPolicy Bypass -File tests\audit-scan.ps1
powershell -ExecutionPolicy Bypass -File tests\driver-preview.ps1
powershell -ExecutionPolicy Bypass -File tests\tweak-state.ps1
```

The tests cover session isolation, catalog merging, settings discovery/exclusions, stage failure/retry handling, report binding, interrupted restore and a synthetic backup/restore round trip. Real installer/reboot behaviour should additionally be exercised in a disposable Windows VM.

UI checks use synthetic search completions and build job specifications without running installers. Add `-Render` to `tests\run-ui.ps1` to save WPF screen previews in `obj\ui-previews`, including the minimum-size layout.

### Catalog health check

```
powershell -ExecutionPolicy Bypass -File toolscheck-catalog.ps1
```

Run it before a release (needs internet; changes nothing). It reports an **error** when a Chocolatey package has no current version, a Store product or winget ID no longer exists, a GitHub entry's latest release has no usable installer, or a logo is unreadable, and a **warning** for Chocolatey packages not updated in two years (`-StaleDays`), missing logos or unrecorded logo sources. Exit code 1 means at least one error. Some stale warnings are expected (old Visual C++ runtimes are frozen; Steam updates itself).

### GitHub requests

Release lookups use github.com's own pages (`/releases/latest` and `expanded_assets`, which include each file's SHA-256), so searches and installs don't use the API's 60-requests-an-hour allowance; the API is a fallback, and repository search uses the separate search allowance.

## Version control

The project is a git repository (`main`). `obj/` and `dist/` are build output and ignored; `.gitattributes` stores files byte-for-byte because scripts and the catalog depend on their existing line endings and encodings.

## Updating Win11Debloat

Replace `vendor\Win11Debloat` with the new release (exclude `.git`, `.github`, `Tests`, `Tools` and `Scripts\Get.ps1`), update its provenance file and rebuild. Labels, defaults and available tweaks come from the embedded vendor configuration.
