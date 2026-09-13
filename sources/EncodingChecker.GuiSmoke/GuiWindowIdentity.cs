using System.Windows.Automation;

namespace EncodingChecker.GuiSmoke;

internal sealed class GuiWindowIdentity(int handle, int processId, int[] runtimeId)
{
    private readonly int[] _runtimeId = (int[])runtimeId.Clone();
    internal int Handle { get; } = handle;

    internal bool Matches(int handle, int process, int[] runtimeId) =>
        handle == Handle && process == processId && runtimeId.AsSpan().SequenceEqual(_runtimeId);

    internal bool Matches(AutomationElement window) =>
        Matches(window.Current.NativeWindowHandle, window.Current.ProcessId, window.GetRuntimeId());

    internal static bool IsReplacement(AutomationElement candidate, GuiWindowIdentity? previous) =>
        IsReplacement(
            previous,
            candidate.Current.NativeWindowHandle,
            candidate.Current.ProcessId,
            candidate.GetRuntimeId());

    internal static bool IsReplacement(
        GuiWindowIdentity? previous,
        int handle,
        int process,
        int[] runtimeId) =>
        previous is null || !previous.Matches(handle, process, runtimeId);

    internal static bool BelongsToProcess(uint thread, uint owner, int expectedProcess) =>
        thread != 0 && owner == (uint)expectedProcess;
}

internal sealed record GuiReview(AutomationElement Element, GuiWindowIdentity Identity);
