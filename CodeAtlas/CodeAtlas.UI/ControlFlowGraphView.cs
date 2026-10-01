using CodeAtlas.Roslyn.Models;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;
using Microsoft.Msagl.Layout.Layered;

namespace CodeAtlas.UI;

public sealed class ControlFlowGraphView : UserControl
{
  private const bool ShowBlockIds = false;

  private readonly GViewer _viewer = new();
  private readonly ControlFlowPresentationBuilder _presentationBuilder = new();
  private readonly System.Windows.Forms.Label _emptyLabel = new()
  {
    Dock = DockStyle.Fill,
    Text = "No control flow available",
    TextAlign = ContentAlignment.MiddleCenter
  };

  public ControlFlowGraphView()
  {
    Dock = DockStyle.Fill;

    _viewer.Dock = DockStyle.Fill;
    _viewer.ToolBarIsVisible = true;

    Controls.Add(_viewer);
    Controls.Add(_emptyLabel);

    ClearGraph();
  }

  private static void ApplyLayoutConstraints(
    Graph graph,
    ControlFlowPresentation presentation,
    IReadOnlyList<SelectCaseFanout> selectCaseFanouts,
    IReadOnlyList<SelectCaseMerge> selectCaseMerges)
  {
    foreach (var edge in presentation.Edges)
    {
      if (IsBackEdge(edge) ||
          edge.Kind == "ControlStructureLink" ||
          IsSelectCaseMergeEdge(edge, selectCaseMerges))
      {
        continue;
      }

      var source = graph.FindNode(edge.From);
      var target = graph.FindNode(edge.To);

      if (source is null || target is null)
      {
        continue;
      }

      graph.LayerConstraints.AddUpDownConstraint(source, target);
    }

    foreach (var fanout in selectCaseFanouts)
    {
      var selector = graph.FindNode(fanout.SelectorId);
      var junction = graph.FindNode(fanout.JunctionId);
      if (selector is not null && junction is not null)
      {
        graph.LayerConstraints.AddUpDownVerticalConstraint(selector, junction);
      }

      var busPoints = fanout.BusPointIds
          .Select(graph.FindNode)
          .Where(node => node is not null)
          .Select(node => node!)
          .ToArray();
      if (busPoints.Length > 1)
      {
        graph.LayerConstraints.PinNodesToSameLayer(busPoints);
        for (var index = 0; index < busPoints.Length - 1; index++)
        {
          graph.LayerConstraints.AddLeftRightConstraint(
              busPoints[index],
              busPoints[index + 1]);
        }
      }

      var branchTargets = fanout.Branches
          .Select(branch => graph.FindNode(branch.TargetId))
          .Where(node => node is not null)
          .Select(node => node!)
          .ToArray();
      if (branchTargets.Length > 1)
      {
        graph.LayerConstraints.PinNodesToSameLayer(branchTargets);
        for (var index = 0; index < branchTargets.Length - 1; index++)
        {
          graph.LayerConstraints.AddLeftRightConstraint(
              branchTargets[index],
              branchTargets[index + 1]);
        }
      }

      foreach (var branch in fanout.Branches)
      {
        var tap = graph.FindNode(branch.TapId);
        var target = graph.FindNode(branch.TargetId);
        if (tap is not null && target is not null)
        {
          graph.LayerConstraints.AddUpDownVerticalConstraint(tap, target);
        }
      }
    }

    foreach (var merge in selectCaseMerges)
    {
      foreach (var lane in merge.Lanes)
      {
        var localBusPoints = lane.LocalBusPointIds
            .Select(graph.FindNode)
            .Where(node => node is not null)
            .Select(node => node!)
            .ToArray();
        if (localBusPoints.Length > 1)
        {
          graph.LayerConstraints.PinNodesToSameLayer(localBusPoints);
          for (var index = 0; index < localBusPoints.Length - 1; index++)
          {
            graph.LayerConstraints.AddLeftRightConstraint(
                localBusPoints[index],
                localBusPoints[index + 1]);
          }
        }

        foreach (var input in lane.Inputs)
        {
          var source = graph.FindNode(input.Edge.From);
          var tap = graph.FindNode(input.TapId);
          if (source is not null && tap is not null)
          {
            graph.LayerConstraints.AddUpDownVerticalConstraint(source, tap);
          }
        }

        var caseNode = graph.FindNode(lane.CaseNodeId);
        var laneOutput = graph.FindNode(lane.OutputId);
        var globalTap = graph.FindNode(lane.GlobalTapId);
        var firstSuccessors = presentation.Edges
            .Where(edge =>
                edge.From == lane.CaseNodeId &&
                !edge.IsBackEdge &&
                !(edge.To == merge.TargetId &&
                    lane.Inputs.Any(input => input.Edge.From == lane.CaseNodeId)))
            .Select(edge => graph.FindNode(edge.To))
            .Where(node => node is not null)
            .Select(node => node!)
            .Distinct()
            .ToArray();
        if (caseNode is not null && firstSuccessors.Length == 1)
        {
          graph.LayerConstraints.AddUpDownVerticalConstraint(caseNode, firstSuccessors[0]);
        }

        if (caseNode is not null && globalTap is not null)
        {
          graph.LayerConstraints.AddUpDownVerticalConstraint(caseNode, globalTap);
        }

        if (laneOutput is not null && globalTap is not null)
        {
          graph.LayerConstraints.AddUpDownVerticalConstraint(laneOutput, globalTap);
        }
      }

      var busPoints = merge.GlobalBusPointIds
          .Select(graph.FindNode)
          .Where(node => node is not null)
          .Select(node => node!)
          .ToArray();
      if (busPoints.Length > 1)
      {
        graph.LayerConstraints.PinNodesToSameLayer(busPoints);
        for (var index = 0; index < busPoints.Length - 1; index++)
        {
          graph.LayerConstraints.AddLeftRightConstraint(
              busPoints[index],
              busPoints[index + 1]);
        }
      }

      var junction = graph.FindNode(merge.JunctionId);
      var target = graph.FindNode(merge.TargetId);
      if (junction is not null && target is not null)
      {
        graph.LayerConstraints.AddUpDownVerticalConstraint(junction, target);
      }
    }

    var entry = presentation.Nodes
        .FirstOrDefault(n => n.Kind == "Entry");

    var exit = presentation.Nodes
        .FirstOrDefault(n => n.Kind == "Exit");

    var entryNode = entry is null
        ? null
        : graph.FindNode(entry.Id);

    var exitNode = exit is null
        ? null
        : graph.FindNode(exit.Id);

    if (entryNode is not null)
    {
      foreach (var node in presentation.Nodes)
      {
        if (node.Id == entry!.Id)
        {
          continue;
        }

        var target = graph.FindNode(node.Id);

        if (target is not null)
        {
          graph.LayerConstraints.AddUpDownConstraint(entryNode, target);
        }
      }
    }

    if (exitNode is not null)
    {
      foreach (var node in presentation.Nodes)
      {
        if (node.Id == exit!.Id)
        {
          continue;
        }

        var source = graph.FindNode(node.Id);

        if (source is not null)
        {
          graph.LayerConstraints.AddUpDownConstraint(source, exitNode);
        }
      }
    }
  }

