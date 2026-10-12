namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowTransferInfo(
    int From,
    int? To,
    string Slot,
    string Semantics,
    string? Condition,
    IReadOnlyList<string> LeavingRegionIds,
    IReadOnlyList<string> EnteringRegionIds,
    IReadOnlyList<string> FinallyRegionIds);
