using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Controls;

namespace Freshkit
{
    internal sealed partial class MainUi
    {
        SetupRecipe CaptureRecipe()
        {
            bool custom;
            var recipe = new SetupRecipe { ListText = Engine.CurrentListText(out custom),
                RestorePoint = Get<CheckBox>("RestorePointSwitch").IsChecked == true, ForceEdge = Get<CheckBox>("ForceEdgeSwitch").IsChecked == true };
            foreach (var app in apps) if (app.IsSelected) recipe.Apps.Add(app.Key);
            if (debloat != null) { recipe.Tweaks = debloat.SelectedFeatures(); recipe.Remove = debloat.SelectedApps(); }
            foreach (var profile in settingsProfiles) if (profile.IsSelected) recipe.Backups.Add(profile.Id);
            return recipe;
        }

        void ValidateRecipe(SetupRecipe recipe)
        {
            // Revalidate even if a caller constructed the object directly.
            SetupRecipe.Parse(recipe.Serialize(), recipe.ListText);
            if (!recipe.HasWindowsSelections) return;
            if (debloat == null && (recipe.Tweaks.Count > 0 || recipe.Remove.Count > 0)) throw new InvalidDataException("Windows tweak definitions are unavailable on this PC.");
            var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var removals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (debloat != null)
            {
                foreach (var t in debloat.AllToggles) features.Add(t.FeatureId);
                var selected = new HashSet<string>(recipe.Tweaks, StringComparer.OrdinalIgnoreCase);
                foreach (var c in debloat.AllChoices)
                {
                    int matchingOptions = 0;
                    foreach (var values in c.Values)
                    {
                        int matches = 0; foreach (string f in values) { features.Add(f); if (selected.Contains(f)) matches++; }
                        if (matches > 0) { if (matches != values.Length) throw new InvalidDataException("Incomplete Windows option: " + c.Label); matchingOptions++; }
                    }
                    if (matchingOptions > 1) throw new InvalidDataException("Conflicting Windows options: " + c.Label);
                }
                foreach (var a in debloat.Apps) { removals.Add(a.AppId); foreach (string id in a.AppIds) removals.Add(id); }
            }
            foreach (string id in recipe.Tweaks) if (!features.Contains(id)) throw new InvalidDataException("Windows tweak unavailable on this PC: " + id);
            foreach (string id in recipe.Remove) if (!removals.Contains(id)) throw new InvalidDataException("Windows app removal unavailable on this PC: " + id);
        }

        void ApplyRecipe(SetupRecipe recipe)
        {
            if (running) throw new InvalidOperationException("Wait for the current job to finish before importing a recipe.");
            // Older recipes carry an older catalog: keep apps added to Freshkit since then available (unticked).
            int addedApps;
            recipe.ListText = CatalogMerge.AddMissing(recipe.ListText, Engine.ReadResourceText(Engine.ListFileName), out addedApps);
            ValidateRecipe(recipe);
            pendingSteamLibraries = null; // a reinstall folder sets these again after applying its recipe
            var backupSelections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in settingsProfiles) if (p.IsSelected) backupSelections.Add(p.Id);
            Engine.SessionList = recipe.ListText;
            LoadList(false);
            var selected = new HashSet<string>(recipe.Apps, StringComparer.OrdinalIgnoreCase);
            Batch(delegate { foreach (var a in apps) a.IsSelected = selected.Contains(a.Key); });
            if (recipe.HasWindowsSelections)
            {
                if (debloat != null) { debloat.ApplyFeatures(recipe.Tweaks); debloat.ApplyApps(recipe.Remove); RefreshTweakCounts(); }
                Get<CheckBox>("RestorePointSwitch").IsChecked = recipe.RestorePoint;
                Get<CheckBox>("ForceEdgeSwitch").IsChecked = recipe.ForceEdge;
                backupSelections = new HashSet<string>(recipe.Backups, StringComparer.OrdinalIgnoreCase);
            }
            foreach (var p in settingsProfiles) p.IsSelected = backupSelections.Contains(p.Id);
            UpdateCount();
            Log("Applied setup recipe to this session." + (addedApps > 0 ? " Added " + addedApps + " built-in app" + (addedApps == 1 ? "" : "s") + " the recipe's list didn't have, unticked." : "") + " Review setup to start it.");
        }