  public void ShowGraph(ControlFlowInfo? controlFlow)
  {
    if (controlFlow is null)
    {
      ClearGraph();
      return;
    }

    var presentation = _presentationBuilder.Build(controlFlow);
    var graph = new Graph(presentation.MethodName)
    {
      Directed = true
    };
    ConfigureGraphLayout(graph);

    foreach (var node in presentation.Nodes)
    {
      var graphNode = graph.AddNode(node.Id);
      ConfigureNode(graphNode, node);
    }

    var selectCaseFanouts = CreateSelectCaseFanouts(graph, presentation);
    var selectCaseMerges = CreateSelectCaseMerges(
        graph,
        presentation,
        selectCaseFanouts);

    foreach (var edge in presentation.Edges)
    {
      if (edge.Kind == "ControlStructureLink" &&
          selectCaseFanouts.Any(fanout =>
              fanout.SelectorId == edge.From &&
              fanout.Branches.Any(branch => branch.TargetId == edge.To)))
      {
        continue;
      }


      if (IsSelectCaseMergeEdge(edge, selectCaseMerges))
      {
        continue;
      }

      var graphEdge = graph.AddEdge(
          edge.From,
          FormatEdgeLabel(edge),
          edge.To);
      ConfigureEdge(graphEdge, edge);
    }

    foreach (var fanout in selectCaseFanouts)
    {
      ConfigureSelectCaseFanoutEdges(graph, fanout);
    }

    foreach (var merge in selectCaseMerges)
    {
      ConfigureSelectCaseMergeEdges(graph, merge);
    }

    ApplyLayoutConstraints(
        graph,
        presentation,
        selectCaseFanouts,
        selectCaseMerges);

    _viewer.Graph = graph;
    _viewer.Visible = true;
    _emptyLabel.Visible = false;
    BeginInvoke(new Action(FitGraphToViewport));
  }

