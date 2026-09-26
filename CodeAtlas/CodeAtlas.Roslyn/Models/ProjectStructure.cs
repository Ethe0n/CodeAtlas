namespace CodeAtlas.Roslyn.Models;

public sealed record ProjectStructure(
    string Id,
    string Name,
    string? FilePath,
    string Language,
    IReadOnlyList<TypeStructure> Types,
    IReadOnlyList<CallRelation> Calls,
    IReadOnlyList<FieldUsageRelation> FieldUsages,
    IReadOnlyList<TypeDependencyRelation> TypeDependencies,
    IReadOnlyList<ControlFlowInfo> ControlFlows)
{
    public ProjectAnalysisStatus AnalysisStatus { get; init; } = ProjectAnalysisStatus.Full;

    public IReadOnlyList<ProjectAnalysisDiagnostic> Diagnostics { get; init; } =
        Array.Empty<ProjectAnalysisDiagnostic>();
}
