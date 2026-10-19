using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.Lint;

public sealed class LintAnalyzer
{
    private readonly LintOptions _options;

    public LintAnalyzer(LintOptions? options = null)
    {
        _options = options ?? new LintOptions();
        if (_options.MaxConditionalBlocks <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The condition-block threshold must be positive.");
    }

    public LintResult Analyze(SolutionStructure solution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        var findings = new List<LintDiagnostic>();
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var methods = project.Types.SelectMany(type => type.Methods)
                .Where(method => !method.IsGenerated)
                .GroupBy(method => method.SymbolId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var flowIds = project.ControlFlows.Select(flow => flow.MethodId).ToHashSet(StringComparer.Ordinal);
            var missingFlows = methods.Keys.Count(id => !flowIds.Contains(id));
            if (project.AnalysisStatus != ProjectAnalysisStatus.Full || missingFlows > 0)
            {
                Add(findings, project, "CAL0001", LintSeverity.Warning, LintCategory.AnalysisCoverage,
                    LintAssessment.DetectedPattern, "Incomplete project analysis",
                    $"Project analysis status is {project.AnalysisStatus}; {missingFlows} user method(s) have no CFG. Missing findings must not be interpreted as safe code.",
                    "Review project load diagnostics and unavailable method CFGs, then analyze again. CFG-based rules cannot run without their input.",
                    null, null, project.FilePath, null);
            }

            foreach (var flow in project.ControlFlows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (methods.TryGetValue(flow.MethodId, out var method))
                    AnalyzeFlow(project, method, flow, findings, cancellationToken);
            }

            foreach (var call in project.Calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (methods.TryGetValue(call.CallerMethodSymbolId, out var method) && !call.IsProjectInternal)
                    AnalyzeCall(project, method, call, findings);
            }

            foreach (var field in project.Types.SelectMany(type => type.Fields))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!field.IsGenerated && field.SymbolId.StartsWith("F:", StringComparison.Ordinal) &&
                    field.IsShared && !field.IsConst && !field.IsReadOnly &&
                    field.Accessibility is "Public" or "Protected" or "Protected Friend")
                {
                    Add(findings, project, "CAL3001", LintSeverity.Info, LintCategory.Maintainability,
                        LintAssessment.DetectedPattern, "Externally mutable Shared field",
                        $"{field.Name} is a visible, mutable Shared field. Its value can be changed outside its declaring type.",
                        "Consider a Private field with a controlled property, or ReadOnly when reassignment is unnecessary.",
                        field.SymbolId, field.DeclaringTypeSymbolId, field.FilePath, field.Span,
                        "https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2211");
                }
            }
        }

