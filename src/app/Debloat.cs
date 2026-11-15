// Freshkit - Windows tweaks backed by the embedded Win11Debloat (MIT, github.com/Raphire/Win11Debloat).
// The tweak list, labels, groups, defaults and removable apps all come from Win11Debloat's own data files.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;

namespace Freshkit
{
    public sealed class TweakToggle : Notifier
    {
        bool selected; Visibility visibility = Visibility.Visible;
        public string FeatureId { get; set; }
        public string Label { get; set; }
        public string Tooltip { get; set; }
        public Action Changed;
        public bool IsSelected { get { return selected; } set { if (selected == value) return; selected = value; Raise("IsSelected"); if (Changed != null) Changed(); } }
        public Visibility Visibility { get { return visibility; } set { visibility = value; Raise("Visibility"); } }
    }

    public sealed class TweakChoice : Notifier
    {
        int index; Visibility visibility = Visibility.Visible;
        public string GroupId { get; set; }
        public string Label { get; set; }
        public string Tooltip { get; set; }
        public List<string> Options { get; set; }          // [0] = "Don't change"
        public List<string[]> Values { get; set; }         // feature ids per option (index-1)
        public Action Changed;
        public int SelectedIndex { get { return index; } set { if (index == value) return; index = value < 0 ? 0 : value; Raise("SelectedIndex"); if (Changed != null) Changed(); } }
        public Visibility Visibility { get { return visibility; } set { visibility = value; Raise("Visibility"); } }
        public string[] SelectedFeatures { get { return index > 0 && index - 1 < Values.Count ? Values[index - 1] : new string[0]; } }
    }

    public sealed class TweakCategory : Notifier
    {
        Visibility visibility = Visibility.Visible;
        public string Name { get; set; }
        public string Glyph { get; set; }
        public ObservableCollection<TweakToggle> Toggles { get; private set; }
        public ObservableCollection<TweakChoice> Choices { get; private set; }
        public TweakCategory() { Toggles = new ObservableCollection<TweakToggle>(); Choices = new ObservableCollection<TweakChoice>(); }
        public string Header { get { return Name.ToUpperInvariant(); } }
        public string CountText
        {
            get
            {
                int n = 0;
                foreach (TweakToggle t in Toggles) if (t.IsSelected) n++;
                foreach (TweakChoice c in Choices) if (c.SelectedIndex > 0) n++;
                return n > 0 ? n + " selected" : "";
            }
        }
        public Visibility Visibility { get { return visibility; } set { visibility = value; Raise("Visibility"); } }
        public void Refresh() { Raise("CountText"); }
    }

    public sealed class RemovalApp : Notifier
    {
        bool selected; Visibility visibility = Visibility.Visible;
        public string AppId { get; set; }
        public string Name { get; set; }
        public string Tooltip { get; set; }
        public bool DefaultOn { get; set; }
        public Action Changed;
        public bool IsSelected { get { return selected; } set { if (selected == value) return; selected = value; Raise("IsSelected"); if (Changed != null) Changed(); } }
        public Visibility Visibility { get { return visibility; } set { visibility = value; Raise("Visibility"); } }
    }

    public sealed class RemovalPreset
    {
        public string Label { get; set; }
        public List<string> AppIds { get; set; }
    }

    internal sealed class Debloat
    {
        public readonly List<TweakCategory> Categories = new List<TweakCategory>();
        public readonly List<RemovalApp> Apps = new List<RemovalApp>();
        public readonly List<RemovalPreset> Presets = new List<RemovalPreset>();
        public readonly HashSet<string> RecommendedFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool RecommendRestorePoint;
        public string Version = "";

        public IEnumerable<TweakToggle> AllToggles { get { foreach (TweakCategory c in Categories) foreach (TweakToggle t in c.Toggles) yield return t; } }
        public IEnumerable<TweakChoice> AllChoices { get { foreach (TweakCategory c in Categories) foreach (TweakChoice t in c.Choices) yield return t; } }

        static string Entry(ZipArchive zip, string path)
        {
            ZipArchiveEntry e = zip.GetEntry(path);
            if (e == null) throw new InvalidDataException("Win11Debloat file missing: " + path);
            using (var r = new StreamReader(e.Open(), Encoding.UTF8)) return r.ReadToEnd().TrimStart('﻿');
        }

