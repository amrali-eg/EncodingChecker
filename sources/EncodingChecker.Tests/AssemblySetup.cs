using System.Runtime.CompilerServices;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// One-time test-assembly setup, run automatically before any test executes.
/// </summary>
internal static class AssemblySetup
{
    /// <summary>
    /// Registers legacy code-page support once for the whole test run, instead of every
    /// test class that needs a legacy encoding (windows-1252, etc.) repeating the call in
    /// its own constructor.
    /// </summary>
    [ModuleInitializer]
    internal static void RegisterCodePagesProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
