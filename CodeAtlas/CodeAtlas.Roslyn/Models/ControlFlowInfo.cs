namespace CodeAtlas.Roslyn.Models;

public sealed record ControlFlowInfo(
    string MethodId,
    string MethodName,
    IReadOnlyList<ControlFlowNode> Nodes,
    IReadOnlyList<ControlFlowEdge> Edges)
{
    public IReadOnlyList<ControlFlowRegionInfo> Regions { get; init; } =
        Array.Empty<ControlFlowRegionInfo>();

    public IReadOnlyList<ControlFlowTransferInfo> Transfers { get; init; } =
        Array.Empty<ControlFlowTransferInfo>();
}
