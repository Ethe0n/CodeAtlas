using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class ControlFlowPresentationBuilder
{
  private const int MaxStatementsPerNode = 4;

  public ControlFlowPresentation Build(ControlFlowInfo controlFlow)
  {
    var nodes = new List<ControlFlowPresentationNode>();
    var chains = new Dictionary<int, IReadOnlyList<ControlFlowPresentationNode>>();

    foreach (var rawNode in controlFlow.Nodes)
    {
      var chain = CreateNodeChain(rawNode);
      chains.Add(rawNode.Id, chain);
      nodes.AddRange(chain);
    }

    var edges = new List<ControlFlowPresentationEdge>();
    foreach (var chain in chains.Values)
    {
      for (var index = 0; index < chain.Count - 1; index++)
      {
        edges.Add(new ControlFlowPresentationEdge(
            chain[index].Id,
            chain[index + 1].Id,
            "FallThrough",
            Condition: null,
            IsBackEdge: false));
      }
    }

    foreach (var rawEdge in controlFlow.Edges)
    {
      if (!chains.TryGetValue(rawEdge.From, out var sourceChain) ||
          !chains.TryGetValue(rawEdge.To, out var targetChain))
      {
        continue;
      }

      edges.Add(new ControlFlowPresentationEdge(
          sourceChain[^1].Id,
          targetChain[0].Id,
          rawEdge.Kind,
          rawEdge.Condition,
          rawEdge.To <= rawEdge.From));
    }

    return new ControlFlowPresentation(controlFlow.MethodName, nodes, edges);
  }

  private static IReadOnlyList<ControlFlowPresentationNode> CreateNodeChain(ControlFlowNode rawNode)
  {
    if (rawNode.Kind is "Entry" or "Exit")
    {
      return
      [
          CreateNode(rawNode, segmentIndex: 0, rawNode.Kind, rawNode.Kind)
      ];
    }

    var lines = SplitLines(rawNode.Text);
    var conditionLine = lines.LastOrDefault(line =>
        line.StartsWith("Condition: ", StringComparison.Ordinal));
    var statementLines = lines
        .Where(line => !line.StartsWith("Condition: ", StringComparison.Ordinal))
        .ToArray();
    var nodes = new List<ControlFlowPresentationNode>();
    var segmentIndex = 0;

    foreach (var statementChunk in statementLines.Chunk(MaxStatementsPerNode))
    {
      nodes.Add(CreateNode(
          rawNode,
          segmentIndex++,
          "Block",
          string.Join(Environment.NewLine, statementChunk)));
    }

    if (conditionLine is not null)
    {
      nodes.Add(CreateNode(
          rawNode,
          segmentIndex,
          "Condition",
          conditionLine["Condition: ".Length..]));
    }

    if (nodes.Count == 0)
    {
      nodes.Add(CreateNode(rawNode, segmentIndex: 0, "Block", string.Empty));
    }

    return nodes;
  }

  private static ControlFlowPresentationNode CreateNode(
      ControlFlowNode rawNode,
      int segmentIndex,
      string kind,
      string text)
  {
    return new ControlFlowPresentationNode(
        $"block_{rawNode.Id}_segment_{segmentIndex}",
        rawNode.Id,
        segmentIndex,
        kind,
        text);
  }

  private static string[] SplitLines(string text)
  {
    return text.Split(
        ['\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  }
}

internal sealed record ControlFlowPresentation(
    string MethodName,
    IReadOnlyList<ControlFlowPresentationNode> Nodes,
    IReadOnlyList<ControlFlowPresentationEdge> Edges);

internal sealed record ControlFlowPresentationNode(
    string Id,
    int SourceBlockId,
    int SegmentIndex,
    string Kind,
    string Text);

internal sealed record ControlFlowPresentationEdge(
    string From,
    string To,
    string Kind,
    string? Condition,
    bool IsBackEdge);
