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
      var edgeKind = IsControlStructureLink(
          rawNodesById[rawEdge.To],
          isBackEdge)
              ? "ControlStructureLink"
              : rawEdge.Kind;
      edges.Add(new ControlFlowPresentationEdge(
          sourceChain[^1].Id,
          targetChain[0].Id,
          edgeKind,
          rawEdge.Condition,
          GetEdgeDisplayLabel(
              rawNodesById[rawEdge.From],
              rawNodesById[rawEdge.To],
              rawEdge.Kind,
              isBackEdge),
          isBackEdge));
    }

    SimplifySelectCases(controlFlow, nodes, edges, chains, rawNodesById);
    CollapsePassThroughNodes(nodes, edges);
    return new ControlFlowPresentation(controlFlow.MethodName, nodes, edges);
  }

  private static void SimplifySelectCases(
      ControlFlowInfo controlFlow,
      List<ControlFlowPresentationNode> nodes,
      List<ControlFlowPresentationEdge> edges,
      IReadOnlyDictionary<int, IReadOnlyList<ControlFlowPresentationNode>> chains,
      IReadOnlyDictionary<int, ControlFlowNode> rawNodesById)
  {
    var groups = controlFlow.Nodes
        .Where(node =>
            node.ControlStructure?.Kind == ControlFlowStructureKind.SelectCase &&
            !string.IsNullOrWhiteSpace(node.ControlStructure.GroupId))
        .GroupBy(node => node.ControlStructure!.GroupId!, StringComparer.Ordinal)
        .OrderByDescending(group => GetSelectSpanLength(group.Key));

    foreach (var group in groups)
    {
      var testBlockIds = group
          .Where(node => node.ControlStructure!.Role == ControlFlowStructureRole.BranchTest)
          .Select(node => node.Id)
          .ToHashSet();
      if (testBlockIds.Count == 0)
      {
        continue;
      }

      var removedNodeIds = nodes
          .Where(node => testBlockIds.Contains(node.SourceBlockId))
          .Select(node => node.Id)
          .ToHashSet(StringComparer.Ordinal);
      var incomingEdges = edges
          .Where(edge => removedNodeIds.Contains(edge.To) && !removedNodeIds.Contains(edge.From))
          .ToArray();
      var selectorStructure = group.First().ControlStructure!;
      var selectorNode = new ControlFlowPresentationNode(
          $"{group.Key}_presentation",
          testBlockIds.Min(),
          SegmentIndex: 0,
          Kind: "SelectCase",
          Text: selectorStructure.Text);

      nodes.RemoveAll(node => removedNodeIds.Contains(node.Id));
      edges.RemoveAll(edge =>
          removedNodeIds.Contains(edge.From) ||
          removedNodeIds.Contains(edge.To));
      nodes.Add(selectorNode);

      foreach (var incomingEdge in incomingEdges)
      {
        AddEdgeIfMissing(edges, incomingEdge with { To = selectorNode.Id });
      }

      var caseIndex = 0;
      var addedBranches = new HashSet<string>(StringComparer.Ordinal);
      foreach (var rawEdge in controlFlow.Edges
          .Where(edge => testBlockIds.Contains(edge.From) && !testBlockIds.Contains(edge.To)))
      {
        if (!chains.TryGetValue(rawEdge.To, out var targetChain) ||
            targetChain.Count == 0 ||
            !rawNodesById.TryGetValue(rawEdge.From, out var sourceNode) ||
            !rawNodesById.TryGetValue(rawEdge.To, out var targetNode))
        {
          continue;
        }

        var targetStructure = targetNode.ControlStructure;
        var branchCondition = targetStructure is not null &&
            targetStructure.Kind == ControlFlowStructureKind.SelectCase &&
            string.Equals(targetStructure.GroupId, group.Key, StringComparison.Ordinal) &&
            targetStructure.Role == ControlFlowStructureRole.BranchBody
                ? targetStructure.BranchCondition
                : rawEdge.Kind == "ConditionalTrue"
                    ? sourceNode.ControlStructure?.BranchCondition
                    : "No matching Case";
        branchCondition = string.IsNullOrWhiteSpace(branchCondition)
            ? targetStructure?.BranchLabel ?? sourceNode.ControlStructure?.BranchLabel ?? "Case"
            : branchCondition;

        var branchKey = $"{branchCondition}\u001f{targetChain[0].Id}";
        if (!addedBranches.Add(branchKey))
        {
          continue;
        }

        var caseNode = new ControlFlowPresentationNode(
            $"{group.Key}_case_{caseIndex++}",
            sourceNode.Id,
            SegmentIndex: 0,
            Kind: "Condition",
            Text: branchCondition);
        nodes.Add(caseNode);

        AddEdgeIfMissing(
            edges,
            new ControlFlowPresentationEdge(
                selectorNode.Id,
                caseNode.Id,
                "ControlStructureLink",
                Condition: null,
                DisplayLabel: null,
                IsBackEdge: false));
        AddEdgeIfMissing(
            edges,
            new ControlFlowPresentationEdge(
                caseNode.Id,
                targetChain[0].Id,
                "FallThrough",
                Condition: null,
                DisplayLabel: null,
                IsBackEdge: false));
      }
    }
  }

  private static int GetSelectSpanLength(string groupId)
  {
    var separatorIndex = groupId.LastIndexOf('_');
    return separatorIndex >= 0 && int.TryParse(groupId[(separatorIndex + 1)..], out var length)
        ? length
        : 0;
  }

  private static bool IsControlStructureLink(
      ControlFlowNode target,
      bool isBackEdge)
  {
    return isBackEdge &&
        target.ControlStructure?.Kind == ControlFlowStructureKind.ForEach;
  }

  private static void AddEdgeIfMissing(
      List<ControlFlowPresentationEdge> edges,
      ControlFlowPresentationEdge edge)
  {
    if (!edges.Contains(edge))
    {
      edges.Add(edge);
    }
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
        if (incoming.Length == 0 ||
            outgoing.Length != 1 ||
            incoming.Any(edge => edge.From == node.Id))
        {
          continue;
        }

        var outgoingEdge = outgoing[0];
        var targetNode = nodes.FirstOrDefault(candidate => candidate.Id == outgoingEdge.To);
        var isExitMerge = targetNode?.Kind == "Exit" && outgoingEdge.Kind == "Return";
        if (outgoingEdge.To == node.Id ||
            (outgoingEdge.Kind != "FallThrough" && !isExitMerge))
        {
          continue;
        }

        var replacements = incoming
            .Select(incomingEdge => CombinePassThroughEdges(incomingEdge, outgoingEdge))
            .ToArray();

        nodes.Remove(node);
        foreach (var incomingEdge in incoming)
        {
          edges.Remove(incomingEdge);
        }

        edges.Remove(outgoingEdge);
        foreach (var replacement in replacements)
        {
          AddEdgeIfMissing(edges, replacement);
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

  private static ControlFlowPresentationEdge CombinePassThroughEdges(
      ControlFlowPresentationEdge incomingEdge,
      ControlFlowPresentationEdge outgoingEdge)
  {
    var semanticEdge = incomingEdge.Kind != "FallThrough"
        ? incomingEdge
        : outgoingEdge;

    return new ControlFlowPresentationEdge(
        incomingEdge.From,
        outgoingEdge.To,
        semanticEdge.Kind,
        semanticEdge.Condition,
        incomingEdge.DisplayLabel ?? outgoingEdge.DisplayLabel,
        incomingEdge.IsBackEdge || outgoingEdge.IsBackEdge);
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