        void ExportRecipe()
        {
            if (running) return;
            var picker = new Microsoft.Win32.SaveFileDialog { Filter = "Freshkit recipe (*.freshkit.json)|*.freshkit.json", FileName = "setup.freshkit.json", OverwritePrompt = true };
            if (picker.ShowDialog(Window) != true) return;
            try { File.WriteAllText(picker.FileName, CaptureRecipe().Serialize(), new UTF8Encoding(false)); Log("Setup recipe exported."); }
            catch (Exception ex) { Info("Couldn't export recipe", ex.Message); }
        }

        void ImportRecipe()
        {
            if (running) return;
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Freshkit recipe or winget export (*.json)|*.json", CheckFileExists = true };
            if (picker.ShowDialog(Window) != true) return;
            try
            {
                if (new FileInfo(picker.FileName).Length > 4 * 1024 * 1024) throw new InvalidDataException("The recipe exceeds 4 MB.");
                bool custom; var recipe = SetupRecipe.Parse(File.ReadAllText(picker.FileName), Engine.CurrentListText(out custom));
                PreviewRecipe(recipe);
            }
            catch (Exception ex) { Info("Couldn't import recipe", ex.Message); }
        }

        void PreviewRecipe(SetupRecipe recipe) { PreviewRecipeWith(recipe, null, null, null); }

        void PreviewRecipeWith(SetupRecipe recipe, string title, string extra, Action applied)
        {
                ValidateRecipe(recipe);
                var preview = new StringBuilder();
                if (!string.IsNullOrEmpty(extra)) preview.AppendLine(extra).AppendLine();
                var selected = new HashSet<string>(recipe.Apps, StringComparer.OrdinalIgnoreCase);
                foreach (var p in ListFile.Parse(recipe.ListText).Packages)
                {
                    string key = Data.KeyFor(p.IsGitHub ? "github" : p.IsWinget ? "winget" : "choco", p.Id);
                    if (selected.Contains(key)) preview.AppendLine(p.Name + "  ·  " + key + (string.IsNullOrEmpty(p.Parameters) ? "" : "\n  Installer arguments: " + p.Parameters));
                }
                if (recipe.HasWindowsSelections)
                {
                    preview.AppendLine("\nWindows tweaks: " + string.Join(", ", recipe.Tweaks.ToArray()));
                    preview.AppendLine("App removals: " + string.Join(", ", recipe.Remove.ToArray()));
                    preview.AppendLine("Backup selections: " + string.Join(", ", recipe.Backups.ToArray()));
                    preview.AppendLine("Restore point: " + recipe.RestorePoint + "; force Edge removal: " + recipe.ForceEdge);
                }
                var details = new TextBox { Text = preview.ToString(), IsReadOnly = true, TextWrapping = System.Windows.TextWrapping.Wrap, VerticalContentAlignment = System.Windows.VerticalAlignment.Top, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new System.Windows.Thickness(0, 10, 0, 0), Style = StyleOf("Input") };
                Confirm(title ?? "Import setup recipe?", recipe.Apps.Count + " selected app" + (recipe.Apps.Count == 1 ? ". " : "s. ") + (recipe.HasWindowsSelections ? "Replaces this session's list, app selection, Windows tweaks, removals and backup selections. Built-in apps missing from the recipe's list are added unticked." : "Imports app selections from winget's built-in sources; version pins are ignored and the current release will be used. Windows and backup selections stay as they are.") + " Review the source IDs and installer arguments below. Apply changes the selections; Review setup starts installation.", "Apply to session", delegate
                {
                    try { ApplyRecipe(recipe); if (applied != null) applied(); } catch (Exception ex) { Info("Couldn't apply recipe", ex.Message); }
                }, details);
        }
    }
}
