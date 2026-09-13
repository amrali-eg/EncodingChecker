namespace EncodingChecker.GuiSmoke;

internal enum GuiReachability
{
    Reachable,
    Absent,
    EnvironmentUnavailable,
    LookupFailed,
    Unknown,
}

/// <summary>Classifies what the driver actually knows about EC's main window.</summary>
internal static class GuiReachabilityPolicy
{
    internal static GuiReachability Classify(
        bool mainWindowFound,
        bool processWindowFound,
        bool? processAlive,
        bool? nativeWindowExists,
        bool lookupFailed,
        bool? onCurrentDesktop)
    {
        if (mainWindowFound)
            return GuiReachability.Reachable;

        // A failed UIA call is not evidence that the window or desktop is absent.
        if (lookupFailed)
            return GuiReachability.LookupFailed;

        // A visible window from this process proves the desktop can still be observed.
        if (processWindowFound)
            return GuiReachability.Absent;

        if (processAlive is null || nativeWindowExists is null)
            return GuiReachability.Unknown;

        if (processAlive == false || nativeWindowExists == false)
            return GuiReachability.Absent;

        // A hung provider can also disappear from UIA. Only the shell can confirm a move.
        return onCurrentDesktop == false
            ? GuiReachability.EnvironmentUnavailable
            : GuiReachability.Unknown;
    }
}
