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

## Pages

| Page | Features |
|---|---|
| Apps | 168 built-in apps (Freshkit's curated list, Sleekmeeter and Afterclip from GitHub, and every app Ninite offers) in a category rail with live counts, a Selected view, responsive cards with logos and installed badges, an installed-app scan, starter and session profiles, and a setup review before starting. **Add apps** is one search box across Chocolatey, winget, the Microsoft Store and GitHub releases: each app appears once, from the source with its newest release, with a source badge, publisher, an Installed badge (with version) when it's already on the PC, and a Website link (https only). Other sources and their versions are noted; owner/repo or a github.com link finds a project directly. |
| Windows tweaks | Embedded Win11Debloat settings, removable preinstalled apps, recommended presets and optional restore point. |
| Drivers & offline kit | Hardware scan and driver installation. Optional driver/Wi-Fi exports go to a folder you explicitly choose; choose a saved kit to use it in the current session. |
| Progress | Seven-stage checklist: offline drivers, Wi-Fi, tweaks, Chocolatey setup, hardware drivers, apps/runtimes and settings restore. Per-app results, restart recovery, failed-step details and targeted retries. |
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
- Silage: UI preferences; excludes device identity and saved sign-in.

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
