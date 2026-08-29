using System.Threading;

namespace EncodingChecker;

/// <summary>
/// Counts application-level automatic detection requests and conversion decisions so the
/// single-pass property can be verified rather than assumed.
/// </summary>
/// <remarks>
/// The counter belongs at the scan boundary, not inside <see cref="TextEncoding"/>:
/// direct detector calls are pure utilities, while View, conversion, and plan application
/// must be observable for accidental second passes.
/// </remarks>
internal static class DetectionCounters
{
    private static long _detections;
    private static long _classifications;

    /// <summary>Times the application requested automatic detection for a file.</summary>
    internal static long Detections => Interlocked.Read(ref _detections);

    /// <summary>Times the conversion policy was decided for an entry.</summary>
    internal static long Classifications => Interlocked.Read(ref _classifications);

    internal static void RecordDetection() => Interlocked.Increment(ref _detections);

    internal static void RecordClassification() =>
        Interlocked.Increment(ref _classifications);

    /// <summary>Resets both counters for an isolated test.</summary>
    internal static void Reset()
    {
        Interlocked.Exchange(ref _detections, 0);
        Interlocked.Exchange(ref _classifications, 0);
    }
}
