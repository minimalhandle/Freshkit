# Neutral defaults for a public release

## Goal
Freshkit's built-in selection came from one person's original install script. Before publishing, ship defaults that
suit most PCs and keep personal choices in recipes.

## Changes
- **Ticked by default (15):** Firefox, Google Chrome, VLC, 7-Zip, Spotify, Discord, Steam, Zoom, Notepad++, Adobe
  Acrobat Reader, Everything search, Microsoft PowerToys, Visual C++ runtimes and the .NET Desktop runtimes (latest and 8).
  Every other catalog app, including Sleekmeeter and Afterclip, is listed unticked.
- **Firefox** uses Mozilla's standard `Mozilla.Firefox` (en-US) package; the language picker still offers other locales,
  including en-GB.
- A personal backup profile for an app that isn't in the catalog or generally available was removed.
- **MSI Afterburner** stays on winget's `Guru3D.Afterburner` (4.6.6, the current stable release), which installs Guru3D's
  official build without the Norton offer bundled in MSI's website installer. A catalog comment records why.

## Kept
Sleekmeeter and Afterclip (public GitHub releases) stay in the catalog, unticked, with their backup profiles. Test
fixtures that use them as GitHub examples are unchanged.
