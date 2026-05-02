using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace screenzap.lib
{
    internal static class PerfTrace
    {
        private static readonly ConcurrentDictionary<string, PerfStats> StatsByOperation = new ConcurrentDictionary<string, PerfStats>(StringComparer.Ordinal);

        internal static IDisposable Scope(string operation, Func<string>? contextFactory = null, int slowMs = 50, int summaryEvery = 25)
        {
            return new PerfScope(operation, contextFactory, slowMs, summaryEvery);
        }

        private sealed class PerfScope : IDisposable
        {
            private readonly string operation;
            private readonly Func<string>? contextFactory;
            private readonly int slowMs;
            private readonly int summaryEvery;
            private readonly Stopwatch stopwatch;
            private bool disposed;

            public PerfScope(string operation, Func<string>? contextFactory, int slowMs, int summaryEvery)
            {
                this.operation = operation;
                this.contextFactory = contextFactory;
                this.slowMs = Math.Max(0, slowMs);
                this.summaryEvery = Math.Max(1, summaryEvery);
                stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                stopwatch.Stop();
                long elapsedMs = stopwatch.ElapsedMilliseconds;

                var stats = StatsByOperation.GetOrAdd(operation, _ => new PerfStats());
                var snapshot = stats.Record(elapsedMs);

                bool shouldLog = elapsedMs >= slowMs || snapshot.Count % summaryEvery == 0;
                if (!shouldLog)
                {
                    return;
                }

                string contextSuffix = string.Empty;
                if (contextFactory != null)
                {
                    try
                    {
                        var context = contextFactory();
                        if (!string.IsNullOrWhiteSpace(context))
                        {
                            contextSuffix = $" {context}";
                        }
                    }
                    catch
                    {
                        // Never let diagnostic context generation affect the main flow.
                    }
                }

                Logger.Log($"[perf] op={operation} elapsedMs={elapsedMs} count={snapshot.Count} avgMs={snapshot.AverageMs} maxMs={snapshot.MaxMs}{contextSuffix}");
            }
        }

        private sealed class PerfStats
        {
            private long count;
            private long totalMs;
            private long maxMs;

            public Snapshot Record(long elapsedMs)
            {
                long newCount = Interlocked.Increment(ref count);
                long newTotal = Interlocked.Add(ref totalMs, elapsedMs);

                while (true)
                {
                    long observedMax = Volatile.Read(ref maxMs);
                    if (elapsedMs <= observedMax)
                    {
                        break;
                    }

                    if (Interlocked.CompareExchange(ref maxMs, elapsedMs, observedMax) == observedMax)
                    {
                        break;
                    }
                }

                long snapshotMax = Volatile.Read(ref maxMs);
                long avg = newCount > 0 ? newTotal / newCount : 0;
                return new Snapshot(newCount, avg, snapshotMax);
            }
        }

        private readonly struct Snapshot
        {
            public Snapshot(long count, long averageMs, long maxMs)
            {
                Count = count;
                AverageMs = averageMs;
                MaxMs = maxMs;
            }

            public long Count { get; }

            public long AverageMs { get; }

            public long MaxMs { get; }
        }
    }
}