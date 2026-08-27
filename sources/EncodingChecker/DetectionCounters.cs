using System.Threading;

namespace EncodingChecker;

/// <summary>
/// Counts how often EC works out an encoding, so that "it is never worked out twice" can
/// be asserted rather than argued.
/// </summary>
/// <remarks>
/// The conversion that runs must be the conversion that was approved. That holds only if
/// nothing between the approval and the write reaches its own conclusion about a file:
/// a second pass over the same bytes can answer differently, and it was the first answer
/// the user agreed to. Every surface is built to detect once — the CLI scan, the plan,
/// the GUI's confirmation — but "built to" is an architectural claim, and this project
/// has already been caught by one of those. The GUI was built to apply the ambiguity
/// refusal too.
/// <para>
/// So the property is measured. Two counts, because they are separate questions with the
/// same failure mode: <em>which encoding is this</em>, and <em>do the bytes settle it</em>.
/// </para>
/// <para>
/// Two interlocked increments on a conversion that reads the whole file are not worth
/// optimising away.
/// </para>
/// </remarks>
internal static class DetectionCounters
{
    private static long _detections;
    private static long _classifications;

    /// <summary>Times an encoding has been worked out from bytes.</summary>
    internal static long Detections => Interlocked.Read(ref _detections);

    /// <summary>Times bytes have been examined to see whether they settle the encoding.</summary>
    internal static long Classifications => Interlocked.Read(ref _classifications);

    internal static void RecordDetection() => Interlocked.Increment(ref _detections);

    internal static void RecordClassification() =>
        Interlocked.Increment(ref _classifications);

    /// <summary>Zeroes both counts. For tests measuring one operation.</summary>
    internal static void Reset()
    {
        Interlocked.Exchange(ref _detections, 0);
        Interlocked.Exchange(ref _classifications, 0);
    }
}
