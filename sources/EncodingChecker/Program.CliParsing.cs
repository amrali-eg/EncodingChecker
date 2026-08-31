using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace EncodingChecker;

internal static partial class Program
{
    // Internal so tests can cover parsing directly.
    internal static bool TryParseArguments(
        string[] args,
        out CliOptions options,
        [NotNullWhen(false)] out string? error)
    {
        options = new CliOptions();
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i].TrimStart('-');

            switch (flag.ToLowerInvariant())
            {
                case "basepath":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.BasePath))
                    {
                        error = "-BasePath requires a value.";
                        return false;
                    }
                    break;

                case "include":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out string? include))
                    {
                        error = "-Include requires a value.";
                        return false;
                    }

                    // Repeated options accumulate patterns.
                    options.IncludeSpecified = true;
                    options.Include.AddRange(SplitCommaList(include));
                    break;

                case "exclude":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out string? exclude))
                    {
                        error = "-Exclude requires a value.";
                        return false;
                    }

                    // Repeated options accumulate patterns.
                    options.ExcludeSpecified = true;
                    options.Exclude.AddRange(SplitCommaList(exclude));
                    break;

                case "target":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.Target))
                    {
                        error = "-Target requires a value.";
                        return false;
                    }
                    break;

                case "from":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.From))
                    {
                        error = "-From requires a value.";
                        return false;
                    }
                    break;

                case "plan":
                    if (!TryTakeValue(args, ref i, out options.PlanPath))
                    {
                        error = "-Plan requires a value.";
                        return false;
                    }
                    break;

                case "journal":
                    if (!TryTakeValue(args, ref i, out options.JournalPath))
                    {
                        error = "-Journal requires a value.";
                        return false;
                    }
                    break;

                case "apply":
                    if (!TryTakeValue(args, ref i, out options.ApplyPath))
                    {
                        error = "-Apply requires a value.";
                        return false;
                    }
                    break;

                case "validate":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.ValidateCharsets))
                    {
                        error = "-Validate requires a value.";
                        return false;
                    }
                    break;

                case "detectonly":
                    options.DetectOnly = true;
                    break;

                case "report":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out options.ReportPath))
                    {
                        error = "-Report requires a value.";
                        return false;
                    }
                    break;

                case "maxparallelism":
                    if (!TryTakeValue(
                            args,
                            ref i,
                            out string? maxParallelismText) ||
                        !int.TryParse(
                            maxParallelismText,
                            out int maxParallelism) ||
                        maxParallelism <= 0)
                    {
                        error =
                            "-MaxParallelism requires a positive integer.";
                        return false;
                    }

                    options.MaxParallelism = maxParallelism;
                    break;

                case "failonchanges":
                    options.FailOnChanges = true;
                    break;

                case "whatif":
                    options.WhatIf = true;
                    break;

                case "backup":
                    options.Backup = true;
                    break;

                case "quiet":
                    options.Quiet = true;
                    break;

                case "verbose":
                    options.Verbose = true;
                    break;

                default:
                    error = $"Unrecognized argument: {args[i]}";
                    return false;
            }
        }

        return true;
    }

    // Lets TryTakeValue distinguish a missing value from a following option.
    private static readonly HashSet<string> KnownFlagNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "basepath", "include", "exclude", "target", "from", "plan", "apply",
            "journal",
            "validate",
            "detectonly", "report", "maxparallelism", "failonchanges",
            "whatif", "backup", "quiet", "verbose",
        };

    // Internal so tests can cover parsing directly.
    internal static bool TryTakeValue(
        string[] args,
        ref int i,
        [NotNullWhen(true)] out string? value)
    {
        if (i + 1 >= args.Length)
        {
            value = null;
            return false;
        }

        string candidate = args[i + 1];

        if (candidate.StartsWith('-') &&
            KnownFlagNames.Contains(candidate.TrimStart('-')))
        {
            value = null;
            return false;
        }

        value = args[++i];
        return true;
    }

    private static List<string> SplitCommaList(string value) =>
    [
        .. value.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries)
    ];

    // Internal so tests can cover validation directly.
    internal static bool TryValidateOptions(
        CliOptions options,
        [NotNullWhen(false)] out string? error)
    {
        // A filter that fails to parse must not widen the scan. -Include "" and
        // -Include ",,," both leave no usable pattern, which would otherwise mean
        // every file - the opposite of what the caller asked for, and dangerous
        // when the value came from an unset variable in a script.
        if (options.IncludeSpecified && options.Include.Count == 0)
        {
            error = "-Include was given but contains no usable pattern. Omit "
                    + "-Include to process every file.";
            return false;
        }

        if (options.ExcludeSpecified && options.Exclude.Count == 0)
        {
            error = "-Exclude was given but contains no usable pattern. Omit "
                    + "-Exclude to process every file.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.PlanPath) &&
            !string.IsNullOrWhiteSpace(options.ApplyPath))
        {
            error = "-Plan writes a plan and -Apply executes one; use them in "
                    + "separate runs so the plan can be reviewed in between.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.ApplyPath))
        {
            if (!File.Exists(options.ApplyPath))
            {
                error = $"The plan file '{options.ApplyPath}' does not exist.";
                return false;
            }

            string? overridden = ApplyConflict(options);

            if (overridden != null)
            {
                error = overridden == "-WhatIf"
                    ? "-WhatIf cannot be combined with -Apply. The saved plan is the "
                      + "preview; applying it performs the reviewed writes."
                    : $"{overridden} cannot be combined with -Apply. A plan already "
                      + "records its scope and conversion settings; re-run -Plan to "
                      + "change them.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.JournalPath) &&
            (options.DetectOnly || !string.IsNullOrWhiteSpace(options.ValidateCharsets)))
        {
            error = "-Journal records what a conversion did; it cannot be combined "
                    + "with -DetectOnly or -Validate. Use -Report for those.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.PlanPath) &&
            (options.DetectOnly || !string.IsNullOrWhiteSpace(options.ValidateCharsets)))
        {
            error = "-Plan previews a conversion; it cannot be combined with "
                    + "-DetectOnly or -Validate.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.From))
        {
            if (options.DetectOnly || !string.IsNullOrWhiteSpace(options.ValidateCharsets))
            {
                error = "-From applies to conversion only; it cannot be combined with "
                        + "-DetectOnly or -Validate, which report what the detector finds.";
                return false;
            }

            try
            {
                Encoding.GetEncoding(options.From!);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                error = ex is NotSupportedException
                    ? $"'{options.From}' is recognized but is not supported by this .NET runtime."
                    : $"'{options.From}' is not a recognized encoding.";
                return false;
            }
        }

        // An applied plan supplies its own scope and conversion settings.
        if (!string.IsNullOrWhiteSpace(options.ApplyPath))
        {
            error = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.BasePath))
        {
            error = "-BasePath is required.";
            return false;
        }

        if (!Directory.Exists(options.BasePath))
        {
            error =
                $"The directory '{options.BasePath}' does not exist.";
            return false;
        }

        if (DirectoryTraversal.IsReparsePointDirectory(options.BasePath))
        {
            error =
                $"'{options.BasePath}' is a symbolic link or other reparse point; " +
                "-BasePath must be a real directory.";
            return false;
        }

        if (options is
            {
                DetectOnly: true,
                ValidateCharsets: not null
            })
        {
            error =
                "-DetectOnly cannot be combined with -Validate.";
            return false;
        }

        // Validate and Convert are separate modes.
        if (options is
            {
                ValidateCharsets: not null,
                Target: not null
            })
        {
            error =
                "-Validate cannot be combined with -Target.";
            return false;
        }

        if (options.ValidateCharsets is not null &&
            SplitCommaList(options.ValidateCharsets).Count == 0)
        {
            error = "-Validate requires at least one charset.";
            return false;
        }

        bool isConvertMode =
            options is
            {
                DetectOnly: false,
                ValidateCharsets: null
            };

        if (isConvertMode &&
            string.IsNullOrWhiteSpace(options.Target))
        {
            error =
                "-Target is required (Convert is the default mode; " +
                "use -Validate or -DetectOnly for read-only modes).";
            return false;
        }

        if (isConvertMode &&
            !string.IsNullOrWhiteSpace(options.Target))
        {
            ScanEngine.ParseCharsetLabel(
                options.Target!,
                out string baseCharset,
                out _);

            try
            {
                Encoding.GetEncoding(baseCharset);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                error = ex is NotSupportedException
                    ? $"'{options.Target}' is recognized but is not supported by this .NET runtime."
                    : $"'{options.Target}' is not a recognized encoding.";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Returns the first option that an applied plan cannot honor. Journal output,
    /// parallelism, and quiet output remain valid apply-time controls.
    /// </summary>
    private static string? ApplyConflict(CliOptions options) =>
        options.BasePath != null ? "-BasePath"
        : options.Include.Count > 0 ? "-Include"
        : options.Exclude.Count > 0 ? "-Exclude"
        : options.Target != null ? "-Target"
        : options.From != null ? "-From"
        : options.Backup ? "-Backup"
        : options.WhatIf ? "-WhatIf"
        : options.DetectOnly ? "-DetectOnly"
        : options.ValidateCharsets != null ? "-Validate"
        : options.ReportPath != null ? "-Report"
        : options.FailOnChanges ? "-FailOnChanges"
        : options.Verbose ? "-Verbose"
        : null;
}
