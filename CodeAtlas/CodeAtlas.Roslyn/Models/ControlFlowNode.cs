namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowNode(
    int Id,
    string Kind,
    string Text,
    TextSpanInfo? SourceLocation);
