using System.Text.Json;
using CodeAtlas.Lint;
using CodeAtlas.Roslyn;
using CodeAtlas.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

var passed = 0;
var span = new TextSpanInfo(10, 1, 10, 20);
var method = new MethodStructure("Run", "M:Example.Owner.Run", "T:Example.Owner", VbMethodKind.Sub,
    "Public", "Owner.vb", false, span);
var field = new FieldStructure("F:Example.Owner.SharedValue", "SharedValue", "System.Int32", "Public",
    true, false, false, method.ContainingTypeSymbolId, "Owner.vb", false, span, "0");
var flow = new ControlFlowInfo(method.SymbolId, method.Name,
[
    new ControlFlowNode(0, "Entry", "", null),
    new ControlFlowNode(1, "Block", "", span)
    {
        Operations = [new ControlFlowOperationInfo("None", "On Error Resume Next", true,
            ControlFlowOperationRole.ErrorHandlingDirective, span)]
    },
    new ControlFlowNode(2, "Exit", "", null)
], [new ControlFlowEdge(1, 2, "ConditionalTrue", "value > 0")])
{
    Regions = [new ControlFlowRegionInfo("catch_1", "Catch", null, 1, 1, "System.Exception")]
};
var type = new TypeStructure("Owner", "Example.Owner", method.ContainingTypeSymbolId,
    "Example", null, "Class", "Public", "System.Object", ["Owner.vb"], span,
    [field], [], [method], [], [], []);
var calls = new[]
{
    Call("M:System.Runtime.Serialization.Formatters.Binary.BinaryFormatter.Deserialize(System.IO.Stream)", 11),
    Call("M:System.Diagnostics.Process.Start(System.String)", 12),
    Call("M:System.Runtime.InteropServices.Marshal.ReleaseComObject(System.Object)", 13),
    Call("M:System.Diagnostics.Process.Start(System.String)", 14)
};
var project = new ProjectStructure("project_1", "Example", "Example.vbproj", "Visual Basic", [type], calls, [], [], [flow]);
var solution = new SolutionStructure("Example.sln", [project]);
var analyzer = new LintAnalyzer();
var before = JsonSerializer.Serialize(solution);
var result = analyzer.Analyze(solution);
Check(result.Diagnostics.Count == 7, "Expected raw findings from six rules (two process call sites)");
Check(result.Diagnostics.Count(item => item.RuleId == "CAL2002") == 2, "Distinct call sites remain distinct");
Check(result.Diagnostics.Single(item => item.RuleId == "CAL1001").Assessment == LintAssessment.ManualReview, "Resume Next is a review, not a proven vulnerability");
Check(result.Diagnostics.Single(item => item.RuleId == "CAL2001").SymbolId == method.SymbolId, "Call findings use the caller SymbolId");
Check(before == JsonSerializer.Serialize(solution), "Linter does not mutate analysis models");
Check(JsonSerializer.Serialize(result) == JsonSerializer.Serialize(analyzer.Analyze(solution)), "Repeated runs are deterministic");

var duplicated = analyzer.Analyze(solution with { Projects = [project with { Calls = calls.Concat([calls[0]]).ToArray() }] });
Check(duplicated.Diagnostics.Count == result.Diagnostics.Count, "Duplicate analyzer records do not duplicate findings");
var partial = analyzer.Analyze(solution with { Projects = [project with { AnalysisStatus = ProjectAnalysisStatus.Partial }] });
Check(partial.IncompleteProjectCount == 1 && partial.Diagnostics.Any(item => item.RuleId == "CAL0001"), "Partial analysis reports coverage uncertainty");
var noFlow = analyzer.Analyze(solution with { Projects = [project with { ControlFlows = [] }] });
Check(noFlow.IncompleteProjectCount == 1, "Missing CFG input is not reported as clean coverage");
Check(!noFlow.Diagnostics.Any(item => item.RuleId is "CAL1001" or "CAL1002" or "CAL3002"), "CFG rules do not guess when CFG is missing");
var failed = analyzer.Analyze(new SolutionStructure("Failed.sln", [project with
{
    Types = [], Calls = [], ControlFlows = [], AnalysisStatus = ProjectAnalysisStatus.Failed
}]));
Check(failed.Diagnostics.Count == 1 && failed.Diagnostics[0].RuleId == "CAL0001", "Failed project produces a coverage finding");

