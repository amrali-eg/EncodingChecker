namespace EncodingChecker.Tests;

/// <summary>The published CLI exit-code contract (see docs/CLI.md), shared across tests.</summary>
internal static class ExpectedExitCode
{
    internal const int ExpectedClean = 0;
    internal const int ExpectedUsageError = 1;
    internal const int ExpectedChangesNeeded = 2;
    internal const int ExpectedProcessingErrors = 3;
    internal const int ExpectedSafeRefusal = 5;
}
