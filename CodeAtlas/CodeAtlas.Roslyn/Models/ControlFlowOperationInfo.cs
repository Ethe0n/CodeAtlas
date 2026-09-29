namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowOperationInfo(
    string Kind,
    string Text,
    bool IsImplicit,
    ControlFlowOperationRole Role,
    TextSpanInfo? SourceLocation);

public enum ControlFlowOperationRole
{
    None,
    ForEachIterationAssignment
}
