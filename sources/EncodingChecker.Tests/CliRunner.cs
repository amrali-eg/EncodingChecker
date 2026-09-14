namespace EncodingChecker.Tests;

/// <summary>Redirects console output/error around <see cref="Program.RunConsoleMode"/> for tests.</summary>
internal static class CliRunner
{
    internal static int Run(params string[] args) => RunCaptured(args).Exit;

    internal static int Run(out string stderr, params string[] args)
    {
        (int exit, _, string error) = RunCaptured(args);
        stderr = error;
        return exit;
    }

    internal static (int Exit, string Output, string Error) RunCaptured(params string[] args) =>
        Redirected(() => Program.RunConsoleMode(args));

    internal static (int Exit, string Output, string Error) RunCapturedWithCancellation(
        string[] args,
        CancellationToken token = default,
        Action<ConversionReportEntry>? completed = null) =>
        Redirected(() => Program.RunConsoleMode(args, token, completed));

    private static (int Exit, string Output, string Error) Redirected(Func<int> invoke)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            return (invoke(), output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
