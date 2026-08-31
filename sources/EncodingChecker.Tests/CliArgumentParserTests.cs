namespace EncodingChecker.Tests;

/// <summary>Program.TryParseArguments/TryValidateOptions/TryTakeValue — the CLI's argument parsing and validation.</summary>
public sealed class CliArgumentParserTests
{
    #region TryParseArguments

    [Fact]
    public void TryParseArguments_BasicConvertArguments_PopulatesOptions()
    {
        string[] args = ["-BasePath", @"C:\Source", "-Include", "*.cs,*.txt", "-Target", "utf-8"];

        bool result = Program.TryParseArguments(args, out Program.CliOptions options, out string? error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal(@"C:\Source", options.BasePath);
        Assert.Equal(["*.cs", "*.txt"], options.Include);
        Assert.Equal("utf-8", options.Target);
    }

    [Fact]
    public void TryParseArguments_LeadingDoubleDash_IsAcceptedTheSameAsSingleDash()
    {
        string[] args = ["--BasePath", @"C:\Source"];

        bool result = Program.TryParseArguments(args, out Program.CliOptions options, out string? error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal(@"C:\Source", options.BasePath);
    }

    [Fact]
    public void TryParseArguments_FlagNamesAreCaseInsensitive()
    {
        string[] args = ["-basepath", @"C:\Source", "-TARGET", "utf-8", "-WHATIF"];

        bool result = Program.TryParseArguments(args, out Program.CliOptions options, out string? error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal(@"C:\Source", options.BasePath);
        Assert.Equal("utf-8", options.Target);
        Assert.True(options.WhatIf);
    }

    [Theory]
    [InlineData("-DetectOnly")]
    [InlineData("-FailOnChanges")]
    [InlineData("-WhatIf")]
    [InlineData("-Backup")]
    [InlineData("-Quiet")]
    [InlineData("-Verbose")]
    public void TryParseArguments_BooleanSwitches_SetTheCorrespondingFlag(string flag)
    {
        string[] args = [flag];

        bool result = Program.TryParseArguments(args, out Program.CliOptions options, out _);

        Assert.True(result);
        bool flagValue = flag.TrimStart('-') switch
        {
            "DetectOnly" => options.DetectOnly,
            "FailOnChanges" => options.FailOnChanges,
            "WhatIf" => options.WhatIf,
            "Backup" => options.Backup,
            "Quiet" => options.Quiet,
            "Verbose" => options.Verbose,
            _ => throw new InvalidOperationException(),
        };
        Assert.True(flagValue);
    }

    [Fact]
    public void TryParseArguments_IncludeAndExclude_SplitOnCommasAndTrimWhitespace()
    {
        string[] args = ["-Include", " *.cs , *.txt ", "-Exclude", "*.designer.cs,*.g.cs"];

        bool result = Program.TryParseArguments(args, out Program.CliOptions options, out _);

        Assert.True(result);
        Assert.Equal(["*.cs", "*.txt"], options.Include);
        Assert.Equal(["*.designer.cs", "*.g.cs"], options.Exclude);
    }

    [Fact]
    public void TryParseArguments_MaxParallelism_ParsesPositiveInteger()
    {
        string[] args = ["-MaxParallelism", "4"];

        bool result = Program.TryParseArguments(args, out Program.CliOptions options, out string? error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal(4, options.MaxParallelism);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("notanumber")]
    public void TryParseArguments_MaxParallelismNotAPositiveInteger_FailsWithClearError(string value)
    {
        string[] args = ["-MaxParallelism", value];

        bool result = Program.TryParseArguments(args, out _, out string? error);

        Assert.False(result);
        Assert.Equal("-MaxParallelism requires a positive integer.", error);
    }

    [Fact]
    public void TryParseArguments_UnrecognizedFlag_FailsWithTheOffendingArgumentNamed()
    {
        string[] args = ["-BasePath", @"C:\Source", "-NotAFlag"];

        bool result = Program.TryParseArguments(args, out _, out string? error);

        Assert.False(result);
        Assert.Equal("Unrecognized argument: -NotAFlag", error);
    }

    [Theory]
    [InlineData("-BasePath")]
    [InlineData("-Include")]
    [InlineData("-Exclude")]
    [InlineData("-Target")]
    [InlineData("-Validate")]
    [InlineData("-Report")]
    public void TryParseArguments_ValueFlagAtEndOfArgs_FailsWithMissingValueError(string flag)
    {
        string[] args = [flag];

        bool result = Program.TryParseArguments(args, out _, out string? error);

        Assert.False(result);
        Assert.Equal($"{flag} requires a value.", error);
    }

    [Fact]
    public void TryParseArguments_ValueLooksLikeAnotherKnownFlag_IsTreatedAsMissingValue()
    {
        string[] args = ["-BasePath", "-Include", "*.txt"];

        bool result = Program.TryParseArguments(args, out _, out string? error);

        Assert.False(result);
        Assert.Equal("-BasePath requires a value.", error);
    }

    [Fact]
    public void TryParseArguments_ValueThatStartsWithDashButIsNotAKnownFlag_IsConsumedAsTheValue()
    {
        // A negative-number-shaped value must not be mistaken for a missing flag value.
        string[] args = ["-MaxParallelism", "-4"];

        bool result = Program.TryParseArguments(args, out _, out string? error);

        Assert.False(result);
        Assert.Equal("-MaxParallelism requires a positive integer.", error);
    }

    [Fact]
    public void TryParseArguments_EmptyArgumentArray_SucceedsWithDefaultOptions()
    {
        bool result = Program.TryParseArguments([], out Program.CliOptions options, out string? error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Null(options.BasePath);
        Assert.Null(options.Target);
        Assert.False(options.WhatIf);
    }

    #endregion

    #region TryValidateOptions

    private static Program.CliOptions ParsedOptions(params string[] args)
    {
        bool parsed = Program.TryParseArguments(args, out Program.CliOptions options, out string? error);
        Assert.True(parsed, error);
        return options;
    }

    [Theory]
    [InlineData("")]
    [InlineData(",,,")]
    [InlineData("   ")]
    [InlineData(",")]
    public void TryValidateOptions_IncludeGivenButUnusable_Fails(string pattern)
    {
        // A filter that parses to nothing must not mean "every file". This is the
        // dangerous direction: -Include "$VAR" with VAR unset would otherwise widen
        // a conversion to the whole tree instead of failing.
        Program.CliOptions options =
            ParsedOptions("-BasePath", @"C:\Source", "-Target", "utf-8", "-Include", pattern);

        Assert.True(options.IncludeSpecified);
        Assert.Empty(options.Include);

        bool valid = Program.TryValidateOptions(options, out string? error);

        Assert.False(valid);
        Assert.Contains("no usable pattern", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",,,")]
    public void TryValidateOptions_ExcludeGivenButUnusable_Fails(string pattern)
    {
        Program.CliOptions options =
            ParsedOptions("-BasePath", @"C:\Source", "-Target", "utf-8", "-Exclude", pattern);

        bool valid = Program.TryValidateOptions(options, out string? error);

        Assert.False(valid);
        Assert.Contains("no usable pattern", error);
    }

    [Fact]
    public void TryValidateOptions_IncludeOmitted_StillMeansEveryFile()
    {
        // The fix must not turn "no filter" into an error; only a supplied-but-empty
        // one. Validation also requires the base path to exist, so use a real folder
        // and the assertion cannot pass for the wrong reason.
        string root = Directory.CreateTempSubdirectory("ec-include-").FullName;

        try
        {
            Program.CliOptions options =
                ParsedOptions("-BasePath", root, "-Target", "utf-8");

            Assert.False(options.IncludeSpecified);
            Assert.Empty(options.Include);
            Assert.True(Program.TryValidateOptions(options, out string? error), error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_IncludeWithOneUsablePattern_Passes()
    {
        string root = Directory.CreateTempSubdirectory("ec-include-").FullName;

        try
        {
            Program.CliOptions options = ParsedOptions(
                "-BasePath", root, "-Target", "utf-8", "-Include", ",,*.txt,,");

            Assert.Equal(["*.txt"], options.Include);
            Assert.True(Program.TryValidateOptions(options, out string? error), error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_MissingBasePath_Fails()
    {
        Program.CliOptions options = ParsedOptions("-Target", "utf-8");

        bool result = Program.TryValidateOptions(options, out string? error);

        Assert.False(result);
        Assert.Equal("-BasePath is required.", error);
    }

    [Fact]
    public void TryValidateOptions_NonexistentBasePath_Fails()
    {
        string missingDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Program.CliOptions options = ParsedOptions("-BasePath", missingDir, "-Target", "utf-8");

        bool result = Program.TryValidateOptions(options, out string? error);

        Assert.False(result);
        Assert.Contains(missingDir, error);
    }

    [Fact]
    public void TryValidateOptions_DetectOnlyCombinedWithValidate_Fails()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Program.CliOptions options =
                ParsedOptions("-BasePath", tempDir, "-DetectOnly", "-Validate", "utf-8");

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.False(result);
            Assert.Equal("-DetectOnly cannot be combined with -Validate.", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_ValidateWithNoCharsets_Fails()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Program.CliOptions options = ParsedOptions("-BasePath", tempDir, "-Validate", "  ,  ,");

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.False(result);
            Assert.Equal("-Validate requires at least one charset.", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_ConvertModeWithoutTarget_Fails()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Program.CliOptions options = ParsedOptions("-BasePath", tempDir);

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.False(result);
            Assert.Contains("-Target is required", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_ConvertModeWithUnrecognizedTargetEncoding_Fails()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Program.CliOptions options =
                ParsedOptions("-BasePath", tempDir, "-Target", "not-a-real-encoding");

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.False(result);
            Assert.Contains("not-a-real-encoding", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("-From")]
    [InlineData("-Target")]
    public void TryValidateOptions_Utf7ConversionCodec_IsReportedAsUnsupported(string option)
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string[] args = option == "-From"
                ? ["-BasePath", tempDir, "-From", "utf-7", "-Target", "utf-8"]
                : ["-BasePath", tempDir, "-Target", "utf-7"];
            Program.CliOptions options = ParsedOptions(args);

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.False(result);
            Assert.Contains("utf-7", error);
            Assert.Contains("not supported", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_ConvertModeWithBomSuffixedTarget_ValidatesTheBaseCharsetOnly()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Program.CliOptions options = ParsedOptions("-BasePath", tempDir, "-Target", "utf-8-bom");

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.True(result);
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_DetectOnlyMode_DoesNotRequireATarget()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Program.CliOptions options = ParsedOptions("-BasePath", tempDir, "-DetectOnly");

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.True(result);
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_ValidateMode_DoesNotRequireATarget()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Program.CliOptions options = ParsedOptions("-BasePath", tempDir, "-Validate", "utf-8,utf-8-bom");

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.True(result);
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_ValidateUtf7_DoesNotConstructTheUnsupportedCodec()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Program.CliOptions options =
                ParsedOptions("-BasePath", tempDir, "-Validate", "utf-7");

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.True(result);
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryValidateOptions_ValidConvertOptions_Succeeds()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Program.CliOptions options = ParsedOptions("-BasePath", tempDir, "-Target", "utf-8");

            bool result = Program.TryValidateOptions(options, out string? error);

            Assert.True(result);
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion
}
