namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowStructureInfo(
    ControlFlowStructureKind Kind,
    string Text,
    TextSpanInfo? SourceLocation);

public enum ControlFlowStructureKind
{
    ForEach
}
