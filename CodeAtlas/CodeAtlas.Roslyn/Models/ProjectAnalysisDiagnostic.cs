namespace CodeAtlas.Roslyn.Models;

public sealed record ProjectAnalysisDiagnostic(
    ProjectAnalysisDiagnosticSeverity Severity,
    string Stage,
    string Message,
    string? ProjectFilePath);

public enum ProjectAnalysisDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
