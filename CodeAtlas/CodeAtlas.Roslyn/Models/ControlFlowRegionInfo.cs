namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowRegionInfo(
    string Id,
    string Kind,
    string? ParentId,
    int FirstBlockId,
    int LastBlockId,
    string? ExceptionType)
{
    public ControlFlowRegionSourceKind SourceKind { get; init; }
}

public enum ControlFlowRegionSourceKind
{
    None,
    Using
}
