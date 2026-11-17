// Freshkit - guided package options with reviewable source-specific advanced arguments.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Freshkit
{
    internal sealed partial class MainUi
    {
        void OpenAppOptions(AppItem item)
        {
            if (running || item == null) return;
            BuildAppOptionsWindow(item).ShowDialog();
        }
        Window BuildAppOptionsWindow(AppItem item)
        {
            string originalId = item.Id, targetId = item.Id;
            bool updating = false;
            var editor = new Window { Title = "Installation options · " + item.Name, Width = 720, Height = 690, MinWidth = 540, MinHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Frozen("#13161C"), Foreground = Frozen("#E8EAED"), FontSize = 13, FontFamily = Window.FontFamily };
            if (Window != null && (Window.IsVisible || new System.Windows.Interop.WindowInteropHelper(Window).Handle != IntPtr.Zero)) editor.Owner = Window;
            var dock = new DockPanel { Margin = new Thickness(22) };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
            var cancel = new Button { Content = "Cancel", Style = StyleOf("Secondary"), IsCancel = true, Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += delegate { editor.Close(); }; footer.Children.Add(cancel);
            var save = new Button { Content = "Save for this session", Style = StyleOf("Primary"), Name = "SaveAppOptions", Padding = new Thickness(18, 8, 18, 8), IsDefault = true }; footer.Children.Add(save);
            var panel = new StackPanel(); dock.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            panel.Children.Add(new TextBlock { Text = item.Name, FontSize = 22, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = item.SourceLabel + " · " + originalId, Margin = new Thickness(0, 6, 0, 16), TextWrapping = TextWrapping.Wrap });
            var input = new TextBox { Name = "AppArguments", Style = StyleOf("Input"), Text = item.Parameters ?? "", AcceptsReturn = false, AcceptsTab = false, MinHeight = 40, MaxLength = 2048, Padding = new Thickness(8), TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !item.IsStore };
            var languageChoices = AppOptions.Languages(item.Source, originalId);
            var components = AppOptions.Components(item.Source, originalId);
            ComboBox language = null;
            var checks = new List<CheckBox>();
            if (languageChoices.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = "Installation language", FontWeight = FontWeights.SemiBold });
                language = new ComboBox { Name = "AppLanguage", Style = (Style)Window.FindResource(typeof(ComboBox)), MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 10) };
                foreach (var choice in languageChoices) language.Items.Add(choice);
                panel.Children.Add(language);
                if (item.IsWinget) panel.Children.Add(new TextBlock { Text = "Firefox languages use separate winget packages. The exact selected package is shown below. This changes the next installation request; an existing installation may keep its language.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            }
            if (components.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = "Include these components", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 8) });
                foreach (var component in components)
                {
                    var check = new CheckBox { Name = component.ControlName, Content = component.Name, Tag = component.Flag, IsChecked = !AppOptions.HasFlag(input.Text, component.Flag), Style = StyleOf("Switch"), Margin = new Thickness(0, 0, 0, 8) };
                    checks.Add(check); panel.Children.Add(check);
                }
                panel.Children.Add(new TextBlock { Text = "Checked uses the package's normal component behavior; uncheck to request omission. Existing installations may retain components. Other custom arguments stay in the advanced field.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            }
            panel.Children.Add(new TextBlock { Text = "Advanced · " + AppOptions.Label(item.Source), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            panel.Children.Add(new TextBlock { Text = AppOptions.Help(item.Source, originalId), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
            panel.Children.Add(input);
            var defaults = new Button { Name = "ResetAppDefaults", Content = "Reset defaults", Style = StyleOf("Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(defaults);
            panel.Children.Add(new TextBlock { Text = "Will be saved", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
            var preview = new TextBlock { Name = "AppOptionsPreview", TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), Padding = new Thickness(10), Background = Frozen("#1C212A") }; panel.Children.Add(preview);
            panel.Children.Add(new TextBlock { Text = "Session only. Saved choices appear in setup review, exported recipes and job snapshots.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            var validation = new TextBlock { Name = "AppArgumentsValidation", TextWrapping = TextWrapping.Wrap, Foreground = Frozen("#FBBF24"), Margin = new Thickness(0, 10, 0, 0) }; panel.Children.Add(validation);
            Action refresh = delegate
            {
                updating = true;
                if (language != null)
                {
                    string current = AppOptions.CurrentLanguage(item.Source, item.IsWinget ? targetId : originalId, input.Text);
                    AppLanguage selected = null;
                    foreach (AppLanguage choice in language.Items) if (choice.Code == current) selected = choice;
                    if (selected == null) { selected = new AppLanguage { Code = "__custom", Name = "Keep custom language from advanced arguments" }; language.Items.Add(selected); }
                    language.SelectedItem = selected;
                }
                foreach (var check in checks) check.IsChecked = !AppOptions.HasFlag(input.Text, (string)check.Tag);
                string error = AppOptions.Validate(item.Source, targetId, input.Text);
                validation.Text = error ?? ""; save.IsEnabled = error == null && !running;
                preview.Text = item.SourceLabel + ": " + targetId + "\nArguments: " + (string.IsNullOrWhiteSpace(input.Text) ? "(package defaults)" : input.Text);
                updating = false;
            };
            Action applyGuided = delegate
            {
                if (updating) return;
                string locale = language == null ? "" : ((AppLanguage)language.SelectedItem).Code;
                if (item.IsWinget && language != null) targetId = AppOptions.LanguagePackage(originalId, locale);
                var omitted = new List<string>(); foreach (var check in checks) if (check.IsChecked != true) omitted.Add((string)check.Tag);
                input.Text = AppOptions.GuidedArguments(item.Source, originalId, input.Text, locale, omitted.ToArray());
                refresh();
            };
            if (language != null) language.SelectionChanged += delegate { applyGuided(); };
            foreach (var check in checks) { check.Checked += delegate { applyGuided(); }; check.Unchecked += delegate { applyGuided(); }; }
            input.TextChanged += delegate { refresh(); };
            defaults.Click += delegate { targetId = originalId; input.Text = ""; refresh(); };
            refresh();
            save.Click += delegate
            {
                if (running) return;
                refresh(); if (!save.IsEnabled) return;
                try
                {
                    ListFile.SetPackageOptions(item.Source, originalId, targetId, input.Text);
                    string oldKey = item.Key;
                    item.Id = targetId;
                    if (!string.Equals(oldKey, item.Key, StringComparison.OrdinalIgnoreCase)) { picks.Remove(oldKey); picks[item.Key] = item.IsSelected; }
                    LoadList(false); MarkCustom(); editor.Close(); Log("Updated installation options for " + item.Name + ".");
                }
                catch (Exception ex) { validation.Text = ex.Message; }
            };
            editor.Content = dock; editor.Loaded += delegate { if (language != null) language.Focus(); else { input.Focus(); input.SelectAll(); } };
            return editor;
        }
    }
}
