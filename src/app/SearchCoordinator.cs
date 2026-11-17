using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Freshkit
{
    internal sealed class PartialSearchException : Exception
    {
        public readonly List<SearchResult> Results;
        public PartialSearchException(List<SearchResult> results, string message) : base(message) { Results = results; }
    }
    internal sealed class SearchBatch
    {
        public readonly List<SearchResult> Results = new List<SearchResult>();
        public readonly Dictionary<string, string> Errors = new Dictionary<string, string>();
        public int Completed;
        public bool IsFinal;
    }

    internal static class SearchIO
    {
        [ThreadStatic] internal static CancellationToken Token;
        public static CancellationTokenRegistration AbortOnCancel(WebRequest request)
        {
            Token.ThrowIfCancellationRequested();
            return Token.Register(request.Abort);
        }
        public static T Run<T>(CancellationToken token, Func<T> action)
        {
            var previous = Token; Token = token;
            try { token.ThrowIfCancellationRequested(); return action(); }
            finally { Token = previous; }
        }
    }

    internal static class SearchCoordinator
    {
        sealed class Completion { public string Source, Error; public List<SearchResult> Results; }
        public static void Run(IDictionary<string, Func<CancellationToken, List<SearchResult>>> sources, int timeoutMs, CancellationToken cancellation, Action<SearchBatch> publish)
        {
            if (cancellation.IsCancellationRequested) return;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(timeoutMs);
                var tasks = new List<Task<Completion>>(); var remaining = new HashSet<string>(sources.Keys);
                var results = new List<SearchResult>(); var errors = new Dictionary<string, string>(); int completed = 0;
                foreach (var source in sources)
                {
                    var work = source;
                    tasks.Add(Task.Factory.StartNew(delegate {
                        var item = new Completion { Source = work.Key };
                        try { item.Results = SearchIO.Run(deadline.Token, delegate { return work.Value(deadline.Token); }); }
                        catch (PartialSearchException ex) { item.Results = ex.Results; item.Error = ex.Message; }
                        catch (Exception ex) { item.Error = ex is OperationCanceledException ? "timed out" : ex.Message; }
                        return item;
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
                }
                try
                {
                    while (tasks.Count > 0 && !deadline.IsCancellationRequested)
                    {
                        int index = Task.WaitAny(tasks.ToArray(), 50);
                        if (index < 0) continue;
                        var item = tasks[index].Result; tasks.RemoveAt(index); remaining.Remove(item.Source); completed++;
                        if (item.Results != null) results.AddRange(item.Results);
                        if (item.Error != null || item.Results == null) errors[item.Source] = item.Error ?? "No response";
                        if (cancellation.IsCancellationRequested) return;
                        var batch = new SearchBatch { Completed = completed, IsFinal = tasks.Count == 0 };
                        batch.Results.AddRange(results); foreach (var pair in errors) batch.Errors[pair.Key] = pair.Value;
                        publish(batch);
                    }
                    if (cancellation.IsCancellationRequested) return;
                    if (tasks.Count > 0)
                    {
                        var batch = new SearchBatch { Completed = completed, IsFinal = true }; batch.Results.AddRange(results);
                        foreach (var pair in errors) batch.Errors[pair.Key] = pair.Value;
                        foreach (string source in remaining) batch.Errors[source] = "timed out";
                        publish(batch);
                    }
                }
                finally { deadline.Cancel(); }
            }
        }
    }
}
