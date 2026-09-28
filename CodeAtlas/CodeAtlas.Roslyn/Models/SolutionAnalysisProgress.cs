namespace CodeAtlas.Roslyn.Models;

public sealed record SolutionAnalysisProgress(
    int TotalProjects,
    int CompletedProjects,
    string? CurrentProjectName,
    SolutionAnalysisStage Stage,
    int CurrentDocument = 0,
    int TotalDocuments = 0,
    string? CurrentDocumentName = null,
    ProjectAnalysisStatus? ProjectStatus = null);

public enum SolutionAnalysisStage
{
    DiscoveringProjects,
    LoadingProject,
    CompilingProject,
    AnalyzingDocuments,
    LoadingFallback,
    ProjectCompleted
}
