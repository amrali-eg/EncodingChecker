using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EncodingChecker;

internal static partial class Program
{
    private static void PrintVerboseSummary(
        List<ConversionReportEntry> entries)
    {
        foreach (ConversionReportEntry entry in entries)
        {
            if (entry.Result == ConversionRowResult.Error &&
                !string.IsNullOrEmpty(entry.Diagnostic))
            {
                Console.Error.WriteLine(
                    $"Error: {entry.FilePath}: {entry.Diagnostic}");
            }
        }

        var byResult =
            entries
                .GroupBy(e => e.Result)
                .ToDictionary(g => g.Key, g => g.Count());

        Console.Out.WriteLine();
        Console.Out.WriteLine(
            $"Total: {entries.Count}  " +
            $"Unchanged: {byResult.GetValueOrDefault(ConversionRowResult.Unchanged)}  " +
            $"Skipped: {byResult.GetValueOrDefault(ConversionRowResult.Skipped)}  " +
            $"Converted: {byResult.GetValueOrDefault(ConversionRowResult.Converted)}  " +
            $"Invalid: {byResult.GetValueOrDefault(ConversionRowResult.Invalid)}  " +
            $"Error: {byResult.GetValueOrDefault(ConversionRowResult.Error)}");
    }
}
