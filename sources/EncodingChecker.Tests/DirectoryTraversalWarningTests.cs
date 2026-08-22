using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EncodingChecker.Tests;

/// <summary>
/// A directory-listing failure (access denied, vanished mid-scan) must be reported through
/// the onWarning callback instead of being silently swallowed, without aborting the scan.
/// </summary>
public sealed class DirectoryTraversalWarningTests
{
    [Fact]
    public void UnlistableBaseDirectory_ReportsWarning_ReturnsEmptyRatherThanThrowing()
    {
        string ghostRoot = Path.Combine(
            Path.GetTempPath(),
            "ec-tests-ghost-" + Guid.NewGuid().ToString("N"));

        List<Regex> matchAll = DirectoryTraversal.CompilePatterns(["*"], defaultToMatchAll: true);

        var warnings = new List<string>();

        // Never created, so listing it fails with DirectoryNotFoundException.
        var found = DirectoryTraversal.EnumerateFiles(
            ghostRoot,
            includeSubdirectories: true,
            matchAll,
            [],
            excludedFullPath: null,
            onWarning: warnings.Add).ToList();

        Assert.Empty(found);

        string warning = Assert.Single(warnings);
        Assert.Contains("cannot list", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ghostRoot, warning);
    }

    [Fact]
    public void AclDeniedSubdirectory_ReportsWarning_ScanContinuesPastIt()
    {
        // Regression test for a real gap: EnumerationOptions.IgnoreInaccessible
        // defaults to true, so without explicitly setting it false, an
        // access-denied subdirectory was silently dropped with no warning at
        // all, instead of hitting the catch block below that reports one.
        string root = Directory.CreateTempSubdirectory("ec_traversal_acl_").FullName;

        try
        {
            string deniedDir = Path.Combine(root, "denied");
            Directory.CreateDirectory(deniedDir);
            File.WriteAllText(Path.Combine(deniedDir, "secret.txt"), "x");
            File.WriteAllText(Path.Combine(root, "visible.txt"), "x");

            string user = $"{Environment.UserDomainName}\\{Environment.UserName}";

            var denyPsi = new ProcessStartInfo(
                "icacls",
                $"\"{deniedDir}\" /deny \"{user}:(OI)(CI)(RX)\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var denyProc = Process.Start(denyPsi)!;
            denyProc.WaitForExit(10000);

            if (denyProc.ExitCode != 0)
            {
                // icacls unavailable/blocked in this environment; nothing to
                // verify, but don't fail the suite over an environment gap.
                return;
            }

            try
            {
                List<Regex> matchAll = DirectoryTraversal.CompilePatterns(["*"], defaultToMatchAll: true);

                var warnings = new List<string>();

                var found = DirectoryTraversal.EnumerateFiles(
                    root,
                    includeSubdirectories: true,
                    matchAll,
                    [],
                    excludedFullPath: null,
                    onWarning: warnings.Add).ToList();

                if (warnings.Count == 0 && found.Count == 2)
                {
                    // The deny didn't actually take effect (e.g. running elevated
                    // as an account the DACL doesn't restrict) - environment gap,
                    // not a product defect.
                    return;
                }

                string found1 = Assert.Single(found);
                Assert.Contains("visible.txt", found1);

                string warning = Assert.Single(warnings);
                Assert.Contains("cannot list", warning, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(deniedDir, warning);
            }
            finally
            {
                Process.Start("icacls", $"\"{deniedDir}\" /reset")?.WaitForExit(5000);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AccessibleBaseDirectory_ReportsNoWarnings()
    {
        string root = Directory.CreateTempSubdirectory("ec_traversal_warning_").FullName;

        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "x");

            List<Regex> matchAll = DirectoryTraversal.CompilePatterns(["*"], defaultToMatchAll: true);

            var warnings = new List<string>();

            var found = DirectoryTraversal.EnumerateFiles(
                root,
                includeSubdirectories: true,
                matchAll,
                [],
                excludedFullPath: null,
                onWarning: warnings.Add).ToList();

            Assert.Single(found);
            Assert.Empty(warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