        static string Glyph(string icon)
        {
            // "&#xE72E;" -> character
            try
            {
                string hex = icon.Replace("&#x", "").Replace("&#X", "").TrimEnd(';');
                return ((char)int.Parse(hex, NumberStyles.HexNumber)).ToString();
            }
            catch { return ""; }
        }

        public static Debloat Load()
        {
            var d = new Debloat();
            int build = Engine.WindowsBuild();
            using (Stream s = Engine.Resource("win11debloat.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                object features = Engine.Json.DeserializeObject(Entry(zip, "Config/Features.json"));
                object langFeatures = Engine.Json.DeserializeObject(Entry(zip, "Config/Languages/en-US/Features.json"));
                object langCats = Engine.Json.DeserializeObject(Entry(zip, "Config/Languages/en-US/Categories.json"));
                object defaults = Engine.Json.DeserializeObject(Entry(zip, "Config/DefaultSettings.json"));
                object apps = Engine.Json.DeserializeObject(Entry(zip, "Config/Apps.json"));
                object langApps = Engine.Json.DeserializeObject(Entry(zip, "Config/Languages/en-US/Apps.json"));
                try
                {
                    string main = Entry(zip, "Win11Debloat.ps1");
                    var m = System.Text.RegularExpressions.Regex.Match(main, "\\$script:Version = \"([^\"]+)\"");
                    if (m.Success) d.Version = m.Groups[1].Value;
                }
                catch { }

                // categories, keyed by both id and display name
                var byKey = new Dictionary<string, TweakCategory>(StringComparer.OrdinalIgnoreCase);
                foreach (object c in J.A(features, "Categories"))
                {
                    string id = J.S(c, "CategoryId");
                    string label = J.S(J.Get(langCats, id), "Label");
                    var cat = new TweakCategory { Name = label.Length > 0 ? label : J.S(c, "Name"), Glyph = Glyph(J.S(c, "Icon")) };
                    d.Categories.Add(cat);
                    byKey[id] = cat; byKey[J.S(c, "Name")] = cat; byKey[cat.Name] = cat;
                }

                var featureInfo = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (object f in J.A(features, "Features")) featureInfo[J.S(f, "FeatureId")] = f;
                Func<string, bool> supported = delegate (string id)
                {
                    object f;
                    if (!featureInfo.TryGetValue(id, out f)) return false;
                    int min = J.I(f, "MinVersion", 0), max = J.I(f, "MaxVersion", int.MaxValue);
                    return build == 0 || (build >= min && build <= max);
                };
                object langF = J.Get(langFeatures, "Features"), langG = J.Get(langFeatures, "UiGroups");

                // grouped (pick one) settings
                var grouped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object g in J.A(features, "UiGroups"))
                {
                    string gid = J.S(g, "GroupId");
                    TweakCategory cat;
                    if (!byKey.TryGetValue(J.S(g, "Category"), out cat)) continue;
                    object lg = J.Get(langG, gid);
                    var choice = new TweakChoice { GroupId = gid, Label = J.S(lg, "Label"), Tooltip = J.S(lg, "ToolTip"), Options = new List<string> { "Don't change" }, Values = new List<string[]>() };
                    if (choice.Label.Length == 0) choice.Label = gid;
                    foreach (object v in J.A(g, "Values"))
                    {
                        var ids = new List<string>();
                        foreach (object id in J.A(v, "FeatureIds")) { ids.Add(Convert.ToString(id)); grouped.Add(Convert.ToString(id)); }
                        if (ids.Count == 0 || !supported(ids[0])) continue;
                        string label = J.S(J.Get(lg, "Values"), ids[0]);
                        choice.Options.Add(label.Length > 0 ? label : ids[0]);
                        choice.Values.Add(ids.ToArray());
                    }
                    if (choice.Tooltip.Length == 0) choice.Tooltip = choice.Label;
                    if (choice.Values.Count > 0) cat.Choices.Add(choice);
                }

                // on/off settings
                foreach (object f in J.A(features, "Features"))
                {
                    string id = J.S(f, "FeatureId");
                    string category = J.S(f, "Category");
                    TweakCategory cat;
                    if (category.Length == 0 || grouped.Contains(id) || !byKey.TryGetValue(category, out cat) || !supported(id)) continue;
                    object lf = J.Get(langF, id);
                    string label = J.S(lf, "Label");
                    var t = new TweakToggle { FeatureId = id, Label = label.Length > 0 ? label : id, Tooltip = J.S(lf, "ToolTip") };
                    if (t.Tooltip.Length == 0) t.Tooltip = t.Label;
                    cat.Toggles.Add(t);
                }
                d.Categories.RemoveAll(c => c.Toggles.Count == 0 && c.Choices.Count == 0);

                // defaults
                foreach (object s2 in J.A(defaults, "Settings"))
                {
                    if (!J.B(s2, "Value")) continue;
                    string name = J.S(s2, "Name");
                    if (name == "CreateRestorePoint") d.RecommendRestorePoint = true;
                    else if (name != "RemoveApps" && name != "Apps") d.RecommendedFeatures.Add(name);
                }

                // removable apps
                foreach (object a in J.A(apps, "Apps"))
                {
                    string id = J.S(a, "AppId");
                    object la = J.Get(langApps, id);
                    string name = J.S(la, "FriendlyName");
                    string rec = J.S(a, "Recommendation");
                    // Win11Debloat's default mode removes the apps marked SelectedByDefault.
                    var app = new RemovalApp { AppId = id, Name = (name.Length > 0 ? name : id) + (rec.Equals("unsafe", StringComparison.OrdinalIgnoreCase) ? "  (caution)" : ""), DefaultOn = J.B(a, "SelectedByDefault") };
                    app.Tooltip = (J.S(la, "Description").Length > 0 ? J.S(la, "Description") + "\n" : "") + id +
                        (rec.Length > 0 && !rec.Equals("safe", StringComparison.OrdinalIgnoreCase) ? "\nWin11Debloat recommendation: " + rec : "");
                    d.Apps.Add(app);
                }
                d.Apps.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
                foreach (object p in J.A(apps, "Presets"))
                {
                    var ids = new List<string>();
                    foreach (object id in J.A(p, "AppIds")) ids.Add(Convert.ToString(id));
                    d.Presets.Add(new RemovalPreset { Label = "+ " + J.S(p, "Name"), AppIds = ids });
                }
            }
            return d;
        }

