using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class ControlFlowPresentationBuilder
{
  private const int MaxOperationsPerNode = 3;

  public ControlFlowPresentation Build(ControlFlowInfo controlFlow)
  {
    var nodes = new List<ControlFlowPresentationNode>();
    var chains = new Dictionary<int, IReadOnlyList<ControlFlowPresentationNode>>();
    var rawNodesById = controlFlow.Nodes.ToDictionary(node => node.Id);

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
            DisplayLabel: null,
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

      var isBackEdge = rawEdge.To <= rawEdge.From;
      edges.Add(new ControlFlowPresentationEdge(
          sourceChain[^1].Id,
          targetChain[0].Id,
          rawEdge.Kind,
          rawEdge.Condition,
          GetEdgeDisplayLabel(
              rawNodesById[rawEdge.From],
              rawNodesById[rawEdge.To],
              rawEdge.Kind,
              isBackEdge),
          isBackEdge));
    }

    CollapsePassThroughNodes(nodes, edges);
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

    var legacyTexts = SplitOperationTexts(rawNode.Text);
    var operationTexts = rawNode.Operations.Count > 0
        ? rawNode.Operations
            .Where(operation => operation.Role is not (
                ControlFlowOperationRole.ForEachIterationAssignment or
                ControlFlowOperationRole.ErrorHandlingDirective))
            .Select(operation => operation.Text)
            .ToArray()
        : rawNode.OperationTexts.Count > 0
            ? rawNode.OperationTexts
        : legacyTexts.Where(operation => !IsConditionOperation(operation)).ToArray();
    var conditionText = rawNode.ControlStructure?.Kind == ControlFlowStructureKind.ForEach
        ? rawNode.ControlStructure.Text
        : rawNode.Condition ?? legacyTexts
            .LastOrDefault(IsConditionOperation)?["Condition: ".Length..];
    var returnText = rawNode.ReturnText ?? operationTexts.LastOrDefault(IsReturnOperation);
    var bodyOperations = operationTexts
        .Where(operation => !IsReturnOperation(operation))
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
          conditionText));
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

  private static string? GetEdgeDisplayLabel(
      ControlFlowNode source,
      ControlFlowNode target,
      string edgeKind,
      bool isBackEdge)
  {
    if (source.ControlStructure?.Kind == ControlFlowStructureKind.ForEach)
    {
      return edgeKind switch
      {
        "ConditionalTrue" => "Next",
        "ConditionalFalse" => "Done",
        _ => null
      };
    }

    if (isBackEdge &&
        target.ControlStructure?.Kind == ControlFlowStructureKind.ForEach &&
        edgeKind is "ConditionalTrue" or "ConditionalFalse")
    {
      return edgeKind == "ConditionalTrue"
          ? "True / Continue"
          : "False / Continue";
    }

    return null;
  }

  private static void CollapsePassThroughNodes(
      List<ControlFlowPresentationNode> nodes,
      List<ControlFlowPresentationEdge> edges)
  {
    while (true)
    {
      var collapsed = false;
      foreach (var node in nodes
          .Where(node => node.Kind == "Block" && string.IsNullOrWhiteSpace(node.Text))
          .ToArray())
      {
        var incoming = edges.Where(edge => edge.To == node.Id).ToArray();
        var outgoing = edges.Where(edge => edge.From == node.Id).ToArray();
        if (incoming.Length != 1 || outgoing.Length != 1)
        {
          continue;
        }

        var incomingEdge = incoming[0];
        var outgoingEdge = outgoing[0];
        var semanticEdge = incomingEdge.Kind != "FallThrough"
            ? incomingEdge
            : outgoingEdge;
        var replacement = new ControlFlowPresentationEdge(
            incomingEdge.From,
            outgoingEdge.To,
            semanticEdge.Kind,
            semanticEdge.Condition,
            incomingEdge.DisplayLabel ?? outgoingEdge.DisplayLabel,
            incomingEdge.IsBackEdge || outgoingEdge.IsBackEdge);

        nodes.Remove(node);
        edges.Remove(incomingEdge);
        edges.Remove(outgoingEdge);
        if (!edges.Contains(replacement))
        {
          edges.Add(replacement);
        }

        collapsed = true;
        break;
      }

      if (!collapsed)
      {
        return;
      }
    }
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
    string? DisplayLabel,
    bool IsBackEdge);