  public void ClearGraph()
  {
    _viewer.Graph = null;
    _viewer.Visible = false;
    _emptyLabel.Visible = true;
  }

  private void FitGraphToViewport()
  {
    if (_viewer.Graph is not null && _viewer.Visible)
    {
      _viewer.FitGraphBoundingBox();
    }
  }

  private static void ConfigureGraphLayout(Graph graph)
  {
    graph.Attr.LayerDirection = LayerDirection.TB;
    graph.Attr.NodeSeparation = 48;
    graph.Attr.LayerSeparation = 70;
    graph.Attr.MinNodeHeight = 1;
    graph.Attr.MinNodeWidth = 1;
    graph.Attr.AspectRatio = 0.8;

    graph.LayoutAlgorithmSettings = new SugiyamaLayoutSettings
    {
      NodeSeparation = 48,
      LayerSeparation = 70,
      EdgeRoutingSettings =
      {
        EdgeRoutingMode = Microsoft.Msagl.Core.Routing.EdgeRoutingMode.Rectilinear,
        ConeAngle = 25,
        Padding = 8
      }
    };
  }

  private static IReadOnlyList<SelectCaseFanout> CreateSelectCaseFanouts(
      Graph graph,
      ControlFlowPresentation presentation)
  {
    var fanouts = new List<SelectCaseFanout>();

    foreach (var selector in presentation.Nodes.Where(node =>
        node.Kind == "SelectCase" &&
        !string.IsNullOrWhiteSpace(node.ControlStructureGroupId)))
    {
      var targets = presentation.Edges
          .Where(edge => edge.From == selector.Id && edge.Kind == "ControlStructureLink")
          .Select(edge => edge.To)
          .Distinct(StringComparer.Ordinal)
          .ToArray();
      if (targets.Length < 2)
      {
        continue;
      }

      var junctionId = $"{selector.Id}_case_junction";
      var busPointIds = new List<string>();
      var branches = new List<SelectCaseFanoutBranch>();
      var middleIndex = targets.Length / 2;

      for (var index = 0; index < targets.Length; index++)
      {
        if (targets.Length % 2 == 0 && index == middleIndex)
        {
          busPointIds.Add(junctionId);
        }

        var tapId = targets.Length % 2 == 1 && index == middleIndex
            ? junctionId
            : $"{selector.Id}_case_tap_{index}";
        busPointIds.Add(tapId);
        branches.Add(new SelectCaseFanoutBranch(tapId, targets[index]));
      }

      foreach (var pointId in busPointIds.Distinct(StringComparer.Ordinal))
      {
        ConfigureInvisibleRoutingNode(graph.AddNode(pointId));
      }

      fanouts.Add(new SelectCaseFanout(
          selector.Id,
          selector.ControlStructureGroupId!,
          junctionId,
          busPointIds,
          branches));
    }

    return fanouts;
  }