var generatedMethod = method with { IsGenerated = true };
var generated = analyzer.Analyze(solution with { Projects = [project with
{
    Types = [type with { Methods = [generatedMethod], Fields = [field with { IsGenerated = true }] }]
}] });
Check(generated.Diagnostics.Count == 0, "Generated methods and fields are excluded");
var internalCalls = analyzer.Analyze(solution with { Projects = [project with
{
    Calls = calls.Select(call => call with { IsProjectInternal = true }).ToArray(),
    Types = [type with { Fields = [] }], ControlFlows = [flow with { Nodes = [], Regions = [] }]
}] });
Check(internalCalls.Diagnostics.Count == 0, "User-defined internal methods are not mistaken for framework APIs");
var lookalike = analyzer.Analyze(solution with { Projects = [project with
{
    Calls = [Call("M:Other.Process.Start(System.String)", 10), Call("M:System.Diagnostics.Process.StartSomething(System.String)", 11)],
    Types = [type with { Fields = [] }], ControlFlows = [flow with { Nodes = [], Regions = [] }]
}] });
Check(lookalike.Diagnostics.Count == 0, "Namespaces and method-name boundaries prevent false matches");
var textLookalike = analyzer.Analyze(solution with { Projects = [project with
{
    Calls = [], Types = [type with { Fields = [] }], ControlFlows = [flow with
    {
        Regions = [], Nodes = [new ControlFlowNode(1, "Block", "On Error Resume Next", span)
        {
            Operations = [new ControlFlowOperationInfo("Literal", "On Error Resume Next", false,
                ControlFlowOperationRole.None, span)]
        }]
    }]
}] });
Check(textLookalike.Diagnostics.Count == 0, "Error-handling text in ordinary operations does not trigger Resume Next");
var readOnlyFields = analyzer.Analyze(solution with { Projects = [project with
{
    Calls = [], Types = [type with { Fields = [field with { IsReadOnly = true }, field with { IsConst = true }, field with { Accessibility = "Private" }] }],
    ControlFlows = [flow with { Nodes = [], Regions = [] }]
}] });
Check(readOnlyFields.Diagnostics.Count == 0, "ReadOnly, Const, and Private fields are not flagged");
var manyConditions = analyzer.Analyze(solution with { Projects = [project with { ControlFlows = [flow with
{
    Edges = Enumerable.Range(0, 13).SelectMany(index => new[]
    {
        new ControlFlowEdge(index, 99, "ConditionalTrue", "condition"),
        new ControlFlowEdge(index, 100, "ConditionalFalse", "condition")
    }).ToArray()
}] }] });
Check(manyConditions.Diagnostics.Single(item => item.RuleId == "CAL3002").Message.Contains("13 conditional"), "Conditions count branch source blocks, not edges");
var overloaded = method with { SymbolId = method.SymbolId + "(System.Int32)" };
var overloadResult = analyzer.Analyze(solution with { Projects = [project with
{
    Types = [type with { Methods = [method, overloaded] }],
    Calls = [calls[0] with { CallerMethodSymbolId = overloaded.SymbolId }],
    ControlFlows = [flow, flow with { MethodId = overloaded.SymbolId, Nodes = [], Regions = [] }]
}] });
Check(overloadResult.Diagnostics.Single(item => item.RuleId == "CAL2001").SymbolId == overloaded.SymbolId, "Overloads are matched by SymbolId");
var second = project with { Id = "project_2" };
var twoProjects = analyzer.Analyze(solution with { Projects = [project, second] });
Check(twoProjects.Diagnostics.Count == result.Diagnostics.Count * 2, "Same SymbolId in different projects is not merged");
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    Throws<OperationCanceledException>(() => analyzer.Analyze(solution, cancellation.Token), "Lint cancellation honored");
}
Throws<ArgumentOutOfRangeException>(() => new LintAnalyzer(new LintOptions { MaxConditionalBlocks = 0 }), "Invalid threshold rejected");

var source = """
Imports System
Public Class SemanticFixture
    Public Sub Run(payload As System.IO.Stream, rcw As Object)
        System.Diagnostics.Process.Start("lint-static-only.exe")
        System.Runtime.InteropServices.Marshal.ReleaseComObject(rcw)
        Dim formatter As New System.Runtime.Serialization.Formatters.Binary.BinaryFormatter()
        formatter.Deserialize(payload)
    End Sub
End Class
""";
var tree = VisualBasicSyntaxTree.ParseText(source);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Select(path => MetadataReference.CreateFromFile(path));
var compilation = VisualBasicCompilation.Create("LintSemanticFixture", [tree], references,
    new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
var semantic = compilation.GetSemanticModel(tree);
var extractor = new VbSyntaxStructureExtractor();
var semanticTypes = extractor.ExtractTypes(tree.GetRoot(), semantic, "SemanticFixture.vb", null);
var semanticCalls = extractor.ExtractCalls(tree.GetRoot(), semantic, compilation, "SemanticFixture.vb", null);
var semanticResult = analyzer.Analyze(new SolutionStructure("Fixture.sln", [project with
{
    Types = semanticTypes, Calls = semanticCalls, ControlFlows = []
}]));
Check(semanticResult.Diagnostics.Count(item => item.RuleId is "CAL2001" or "CAL2002" or "CAL2003") == 3,
    "All security/interop call rules match actual Roslyn-extracted VB symbols (code never executed)");

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var sample = await new VbSolutionAnalyzer().AnalyzeAsync(Path.Combine(root, "sample_project", "sample_project.sln"), trustProjects: true);
var sampleResult = analyzer.Analyze(sample);
Check(sampleResult.Diagnostics.Any(item => item.RuleId == "CAL1001" && item.SymbolId!.Contains("TestLegacyErrorFlow", StringComparison.Ordinal)),
    "Existing sample On Error Resume Next is detected");
Check(sampleResult.Diagnostics.Any(item => item.RuleId == "CAL1002"), "Existing sample broad Catch is detected");
Console.WriteLine("\nSAMPLE FINDINGS");
foreach (var diagnostic in sampleResult.Diagnostics)
    Console.WriteLine($"{diagnostic.RuleId} [{diagnostic.Severity}] {diagnostic.FilePath}:{diagnostic.SourceLocation?.StartLine} {diagnostic.Title}");
Console.WriteLine($"PASS: {passed} lint checks; sample findings: {sampleResult.Diagnostics.Count}");

CallRelation Call(string target, int line) => new(method.SymbolId, method.Name, target, target,
    false, "Framework", "Owner.vb", span with { StartLine = line, EndLine = line });
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    passed++;
    Console.WriteLine("PASS " + message);
}
void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, message); return; }
    throw new InvalidOperationException(message + " did not throw " + typeof(T).Name);
}
