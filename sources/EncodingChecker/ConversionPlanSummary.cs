using System.Collections.Generic;
using System.Linq;

namespace EncodingChecker;

/// <summary>Mutually exclusive outcomes for the files in one conversion plan.</summary>
internal sealed record ConversionPlanSummary
{
    public required int Selected { get; init; }
    public required int ReadyToConvert { get; init; }
    public required int AlreadyTarget { get; init; }
    public required int NotIdentified { get; init; }
    public required int NeedsSourceChoice { get; init; }
    public required int OtherRefusals { get; init; }

    internal static ConversionPlanSummary From(IEnumerable<PlannedFile> files)
    {
        PlannedFile[] entries = files.ToArray();

        int needsSourceChoice = entries.Count(file => file.NeedsSourceChoice);
        int refusals = entries.Count(file => file.Action == PlannedAction.Refuse);

        return new ConversionPlanSummary
        {
            Selected = entries.Length,
            ReadyToConvert = entries.Count(file => file.Action == PlannedAction.Convert),
            AlreadyTarget = entries.Count(file => file.Action == PlannedAction.Unchanged),
            NotIdentified = entries.Count(file => file.Action == PlannedAction.Skip),
            NeedsSourceChoice = needsSourceChoice,
            OtherRefusals = refusals - needsSourceChoice,
        };
    }
}