  private static IReadOnlyList<SelectCaseMerge> CreateSelectCaseMerges(
      Graph graph,
      ControlFlowPresentation presentation,
      IReadOnlyList<SelectCaseFanout> fanouts)
  {
    var merges = new List<SelectCaseMerge>();
    var nodesById = presentation.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

    foreach (var fanout in fanouts)
    {
      var groupNodeIds = presentation.Nodes
          .Where(node => string.Equals(
              node.ControlStructureGroupId,
              fanout.GroupId,
              StringComparison.Ordinal))
          .Select(node => node.Id)
          .ToHashSet(StringComparer.Ordinal);
      var exitCandidates = new List<SelectCaseExitCandidate>();

      foreach (var branch in fanout.Branches)
      {
        var branchNodeIds = GetReachableWithinGroup(
            branch.TargetId,
            groupNodeIds,
            presentation.Edges);
        var exitEdges = presentation.Edges
            .Where(edge =>
                branchNodeIds.Contains(edge.From) &&
                !groupNodeIds.Contains(edge.To) &&
                !edge.IsBackEdge &&
                edge.Kind != "Return" &&
                (!nodesById.TryGetValue(edge.To, out var targetNode) || targetNode.Kind != "Exit"))
            .Distinct()
            .ToArray();
        if (exitEdges.Length > 0)
        {
          exitCandidates.Add(new SelectCaseExitCandidate(branch.TargetId, exitEdges));
        }
      }

      if (exitCandidates.Count < 2)
      {
        continue;
      }

      var commonTargetIds = exitCandidates
          .Select(candidate => candidate.Edges
              .Select(edge => edge.To)
              .Distinct(StringComparer.Ordinal))
          .Aggregate((common, targets) => common.Intersect(targets, StringComparer.Ordinal))
          .ToHashSet(StringComparer.Ordinal);
      var targetId = presentation.Nodes
          .Select(node => node.Id)
          .FirstOrDefault(commonTargetIds.Contains);
      if (targetId is null)
      {
        continue;
      }

      var activeCandidates = exitCandidates
          .Select(candidate => candidate with
          {
            Edges = candidate.Edges
                .Where(edge => edge.To == targetId)
                .ToArray()
          })
          .Where(candidate => candidate.Edges.Count > 0)
          .ToArray();
      if (activeCandidates.Length < 2)
      {
        continue;
      }

      var junctionId = $"{fanout.SelectorId}_merge_junction";
      var globalBusPointIds = new List<string>();
      var globalTapIds = new List<string>();
      var middleIndex = activeCandidates.Length / 2;

      for (var index = 0; index < activeCandidates.Length; index++)
      {
        if (activeCandidates.Length % 2 == 0 && index == middleIndex)
        {
          globalBusPointIds.Add(junctionId);
        }

        var tapId = activeCandidates.Length % 2 == 1 && index == middleIndex
            ? junctionId
            : $"{fanout.SelectorId}_merge_tap_{index}";
        globalTapIds.Add(tapId);
        globalBusPointIds.Add(tapId);
      }

      var lanes = new List<SelectCaseMergeLane>();
      for (var laneIndex = 0; laneIndex < activeCandidates.Length; laneIndex++)
      {
        var candidate = activeCandidates[laneIndex];
        var globalTapId = globalTapIds[laneIndex];
        if (candidate.Edges.Count == 1)
        {
          lanes.Add(new SelectCaseMergeLane(
              candidate.CaseNodeId,
              candidate.Edges[0].From,
              globalTapId,
              Array.Empty<string>(),
              [new SelectCaseMergeInput(candidate.Edges[0], globalTapId)]));
          continue;
        }

        var localJunctionId = $"{fanout.SelectorId}_local_merge_{laneIndex}";
        var localBusPointIds = new List<string>();
        var inputs = new List<SelectCaseMergeInput>();
        var localMiddleIndex = candidate.Edges.Count / 2;
        for (var inputIndex = 0; inputIndex < candidate.Edges.Count; inputIndex++)
        {
          if (candidate.Edges.Count % 2 == 0 && inputIndex == localMiddleIndex)
          {
            localBusPointIds.Add(localJunctionId);
          }

          var localTapId = candidate.Edges.Count % 2 == 1 && inputIndex == localMiddleIndex
              ? localJunctionId
              : $"{fanout.SelectorId}_local_merge_{laneIndex}_tap_{inputIndex}";
          localBusPointIds.Add(localTapId);
          inputs.Add(new SelectCaseMergeInput(candidate.Edges[inputIndex], localTapId));
        }

        lanes.Add(new SelectCaseMergeLane(
            candidate.CaseNodeId,
            localJunctionId,
            globalTapId,
            localBusPointIds,
            inputs));
      }

      foreach (var pointId in globalBusPointIds
          .Concat(lanes.SelectMany(lane => lane.LocalBusPointIds))
          .Distinct(StringComparer.Ordinal))
      {
        ConfigureInvisibleRoutingNode(graph.AddNode(pointId));
      }

      merges.Add(new SelectCaseMerge(
          junctionId,
          targetId,
          globalBusPointIds,
          lanes));
    }

    return merges;
  }

  private static HashSet<string> GetReachableWithinGroup(
      string startNodeId,
      IReadOnlySet<string> groupNodeIds,
      IReadOnlyList<ControlFlowPresentationEdge> edges)
  {
    var reachable = new HashSet<string>(StringComparer.Ordinal) { startNodeId };
    var pending = new Queue<string>();
    pending.Enqueue(startNodeId);

    while (pending.Count > 0)
    {
      var current = pending.Dequeue();
      foreach (var edge in edges.Where(edge =>
          edge.From == current &&
          !edge.IsBackEdge &&
          groupNodeIds.Contains(edge.To)))
      {
        if (!reachable.Add(edge.To))
        {
          continue;
        }

        pending.Enqueue(edge.To);
      }
    }

    return reachable;
  }

