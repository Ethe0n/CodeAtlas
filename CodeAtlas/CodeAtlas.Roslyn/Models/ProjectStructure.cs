namespace CodeAtlas.Roslyn.Models;

public sealed record ProjectStructure(
    string Id,
    string Name,
    string? FilePath,
    string Language,
    IReadOnlyList<TypeStructure> Types,
    IReadOnlyList<CallRelation> Calls,
    IReadOnlyList<FieldUsageRelation> FieldUsages,
    IReadOnlyList<ControlFlowInfo> ControlFlows);
