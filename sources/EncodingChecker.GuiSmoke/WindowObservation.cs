namespace EncodingChecker.GuiSmoke;

internal sealed record WindowObservation(
    GuiReachability Reachability,
    bool? ProcessAlive,
    int NativeHandle,
    bool? NativeWindowExists,
    bool ProcessWindowFound,
    bool? ReviewPresent,
    bool? StatusBarFound,
    bool? StatusBarReadable,
    bool? Offscreen,
    string? Status,
    IReadOnlyList<string> Errors,
    bool? OnCurrentDesktop = null)
{
    // Diagnosis is secondary: it must not replace the timeout we are trying to explain.
    internal static WindowObservation Capture(Func<WindowObservation> observe)
    {
        try { return observe(); }
        catch (Exception ex)
        {
            return new WindowObservation(GuiReachability.Unknown, null, 0, null, false,
                null, null, null, null, null,
                Array.AsReadOnly(new[] { "Window observation failed: " + ex }));
        }
    }

    internal string Describe()
    {
        string status = string.IsNullOrWhiteSpace(Status)
            ? "none"
            : Status.Replace("\r", " ", StringComparison.Ordinal)
                    .Replace("\n", " | ", StringComparison.Ordinal);
        string errors = Errors.Count == 0
            ? "none"
            : string.Join("; ", Errors);

        return " Window observation: "
            + $"{Reachability}; process alive={Word(ProcessAlive)}; "
            + $"native handle={NativeHandle}; native window exists={Word(NativeWindowExists)}; "
            + $"process window visible={Word(ProcessWindowFound)}; "
            + $"review present={Word(ReviewPresent)}; "
            + $"status bar found={Word(StatusBarFound)}; "
            + $"status bar readable={Word(StatusBarReadable)}; "
            + $"window offscreen={Word(Offscreen)}; on current desktop={Word(OnCurrentDesktop)}; "
            + $"status={status}; errors={errors}.";
    }

    private static string Word(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        null => "unknown",
    };
}


internal sealed class GuiWaitException(
    string message,
    WindowObservation observation,
    Exception? innerException = null) : TimeoutException(message, innerException)
{
    internal WindowObservation Observation { get; } = observation;
}
