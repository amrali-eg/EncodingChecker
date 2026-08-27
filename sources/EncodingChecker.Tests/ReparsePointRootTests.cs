using System.Diagnostics;

namespace EncodingChecker.Tests;

/// <summary>
/// ScanEngine.ValidateOptions and Program.TryValidateOptions must both reject a
/// -BasePath that is itself a symlink/junction, rather than silently scanning through it.
/// </summary>
public sealed class ReparsePointRootTests
{
    [Fact]
    public void ReparsePointBaseDirectory_IsRejectedByEngineAndCli_NotSilentlyFollowed()
    {
        string root = Directory.CreateTempSubdirectory("ec_reparse_root_").FullName;

        try
        {
            string real = Path.Combine(root, "real");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "file.txt"), "hello");

            string junction = Path.Combine(root, "link");

            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{real}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var proc = Process.Start(psi)!;
            proc.WaitForExit(10000);

            if (proc.ExitCode != 0 || !Directory.Exists(junction))
            {
                // mklink unavailable/blocked in this environment; nothing to
                // verify, but don't fail the suite over an environment gap.
                return;
            }

            try
            {
                Assert.True(DirectoryTraversal.IsReparsePointDirectory(junction));

                var options = new ScanDirectoryOptions
                {
                    BaseDirectory = junction,
                    Action = ScanAction.Detect,
                };

                var entries = new EntrySink();

                Assert.Throws<ArgumentException>(() =>
                    ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None));

                Assert.Empty(entries);

                var cliOptions = new Program.CliOptions
                {
                    BasePath = junction,
                    Target = "utf-8",
                };

                bool valid = Program.TryValidateOptions(cliOptions, out string? error);

                Assert.False(valid);
                Assert.Contains("reparse point", error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Process.Start("cmd.exe", $"/c rmdir \"{junction}\"")?.WaitForExit(5000);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsReparsePointDirectory_RealDirectory_ReturnsFalse()
    {
        string dir = Directory.CreateTempSubdirectory("ec_reparse_check_").FullName;

        try
        {
            Assert.False(DirectoryTraversal.IsReparsePointDirectory(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
