using System.Collections;
using System.Collections.Concurrent;

namespace EncodingChecker.Tests;

/// <summary>
/// Collects concurrently reported entries safely and exposes them in a fixed order.
/// </summary>
/// <remarks>
/// <see cref="ScanEngine.ScanDirectory"/> and <see cref="ScanEngine.ConvertFiles"/> invoke
/// their callback concurrently. The collector must therefore synchronize internally;
/// tests must never rely on each test author remembering that contract.
/// <para>
/// Incident this prevents: tests previously passed <c>List&lt;T&gt;.Add</c>, which is not
/// thread-safe, and lost one or two entries in 3 of 40 runs over 200 files. A dropped
/// entry does not throw. It silently removes a file from the test's assertions, allowing
/// the test to pass while proving less than it claims. One stale-plan test failed this
/// way because its changed file had never reached the plan.
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