  private static void ConfigureInvisibleRoutingNode(Node node)
  {
    node.LabelText = string.Empty;
    node.Attr.Shape = Shape.Point;
    node.Attr.Padding = 0;
    node.Attr.LabelMargin = 0;
    node.Attr.LineWidth = 0;
    node.Attr.Color = new Microsoft.Msagl.Drawing.Color(0, 0, 0, 0);
    node.Attr.FillColor = new Microsoft.Msagl.Drawing.Color(0, 0, 0, 0);
    if (node.Label is not null)
    {
      node.Label.FontSize = 1;
    }
  }

  private static void ConfigureSelectCaseFanoutEdges(
      Graph graph,
      SelectCaseFanout fanout)
  {
    ConfigureRoutingEdge(graph.AddEdge(fanout.SelectorId, fanout.JunctionId), false);

    for (var index = 0; index < fanout.BusPointIds.Count - 1; index++)
    {
      ConfigureRoutingEdge(graph.AddEdge(
          fanout.BusPointIds[index],
          fanout.BusPointIds[index + 1]), false);
    }

    foreach (var branch in fanout.Branches)
    {
      ConfigureRoutingEdge(graph.AddEdge(branch.TapId, branch.TargetId), true);
    }
  }

  private static void ConfigureSelectCaseMergeEdges(
      Graph graph,
      SelectCaseMerge merge)
  {
    foreach (var lane in merge.Lanes)
    {
      foreach (var input in lane.Inputs)
      {
        var inputEdge = graph.AddEdge(
            input.Edge.From,
            FormatEdgeLabel(input.Edge),
            input.TapId);
        if (input.Edge.Kind is "ConditionalTrue" or "ConditionalFalse" or "Conditional")
        {
          ConfigureEdge(inputEdge, input.Edge);
        }
        else
        {
          ConfigureMergeRoutingEdge(inputEdge, false);
        }
      }

      for (var index = 0; index < lane.LocalBusPointIds.Count - 1; index++)
      {
        ConfigureMergeRoutingEdge(graph.AddEdge(
            lane.LocalBusPointIds[index],
            lane.LocalBusPointIds[index + 1]), false);
      }

      if (lane.LocalBusPointIds.Count > 0)
      {
        ConfigureMergeRoutingEdge(graph.AddEdge(
            lane.OutputId,
            lane.GlobalTapId), false);
      }
    }

    for (var index = 0; index < merge.GlobalBusPointIds.Count - 1; index++)
    {
      ConfigureMergeRoutingEdge(graph.AddEdge(
          merge.GlobalBusPointIds[index],
          merge.GlobalBusPointIds[index + 1]), false);
    }

    ConfigureMergeRoutingEdge(graph.AddEdge(merge.JunctionId, merge.TargetId), true);
  }

  private static void ConfigureRoutingEdge(Edge edge, bool showArrow)
  {
    edge.Attr.ArrowheadAtTarget = showArrow
        ? ArrowStyle.Normal
        : ArrowStyle.None;
    edge.Attr.LineWidth = 1.4;
    edge.Attr.Color = GetEdgeColor("ControlStructureLink");
  }

  private static void ConfigureMergeRoutingEdge(Edge edge, bool showArrow)
  {
    edge.Attr.ArrowheadAtTarget = showArrow
        ? ArrowStyle.Normal
        : ArrowStyle.None;
    edge.Attr.LineWidth = 1;
    edge.Attr.Color = Microsoft.Msagl.Drawing.Color.DimGray;
  }

  private static bool IsSelectCaseMergeEdge(
      ControlFlowPresentationEdge edge,
      IReadOnlyList<SelectCaseMerge> merges)
  {
    return merges.Any(merge =>
        edge.To == merge.TargetId &&
        merge.Lanes.Any(lane =>
            lane.Inputs.Any(input => input.Edge.From == edge.From)));
  }

  private static void ConfigureNode(Node graphNode, ControlFlowPresentationNode node)
  {
    graphNode.LabelText = FormatNodeLabel(node);
    graphNode.Attr.Shape = node.Kind switch
    {
      "Entry" or "Exit" => Shape.Ellipse,
      _ when IsConditionNode(node) => Shape.Diamond,
      _ => Shape.Box
    };
    graphNode.Attr.Padding = 12;
    graphNode.Attr.LineWidth = node.Kind is "Entry" or "Exit" ? 2 : 1;
    graphNode.Attr.Color = node.Kind switch
    {
      "Entry" => Microsoft.Msagl.Drawing.Color.ForestGreen,
      "Exit" => Microsoft.Msagl.Drawing.Color.DimGray,
      _ when IsConditionNode(node) => Microsoft.Msagl.Drawing.Color.DarkOrange,
      _ => Microsoft.Msagl.Drawing.Color.SlateGray
    };
    graphNode.Attr.FillColor = node.Kind switch
    {
      "Entry" => new Microsoft.Msagl.Drawing.Color(224, 247, 232),
      "Exit" => new Microsoft.Msagl.Drawing.Color(238, 238, 238),
      _ when IsConditionNode(node) => new Microsoft.Msagl.Drawing.Color(255, 247, 224),
      _ => Microsoft.Msagl.Drawing.Color.White
    };
  }

