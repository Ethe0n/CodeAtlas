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
        return AnalyzeProjects(solution.Projects, cancellationToken);
    }

    public LintResult AnalyzeProject(ProjectStructure project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return AnalyzeProjects([project], cancellationToken);
    }

    private LintResult AnalyzeProjects(IReadOnlyList<ProjectStructure> projects, CancellationToken cancellationToken)
    {
        var findings = new List<LintDiagnostic>();
        foreach (var project in projects)
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
                    LintAssessment.DetectedPattern, "프로젝트 분석이 완전하지 않음",
                    $"프로젝트 분석 상태: {FormatAnalysisStatus(project.AnalysisStatus)}. 사용자 메서드 {missingFlows}개의 제어 흐름 그래프(CFG)가 없습니다. 진단 결과가 없더라도 안전한 코드라는 뜻은 아닙니다.",
                    "프로젝트 로딩 오류와 CFG가 없는 메서드를 확인한 뒤 다시 분석하세요. CFG 기반 규칙은 해당 분석 데이터가 없으면 검사할 수 없습니다.",
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
                        LintAssessment.DetectedPattern, "외부에서 변경 가능한 Shared 필드",
                        $"{field.Name}은 외부에 공개된 변경 가능한 Shared 필드입니다. 선언한 타입 밖에서도 값을 변경할 수 있습니다.",
                        "Private 필드와 변경을 제어하는 Property를 사용하거나, 재할당이 필요 없다면 ReadOnly로 선언하는 것을 검토하세요.",
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
        return new LintResult(diagnostics, projects.Count,
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
                        LintAssessment.ManualReview, "On Error Resume Next 사용 검토 필요",
                        "오류가 무시된 채 다음 자동화 작업이 계속 실행될 수 있습니다. 바로 뒤에서 Err를 확인한다면 의도적인 처리일 수 있습니다.",
                        "오류 무시 범위를 필요한 COM 작업으로 제한하고, 즉시 Err를 확인한 뒤 오류 처리 상태를 복원하세요. 그 외에는 Try/Catch 사용을 검토하세요.",
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
                LintAssessment.ManualReview, "광범위한 Catch 처리 검토 필요",
                $"Catch 영역 {region.Id}의 처리 대상: {region.ExceptionType ?? "모든 예외"}. 이 진단만으로 예외를 무시하고 있다고 단정할 수는 없습니다.",
                "복구할 수 있는 예외 타입을 구체적으로 처리하세요. 최상위 예외 처리에서는 로그를 남기고, 복구할 수 없다면 예외를 다시 던지는 방식을 검토하세요.",
                method.SymbolId, method.ContainingTypeSymbolId, method.FilePath, location,
                "https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1031");
        }

        var conditions = flow.Edges.Where(edge => edge.Kind is "ConditionalTrue" or "ConditionalFalse")
            .Select(edge => edge.From).Distinct().Count();
        if (conditions > _options.MaxConditionalBlocks)
        {
            Add(findings, project, "CAL3002", LintSeverity.Info, LintCategory.Maintainability,
                LintAssessment.ManualReview, "조건 분기 블록이 많은 메서드",
                $"이 메서드의 조건 분기 블록은 {conditions}개입니다(기준: {_options.MaxConditionalBlocks}개 초과). 컴파일러가 생성한 조건도 포함되며, 순환 복잡도 수치는 아닙니다.",
                "독립적인 역할을 더 작은 메서드로 분리할 수 있는지 검토하세요. 이 기준을 맞추기 위한 목적만으로 코드를 나누지는 마세요.",
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
                "안전하지 않은 BinaryFormatter 역직렬화 API",
                "BinaryFormatter 역직렬화 호출이 발견되었습니다. 신뢰할 수 없는 입력을 처리하면 위험할 수 있습니다. 이 규칙은 입력의 출처를 분석하지 않습니다.",
                "타입을 명시적으로 지정하는 다른 직렬화 형식으로 대체하세요. Binder만 추가하는 것으로는 보안 문제가 완전히 해결되지 않습니다.",
                "https://learn.microsoft.com/dotnet/standard/serialization/binaryformatter-security-guide");
        }
        else if (IsMethod(id, "System.Diagnostics.Process.Start") || IsMethod(id, "Microsoft.VisualBasic.Interaction.Shell"))
        {
            AddCall("CAL2002", LintSeverity.Info, LintCategory.Security, LintAssessment.ManualReview,
                "외부 프로세스 실행 검토 필요",
                "외부 프로세스를 실행하는 API 호출이 발견되었습니다. 이 사실만으로 명령어 삽입 취약점이 있다고 단정할 수는 없습니다. 인수 값과 신뢰 여부는 분석하지 않습니다.",
                "신뢰할 수 있는 실행 파일을 사용하고, 문자열 조합으로 셸 명령을 만들지 않도록 하세요. 외부 입력을 검증하고, 대상 프레임워크가 지원한다면 인수를 분리해 전달하세요.",
                "https://learn.microsoft.com/dotnet/api/system.diagnostics.process.start");
        }
        else if (IsMethod(id, "System.Runtime.InteropServices.Marshal.ReleaseComObject") ||
            IsMethod(id, "System.Runtime.InteropServices.Marshal.FinalReleaseComObject"))
        {
            AddCall("CAL2003", LintSeverity.Info, LintCategory.Interop, LintAssessment.ManualReview,
                "수동 COM 해제 시 소유권 확인 필요",
                "COM 객체를 수동으로 해제하는 API 호출이 발견되었습니다. COM 래퍼(RCW)를 공유 중이라면 다른 자동화 코드에서 아직 사용하고 있을 수 있습니다.",
                "해당 래퍼를 단독으로 소유하는지, 실행 중인 호출이나 다른 코드에서 더 이상 사용하지 않는지 확인하세요. 해제 코드를 일괄적으로 추가하거나 제거하지 마세요.",
                "https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.releasecomobject");
        }

        void AddCall(string rule, LintSeverity severity, LintCategory category, LintAssessment assessment,
            string title, string message, string recommendation, string help) =>
            Add(findings, project, rule, severity, category, assessment, title,
                $"{message}\n호출 대상: {call.CalleeMethodSymbolId}", recommendation,
                method.SymbolId, method.ContainingTypeSymbolId, call.FilePath ?? method.FilePath, call.Span, help);
    }

    private static bool IsMethod(string id, string qualifiedMethod) =>
        id == $"M:{qualifiedMethod}" || id.StartsWith($"M:{qualifiedMethod}(", StringComparison.Ordinal);

    private static string FormatAnalysisStatus(ProjectAnalysisStatus status) => status switch
    {
        ProjectAnalysisStatus.Full => "전체 분석",
        ProjectAnalysisStatus.Partial => "부분 분석",
        ProjectAnalysisStatus.Failed => "분석 실패",
        _ => status.ToString()
    };

    private static void Add(List<LintDiagnostic> findings, ProjectStructure project, string rule,
        LintSeverity severity, LintCategory category, LintAssessment assessment, string title,
        string message, string recommendation, string? symbolId, string? typeId, string? file,
        TextSpanInfo? location, string? help = null) => findings.Add(new LintDiagnostic(
            rule, severity, category, assessment, title, message, recommendation,
            project.Id, project.Name, symbolId, typeId, file, location, help));
}
