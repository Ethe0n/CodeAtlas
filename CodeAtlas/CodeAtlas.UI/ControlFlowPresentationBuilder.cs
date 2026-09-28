using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class ControlFlowPresentationBuilder
{
  private const int MaxOperationsPerNode = 3;

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

    var operationTexts = SplitOperationTexts(rawNode.Text);
    var conditionText = operationTexts.LastOrDefault(IsConditionOperation);
    var returnText = conditionText is null
        ? operationTexts.LastOrDefault(IsReturnOperation)
        : null;
    var bodyOperations = operationTexts
        .Where(operation => !IsConditionOperation(operation) && !IsReturnOperation(operation))
        .ToArray();
    var nodes = new List<ControlFlowPresentationNode>();
    var segmentIndex = 0;

    foreach (var operationChunk in bodyOperations.Chunk(MaxOperationsPerNode))
    {
      nodes.Add(CreateNode(
          rawNode,
          segmentIndex++,
          "Block",
          string.Join(Environment.NewLine, operationChunk)));
    }

    if (conditionText is not null)
    {
      nodes.Add(CreateNode(
          rawNode,
          segmentIndex++,
          "Condition",
          conditionText["Condition: ".Length..]));
    }

    if (returnText is not null)
    {
      nodes.Add(CreateNode(
          rawNode,
          segmentIndex++,
          "Return",
          returnText));
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

  private static string[] SplitOperationTexts(string text)
  {
    // ControlFlowAnalyzer emits one normalized line per top-level operation.
    return text.Split(
        ['\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  }

  private static bool IsConditionOperation(string text)
  {
    return text.StartsWith("Condition: ", StringComparison.Ordinal);
  }

  private static bool IsReturnOperation(string text)
  {
    return text.Equals("Return", StringComparison.Ordinal) ||
        text.StartsWith("Return ", StringComparison.Ordinal);
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
