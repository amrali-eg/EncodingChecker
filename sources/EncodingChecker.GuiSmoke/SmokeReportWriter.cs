using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncodingChecker.GuiSmoke;

/// <summary>Writes the evidence files without leaving a partly written report.</summary>
internal static class SmokeReportWriter
{
    internal const string JsonFileName = "gui-smoke-report.json";
    internal const string MarkdownFileName = "gui-smoke-report.md";

    internal static void Write(string output, SmokeReport report)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        var errors = new List<Exception>();
        TryWrite(JsonFileName, () => JsonSerializer.Serialize(report, jsonOptions));
        TryWrite(MarkdownFileName, () => RenderMarkdown(report));
        if (errors.Count > 0)
            throw new AggregateException("Some smoke evidence could not be written.", errors);

        void TryWrite(string name, Func<string> render)
        {
            try { WriteUtf8Atomically(Path.Combine(output, name), render()); }
            catch (Exception ex) { errors.Add(new IOException($"Could not write {name}.", ex)); }
        }
    }

    internal static string RenderMarkdown(SmokeReport report)
    {
        var markdown = new StringBuilder()
            .AppendLine("# EncodingChecker automated GUI smoke test")
            .AppendLine()
            .AppendLine($"- Result: **{report.Outcome.Label()}**")
            .AppendLine($"- EC version: `{report.EcVersion}`")
            .AppendLine($"- Executable SHA-256: `{report.EcSha256}`");

        if (report.EcManagedAssemblySha256 is { Length: > 0 })
        {
            markdown
                .AppendLine($"- Managed assembly: `{report.EcManagedAssembly}`")
                .AppendLine(
                    $"- Managed assembly SHA-256: `{report.EcManagedAssemblySha256}`");
        }
        else if (report.EvidenceErrors.Count == 0)
        {
            markdown.AppendLine(
                "- Managed assembly: none; a single-file publish leaves no loose "
                + "assembly, so only the executable above is hashed");
        }

        markdown
            .AppendLine($"- Started UTC: `{report.StartedUtc}`")
            .AppendLine($"- Completed UTC: `{report.CompletedUtc}`")
            .AppendLine($"- Windows: `{report.OS}`")
            .AppendLine($"- .NET: `{report.DotNet}`")
            .AppendLine($"- Duration: `{report.DurationMilliseconds} ms`")
            .AppendLine()
            .AppendLine("| Phase | Result | Duration | Check |")
            .AppendLine("|---|---|---:|---|");

        IEnumerable<SmokePhaseResult> recorded = report.Preflight is null
            ? report.Phases
            : report.Phases.Prepend(report.Preflight);
        foreach (SmokePhaseResult phase in recorded)
        {
            markdown.AppendLine(
                $"| {phase.Id} | {phase.Outcome.Label()} | "
                + $"{phase.DurationMilliseconds} ms | "
                + $"{EscapeCell(phase.Name)} |");
        }

        foreach (SmokePhaseResult phase in recorded.Where(phase => phase.Metrics.Count > 0))
        {
            markdown.AppendLine().AppendLine($"## Phase {phase.Id} metrics");
            foreach ((string name, long value) in phase.Metrics.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                markdown.AppendLine($"- {name}: `{value}`");
        }

        if (!string.IsNullOrWhiteSpace(report.Error))
        {
            markdown.AppendLine().AppendLine("## Run-level problem")
                .AppendLine().AppendLine("```text")
                .AppendLine(report.Error)
                .AppendLine("```");
        }

        foreach (string evidenceError in report.EvidenceErrors)
            markdown.AppendLine().AppendLine("```text").AppendLine(evidenceError).AppendLine("```");

        SmokePhaseResult[] problems =
        [
            .. recorded.Where(phase => phase.Outcome != SmokeOutcome.Passed)
        ];

        if (problems.Length > 0)
        {
            markdown.AppendLine().AppendLine("## Phase problems");

            foreach (SmokePhaseResult phase in problems)
            {
                markdown.AppendLine().AppendLine($"### Phase {phase.Id} — {phase.Outcome}")
                    .AppendLine().AppendLine("```text")
                    .AppendLine(phase.Error)
                    .AppendLine("```");
                foreach (string cleanupError in phase.CleanupErrors)
                    markdown.AppendLine().AppendLine("Cleanup problem:").AppendLine("```text")
                        .AppendLine(cleanupError).AppendLine("```");
            }
        }

        return markdown.ToString();
    }

    private static void WriteUtf8Atomically(string path, string contents)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception original)
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Report write and temporary-file cleanup failed.", original, cleanup);
            }
            throw;
        }
    }

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
             .Replace("\r", " ", StringComparison.Ordinal)
             .Replace("\n", " ", StringComparison.Ordinal);
}
