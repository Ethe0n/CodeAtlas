namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowStructureInfo(
    ControlFlowStructureKind Kind,
    string Text,
    TextSpanInfo? SourceLocation)
{
    public string? GroupId { get; init; }

    public ControlFlowStructureRole Role { get; init; }

    public string? BranchLabel { get; init; }

    public string? BranchCondition { get; init; }
}

public enum ControlFlowStructureKind
{
    ForEach,
    SelectCase
}

public enum ControlFlowStructureRole
{
    None,
    BranchTest,
    BranchBody
}
