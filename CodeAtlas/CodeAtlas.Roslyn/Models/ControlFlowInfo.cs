namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowInfo(
    string MethodId,
    string MethodName,
    IReadOnlyList<ControlFlowNode> Nodes,
    IReadOnlyList<ControlFlowEdge> Edges);
