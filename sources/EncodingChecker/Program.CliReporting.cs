using System;
using System.Collections.Generic;
using System.Linq;

namespace EncodingChecker;

internal static partial class Program
{
    private static void PrintVerboseSummary(
        List<ConversionReportEntry> entries)
    {
        // Errors are reported unconditionally by the caller, so -Verbose adds only the
        // breakdown rather than repeating them.
        var byResult =
            entries
                .GroupBy(e => e.Result)
                .ToDictionary(g => g.Key, g => g.Count());

        Console.Out.WriteLine();
        Console.Out.WriteLine(
            $"Total: {entries.Count}  " +
            $"Unchanged: {byResult.GetValueOrDefault(ConversionRowResult.Unchanged)}  " +
            $"Skipped: {byResult.GetValueOrDefault(ConversionRowResult.Skipped)}  " +
            $"Refused: {byResult.GetValueOrDefault(ConversionRowResult.Refused)}  " +
            $"Converted: {byResult.GetValueOrDefault(ConversionRowResult.Converted)}  " +
            $"Invalid: {byResult.GetValueOrDefault(ConversionRowResult.Invalid)}  " +
            $"Error: {byResult.GetValueOrDefault(ConversionRowResult.Error)}");
    }
}
