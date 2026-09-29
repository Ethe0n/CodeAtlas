namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowNode(
    int Id,
    string Kind,
    string Text,
    TextSpanInfo? SourceLocation)
{
    public IReadOnlyList<string> OperationTexts { get; init; } = Array.Empty<string>();

    public string? Condition { get; init; }

    public string? ReturnText { get; init; }
}
