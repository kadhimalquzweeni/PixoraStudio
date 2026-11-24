using System.Diagnostics;
using System.Threading;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Opt-in, thread-safe timing counters for CABAC arithmetic hot paths.
/// No Stopwatch timestamps are collected unless this profiler is supplied
/// to the decoder. Decision time is inclusive of any renormalisation it triggers.
/// </summary>
public sealed class H264CabacPerformanceProfiler
{
    private long _decisionBinCount;
    private long _decisionElapsedTimestampTicks;
    private long _bypassBinCount;
    private long _bypassElapsedTimestampTicks;
    private long _renormalizationCount;
    private long _renormalizationElapsedTimestampTicks;

    internal void RecordDecision(long elapsedTimestampTicks)
    {
        Interlocked.Increment(ref _decisionBinCount);
        Interlocked.Add(ref _decisionElapsedTimestampTicks, elapsedTimestampTicks);
    }

    internal void RecordBypass(long elapsedTimestampTicks)
    {
        Interlocked.Increment(ref _bypassBinCount);
        Interlocked.Add(ref _bypassElapsedTimestampTicks, elapsedTimestampTicks);
    }

    internal void RecordRenormalization(long elapsedTimestampTicks)
    {
        Interlocked.Increment(ref _renormalizationCount);
        Interlocked.Add(ref _renormalizationElapsedTimestampTicks, elapsedTimestampTicks);
    }

    public H264CabacPerformanceSnapshot GetSnapshot() =>
        new(
            Interlocked.Read(ref _decisionBinCount),
            Interlocked.Read(ref _decisionElapsedTimestampTicks),
            Interlocked.Read(ref _bypassBinCount),
            Interlocked.Read(ref _bypassElapsedTimestampTicks),
            Interlocked.Read(ref _renormalizationCount),
            Interlocked.Read(ref _renormalizationElapsedTimestampTicks));
}

/// <summary>
/// A consistent snapshot of CABAC profiling counters. The three elapsed
/// durations use Stopwatch's clock and are converted to TimeSpan on access.
/// DecisionElapsed includes Renormalize time; these values must not be summed.
/// </summary>
public readonly record struct H264CabacPerformanceSnapshot(
    long DecisionBinCount,
    long DecisionElapsedTimestampTicks,
    long BypassBinCount,
    long BypassElapsedTimestampTicks,
    long RenormalizationCount,
    long RenormalizationElapsedTimestampTicks)
{
    public TimeSpan DecisionElapsed =>
        TimeSpan.FromSeconds(DecisionElapsedTimestampTicks / (double)Stopwatch.Frequency);

    public TimeSpan BypassElapsed =>
        TimeSpan.FromSeconds(BypassElapsedTimestampTicks / (double)Stopwatch.Frequency);

    public TimeSpan RenormalizationElapsed =>
        TimeSpan.FromSeconds(RenormalizationElapsedTimestampTicks / (double)Stopwatch.Frequency);
}
