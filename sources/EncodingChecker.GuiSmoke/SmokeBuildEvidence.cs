using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace EncodingChecker.GuiSmoke;

/// <summary>Captures build identity before the test; a later file change cannot erase results.</summary>
internal sealed record SmokeBuildEvidence
{
    public string Version { get; init; } = "unknown";
    public string? ExecutableSha256 { get; init; }
    public string? ManagedAssembly { get; init; }
    public string? ManagedAssemblySha256 { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    internal static SmokeBuildEvidence Capture(string app)
    {
        var errors = new List<string>();
        string? Read(string operation, Func<string?> read)
        {
            try { return read(); }
            catch (Exception ex)
            {
                errors.Add($"{operation}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        string? hash = Read("Executable SHA-256", () => Hash(app));
        string? version = Read("Executable version", () =>
            FileVersionInfo.GetVersionInfo(app).FileVersion);
        string assembly = Path.ChangeExtension(app, ".dll");
        // Opening, rather than File.Exists, distinguishes absence from access failure.
        string? assemblyHash = Read("Managed assembly SHA-256", () =>
        {
            try { return Hash(assembly); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        });

        return new SmokeBuildEvidence
        {
            Version = version ?? "unknown",
            ExecutableSha256 = hash,
            ManagedAssembly = assemblyHash is null ? null : assembly,
            ManagedAssemblySha256 = assemblyHash,
            Errors = Array.AsReadOnly(errors.ToArray()),
        };
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
