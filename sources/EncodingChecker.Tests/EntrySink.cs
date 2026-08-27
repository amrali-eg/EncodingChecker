using System.Collections;
using System.Collections.Concurrent;

namespace EncodingChecker.Tests;

/// <summary>
/// Collects the entries a scan or conversion reports, safely.
/// </summary>
/// <remarks>
/// <see cref="ScanEngine.ScanDirectory"/> and <see cref="ScanEngine.ConvertFiles"/> invoke
/// their callback concurrently from worker threads and say so; the caller has to
/// synchronise. Both production callers use a <see cref="ConcurrentBag{T}"/>. The tests
/// passed <c>List&lt;T&gt;.Add</c>, which is not thread-safe, and lost entries — measured
/// at 3 runs in 40 over 200 files, dropping one or two each time.
/// <para>
/// That is worse in a test than in the product. A dropped entry does not throw; it
/// silently removes a file from what the test then asserts about, so the test still
/// passes and asserts less than it claims. It surfaced as a CI failure where a file
/// modified after planning was not caught as stale — because the file had never made it
/// into the plan.
/// </para>
/// <para>
/// Enumerates in path order so a test reading two entries gets them in a fixed order.
/// </para>
/// </remarks>
internal sealed class EntrySink : IEnumerable<ConversionReportEntry>
{
    private readonly ConcurrentBag<ConversionReportEntry> _entries = [];

    /// <summary>Pass this as the <c>onEntry</c> callback.</summary>
    internal void Add(ConversionReportEntry entry) => _entries.Add(entry);

    internal List<ConversionReportEntry> ToList() =>
    [
        .. _entries.OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
    ];

    internal int Count => _entries.Count;

    public IEnumerator<ConversionReportEntry> GetEnumerator() => ToList().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
