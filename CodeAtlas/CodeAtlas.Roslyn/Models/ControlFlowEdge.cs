namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowEdge(
    int From,
    int To,
    string Kind,
    string? Condition);
