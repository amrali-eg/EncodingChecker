using System.Windows.Automation;

namespace EncodingChecker.GuiSmoke;

/// <summary>The result the cancellation phase can establish from EC's final status.</summary>
internal enum CancellationOutcome
{
    Stopped,
    Completed,
    OtherFinalStatus,
    TimedOut,
}

/// <summary>Evidence gathered while the driver tried to press Cancel.</summary>
internal sealed record CancellationAttempt(
    bool PressAttempted,
    Exception? UncertainPress = null);

/// <summary>A cancellation verdict and the diagnostic to use when it is not stopped.</summary>
internal sealed record CancellationDecision(CancellationOutcome Outcome, string Message);

/// <summary>Keeps cancellation classification independent from UI Automation polling.</summary>
internal static class CancellationPolicy
{
    internal static CancellationDecision Decide(
        string? finalStatus,
        CancellationAttempt attempt,
        Exception? lastRefusal)
    {
        if (finalStatus is null)
        {
            string message;
            if (attempt.PressAttempted)
                message = "A Cancel press was attempted but the run never reported a final status.";
            else if (lastRefusal is ElementNotEnabledException)
                message = "Cancel presses were refused and the run never reported a final status.";
            else
                message = "The driver could not find a Cancel button and the run never reported a final status.";

            return new CancellationDecision(
                CancellationOutcome.TimedOut,
                message + DescribePressEvidence(attempt.UncertainPress, lastRefusal));
        }

        if (finalStatus.Contains("Conversion stopped", StringComparison.Ordinal))
            return new CancellationDecision(CancellationOutcome.Stopped, string.Empty);

        if (finalStatus.Contains("Conversion complete", StringComparison.Ordinal))
        {
            return new CancellationDecision(
                CancellationOutcome.Completed,
                "Cancellation was not exercised: the run finished before it could be stopped. "
                + "EC reported: " + finalStatus
                + DescribePressEvidence(attempt.UncertainPress, lastRefusal));
        }

        return new CancellationDecision(
            CancellationOutcome.OtherFinalStatus,
            "The conversion neither stopped nor completed, so this phase proved nothing "
            + "about cancellation. EC reported: " + finalStatus
            + DescribePressEvidence(attempt.UncertainPress, lastRefusal));
    }

    private static string DescribePressEvidence(Exception? uncertainPress, Exception? lastRefusal)
    {
        if (uncertainPress is not null)
        {
            return " The Cancel press failed with an unknown outcome and was not repeated: "
                   + $"{uncertainPress.GetType().Name}: {uncertainPress.Message}";
        }

        return lastRefusal is ElementNotEnabledException
            ? " The last attempt to press Cancel was refused: "
              + $"{lastRefusal.GetType().Name}: {lastRefusal.Message}"
            : string.Empty;
    }
}
