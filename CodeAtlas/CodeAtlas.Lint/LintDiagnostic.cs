using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.Lint;

public enum LintSeverity { Info, Warning }
public enum LintCategory { AnalysisCoverage, Reliability, Security, Interop, Maintainability }
public enum LintAssessment { DetectedPattern, ManualReview }

public sealed record LintDiagnostic(
    string RuleId,
    LintSeverity Severity,
    LintCategory Category,
    LintAssessment Assessment,
    string Title,
    string Message,
    string Recommendation,
    string ProjectId,
    string ProjectName,
    string? SymbolId,
    string? ContainingTypeSymbolId,
    string? FilePath,
    TextSpanInfo? SourceLocation,
    string? HelpLink);

public sealed record LintResult(
    IReadOnlyList<LintDiagnostic> Diagnostics,
    int ProjectCount,
    int IncompleteProjectCount);