  private static void ConfigureEdge(Edge graphEdge, ControlFlowPresentationEdge edge)
  {
    var edgeColor = GetEdgeColor(edge.Kind);

    graphEdge.Attr.ArrowheadAtTarget = ArrowStyle.Normal;
    graphEdge.Attr.LineWidth = edge.Kind is "ConditionalTrue" or "ConditionalFalse" or "ControlStructureLink" ? 1.4 : 1;
    graphEdge.Attr.Color = edgeColor;

    if (graphEdge.Label is not null &&
        edge.Kind is "ConditionalTrue" or "ConditionalFalse" or "ControlStructureLink")
    {
      graphEdge.Label.FontColor = edgeColor;
    }

    if (IsBackEdge(edge))
    {
      graphEdge.Attr.LineWidth = 1.6;
      graphEdge.Attr.AddStyle(Style.Dashed);
    }
  }

  private static Microsoft.Msagl.Drawing.Color GetEdgeColor(string edgeKind)
  {
    return edgeKind switch
    {
      "ConditionalTrue" => Microsoft.Msagl.Drawing.Color.ForestGreen,
      "ConditionalFalse" => Microsoft.Msagl.Drawing.Color.Firebrick,
      "ControlStructureLink" => new Microsoft.Msagl.Drawing.Color(184, 134, 11),
      _ => Microsoft.Msagl.Drawing.Color.DimGray
    };
  }

  private static string FormatNodeLabel(ControlFlowPresentationNode node)
  {
    if (node.Kind is "Entry" or "Exit")
    {
      return ShowBlockIds
          ? $"{node.Kind}{Environment.NewLine}{FormatBlockId(node)}"
          : node.Kind;
    }

    var body = node.Text;

    return string.IsNullOrWhiteSpace(body)
        ? FormatBlockId(node)
        : ShowBlockIds
            ? $"{body}{Environment.NewLine}{FormatBlockId(node)}"
            : body;
  }

  private static string FormatEdgeLabel(ControlFlowPresentationEdge edge)
  {
    if (!string.IsNullOrWhiteSpace(edge.DisplayLabel))
    {
      return edge.DisplayLabel;
    }

    return edge.Kind switch
    {
      "ConditionalTrue" => "True",
      "ConditionalFalse" => "False",
      _ => string.Empty
    };
  }

  private static bool IsConditionNode(ControlFlowPresentationNode node)
  {
    return node.Kind is "Condition" or "SelectCase";
  }

  private static bool IsBackEdge(ControlFlowPresentationEdge edge)
  {
    return edge.IsBackEdge;
  }

  private static string FormatBlockId(ControlFlowPresentationNode node)
  {
    return node.SegmentIndex == 0
        ? $"Block {node.SourceBlockId}"
            : $"Block {node.SourceBlockId}.{node.SegmentIndex + 1}";
  }

  private sealed record SelectCaseFanout(
      string SelectorId,
      string GroupId,
      string JunctionId,
      IReadOnlyList<string> BusPointIds,
      IReadOnlyList<SelectCaseFanoutBranch> Branches);

  private sealed record SelectCaseFanoutBranch(
      string TapId,
      string TargetId);

  private sealed record SelectCaseMerge(
      string JunctionId,
      string TargetId,
      IReadOnlyList<string> GlobalBusPointIds,
      IReadOnlyList<SelectCaseMergeLane> Lanes);

  private sealed record SelectCaseMergeLane(
      string CaseNodeId,
      string OutputId,
      string GlobalTapId,
      IReadOnlyList<string> LocalBusPointIds,
      IReadOnlyList<SelectCaseMergeInput> Inputs);

  private sealed record SelectCaseMergeInput(
      ControlFlowPresentationEdge Edge,
      string TapId);

  private sealed record SelectCaseExitCandidate(
      string CaseNodeId,
      IReadOnlyList<ControlFlowPresentationEdge> Edges);
}
