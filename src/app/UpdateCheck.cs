using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Freshkit
{
    internal sealed class PartialUpdatesException : Exception
    {
        public readonly List<UpdateItem> Items;
        public PartialUpdatesException(List<UpdateItem> items, string message) : base(message) { Items = items; }
    }
    internal sealed class UpdateCheck
    {
        public readonly List<UpdateItem> Items = new List<UpdateItem>();
        public readonly List<string> Errors = new List<string>();
        public bool Complete { get { return Errors.Count == 0; } }
        public string Status
        {
            get
            {
                if (!Complete) return "Update check incomplete. " + Items.Count + " updates found. " + string.Join("; ", Errors.ToArray()) + ". Retry to check these sources.";
                return Items.Count == 0 ? "Everything is up to date." : Items.Count + " update" + (Items.Count == 1 ? "" : "s") + " available.";
            }
        }
        public static UpdateCheck Run(IDictionary<string, Func<List<UpdateItem>>> sources)
        {
            var result = new UpdateCheck(); var tasks = new List<Task>();
            foreach (var source in sources)
            {
                var work = source;
                tasks.Add(Task.Factory.StartNew(delegate
                {
                    try { var items = work.Value(); lock (result) result.Items.AddRange(items); }
                    catch (PartialUpdatesException ex) { lock (result) { result.Items.AddRange(ex.Items); result.Errors.Add(work.Key + ": " + ex.Message); } }
                    catch (Exception ex) { lock (result) result.Errors.Add(work.Key + ": " + ex.Message); }
                }));
            }
            Task.WaitAll(tasks.ToArray());
            result.Errors.Sort(StringComparer.OrdinalIgnoreCase);
            result.Items.Sort(delegate(UpdateItem a, UpdateItem b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return result;
        }
    }
}