        // Remove repeated analyzer records at the same site, not distinct source occurrences.
        var diagnostics = findings.DistinctBy(item => (
                item.ProjectId, item.RuleId, item.SymbolId, item.FilePath, item.SourceLocation, item.Message))
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.ProjectName, StringComparer.Ordinal)
            .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceLocation?.StartLine ?? 0)
            .ThenBy(item => item.SourceLocation?.StartColumn ?? 0)
            .ThenBy(item => item.RuleId, StringComparer.Ordinal)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new LintResult(diagnostics, solution.Projects.Count,
            diagnostics.Count(item => item.RuleId == "CAL0001"));
    }

    private void AnalyzeFlow(ProjectStructure project, MethodStructure method, ControlFlowInfo flow,
        List<LintDiagnostic> findings, CancellationToken cancellationToken)
    {
        foreach (var node in flow.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var operation in node.Operations)
            {
                if (operation.Role == ControlFlowOperationRole.ErrorHandlingDirective &&
                    string.Equals(string.Join(" ", operation.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
                        "On Error Resume Next", StringComparison.OrdinalIgnoreCase))
                {
                    Add(findings, project, "CAL1001", LintSeverity.Warning, LintCategory.Reliability,
                        LintAssessment.ManualReview, "On Error Resume Next",
                        "Errors may be ignored while subsequent automation operations continue. Immediate Err checks may make this intentional.",
                        "Limit error suppression to the intended COM operation, check Err immediately, and restore error handling; otherwise use Try/Catch.",
                        method.SymbolId, method.ContainingTypeSymbolId, method.FilePath, operation.SourceLocation ?? method.Span,
                        "https://learn.microsoft.com/dotnet/visual-basic/language-reference/statements/on-error-statement");
                }
            }
        }

        foreach (var region in flow.Regions.Where(region => region.Kind == "Catch" &&
            region.ExceptionType is null or "System.Exception" or "System.SystemException" or "System.Object"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = flow.Nodes.Where(node => node.Id >= region.FirstBlockId && node.Id <= region.LastBlockId)
                .Select(node => node.SourceLocation).FirstOrDefault(location => location is not null) ?? method.Span;
            Add(findings, project, "CAL1002", LintSeverity.Info, LintCategory.Reliability,
                LintAssessment.ManualReview, "Broad Catch requires review",
                $"Catch region {region.Id} handles {region.ExceptionType ?? "all exceptions"}. This check does not prove that an exception is swallowed.",
                "Catch recoverable exception types; at application boundaries, preserve logging and rethrow when recovery is not possible.",
                method.SymbolId, method.ContainingTypeSymbolId, method.FilePath, location,
                "https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1031");
        }

        var conditions = flow.Edges.Where(edge => edge.Kind is "ConditionalTrue" or "ConditionalFalse")
            .Select(edge => edge.From).Distinct().Count();
        if (conditions > _options.MaxConditionalBlocks)
        {
            Add(findings, project, "CAL3002", LintSeverity.Info, LintCategory.Maintainability,
                LintAssessment.ManualReview, "Many conditional CFG blocks",
                $"This method has {conditions} conditional BasicBlocks (threshold: {_options.MaxConditionalBlocks}). Compiler-generated conditions are included; this is not cyclomatic complexity.",
                "Review whether independent responsibilities can be extracted into smaller methods. Do not split code solely to satisfy this threshold.",
                method.SymbolId, method.ContainingTypeSymbolId, method.FilePath, method.Span);
        }
    }

    private static void AnalyzeCall(ProjectStructure project, MethodStructure method, CallRelation call,
        List<LintDiagnostic> findings)
    {
        var id = call.CalleeMethodSymbolId;
        if (IsMethod(id, "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter.Deserialize") ||
            IsMethod(id, "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter.UnsafeDeserialize") ||
            IsMethod(id, "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter.UnsafeDeserializeMethodResponse"))
        {
            AddCall("CAL2001", LintSeverity.Warning, LintCategory.Security, LintAssessment.DetectedPattern,
                "Unsafe BinaryFormatter deserialization API",
                "A BinaryFormatter deserialization call was found. Untrusted payloads can be dangerous; input provenance is not analyzed by this rule.",
                "Replace BinaryFormatter with an explicitly typed serialization format. A Binder is not a complete security fix.",
                "https://learn.microsoft.com/dotnet/standard/serialization/binaryformatter-security-guide");
        }
        else if (IsMethod(id, "System.Diagnostics.Process.Start") || IsMethod(id, "Microsoft.VisualBasic.Interaction.Shell"))
        {
            AddCall("CAL2002", LintSeverity.Info, LintCategory.Security, LintAssessment.ManualReview,
                "External process execution requires review",
                "An external-process API is called. This alone does not establish command injection; argument values and trust are not analyzed.",
                "Use a trusted executable, avoid shell command construction, validate external input, and separate arguments where the target framework supports it.",
                "https://learn.microsoft.com/dotnet/api/system.diagnostics.process.start");
        }
        else if (IsMethod(id, "System.Runtime.InteropServices.Marshal.ReleaseComObject") ||
            IsMethod(id, "System.Runtime.InteropServices.Marshal.FinalReleaseComObject"))
        {
            AddCall("CAL2003", LintSeverity.Info, LintCategory.Interop, LintAssessment.ManualReview,
                "Manual COM release requires ownership review",
                "A manual COM-release API is called. If the RCW is shared, other automation code may still depend on it.",
                "Verify exclusive ownership and that no active call or consumer still uses the wrapper. Do not add or remove releases automatically.",
                "https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.releasecomobject");
        }

        void AddCall(string rule, LintSeverity severity, LintCategory category, LintAssessment assessment,
            string title, string message, string recommendation, string help) =>
            Add(findings, project, rule, severity, category, assessment, title,
                $"{message}\nTarget: {call.CalleeMethodSymbolId}", recommendation,
                method.SymbolId, method.ContainingTypeSymbolId, call.FilePath ?? method.FilePath, call.Span, help);
    }

    private static bool IsMethod(string id, string qualifiedMethod) =>
        id == $"M:{qualifiedMethod}" || id.StartsWith($"M:{qualifiedMethod}(", StringComparison.Ordinal);

    private static void Add(List<LintDiagnostic> findings, ProjectStructure project, string rule,
        LintSeverity severity, LintCategory category, LintAssessment assessment, string title,
        string message, string recommendation, string? symbolId, string? typeId, string? file,
        TextSpanInfo? location, string? help = null) => findings.Add(new LintDiagnostic(
            rule, severity, category, assessment, title, message, recommendation,
            project.Id, project.Name, symbolId, typeId, file, location, help));
}