        // ---------------------------------------------------------------- selection
        public List<string> SelectedFeatures()
        {
            var list = new List<string>();
            foreach (TweakToggle t in AllToggles) if (t.IsSelected) list.Add(t.FeatureId);
            foreach (TweakChoice c in AllChoices) list.AddRange(c.SelectedFeatures);
            return list;
        }

        public List<string> SelectedApps()
        {
            var list = new List<string>();
            foreach (RemovalApp a in Apps) if (a.IsSelected) list.Add(a.AppId);
            return list;
        }

        public void ApplyFeatures(ICollection<string> ids)
        {
            var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            foreach (TweakToggle t in AllToggles) t.IsSelected = set.Contains(t.FeatureId);
            foreach (TweakChoice c in AllChoices)
            {
                int pick = 0;
                for (int i = 0; i < c.Values.Count; i++) if (set.Contains(c.Values[i][0])) pick = i + 1;
                c.SelectedIndex = pick;
            }
        }

        public void ApplyApps(ICollection<string> ids)
        {
            var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            foreach (RemovalApp a in Apps) a.IsSelected = set.Contains(a.AppId);
        }

        /// Win11Debloat command-line arguments for the current selection (null when nothing is selected).
        public List<string> BuildArguments(bool restorePoint, bool forceRemoveEdge)
        {
            var args = new List<string>();
            foreach (string f in SelectedFeatures()) if (!args.Contains("-" + f)) args.Add("-" + f);
            List<string> apps = SelectedApps();
            if (apps.Count > 0) { args.Add("-RemoveApps"); args.Add("-Apps"); args.Add(string.Join(",", apps.ToArray())); }
            if (forceRemoveEdge) args.Add("-ForceRemoveEdge");
            if (args.Count == 0) return null;
            if (restorePoint) args.Insert(0, "-CreateRestorePoint");
            return args;
        }
    }
}
