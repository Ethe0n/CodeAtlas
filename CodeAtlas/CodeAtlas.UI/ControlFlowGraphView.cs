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
      var busPoints = merge.BusPointIds
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

      foreach (var branch in merge.Branches)
      {
        for (var index = 0; index < branch.PathNodeIds.Count - 1; index++)
        {
          var pathSource = graph.FindNode(branch.PathNodeIds[index]);
          var pathTarget = graph.FindNode(branch.PathNodeIds[index + 1]);
          if (pathSource is not null && pathTarget is not null)
          {
            graph.LayerConstraints.AddUpDownVerticalConstraint(pathSource, pathTarget);
          }
        }

        var input = graph.FindNode(branch.InputId);
        var tap = graph.FindNode(branch.TapId);
        if (input is not null && tap is not null)
        {
          graph.LayerConstraints.AddUpDownVerticalConstraint(input, tap);
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

    foreach (var selector in presentation.Nodes.Where(node => node.Kind == "SelectCase"))
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

    foreach (var fanout in fanouts)
    {
      var reachableByBranch = fanout.Branches
          .Select(branch => GetReachableDistances(branch.TargetId, presentation.Edges))
          .ToArray();
      var commonNodeIds = reachableByBranch
          .Select(distances => distances.Keys.AsEnumerable())
          .Aggregate((common, nodeIds) => common.Intersect(nodeIds, StringComparer.Ordinal))
          .Where(nodeId =>
              nodeId != fanout.SelectorId &&
              fanout.Branches.All(branch => branch.TargetId != nodeId))
          .OrderBy(nodeId => reachableByBranch.Sum(distances => distances[nodeId]))
          .ThenBy(nodeId => reachableByBranch.Max(distances => distances[nodeId]));

      foreach (var commonNodeId in commonNodeIds)
      {
        var incomingEdges = presentation.Edges
            .Where(edge => edge.To == commonNodeId && !edge.IsBackEdge)
            .ToArray();
        var mergeInputs = new List<string>();
        var isValidMerge = true;

        for (var branchIndex = 0; branchIndex < reachableByBranch.Length; branchIndex++)
        {
          var branchDistances = reachableByBranch[branchIndex];
          var exclusiveEdges = incomingEdges
              .Where(edge =>
                  branchDistances.ContainsKey(edge.From) &&
                  reachableByBranch
                      .Where((_, index) => index != branchIndex)
                      .All(otherDistances => !otherDistances.ContainsKey(edge.From)))
              .Where(edge =>
                  edge.Kind is "FallThrough" or "Return" &&
                  string.IsNullOrWhiteSpace(edge.DisplayLabel))
              .ToArray();
          if (exclusiveEdges.Length != 1)
          {
            isValidMerge = false;
            break;
          }

          mergeInputs.Add(exclusiveEdges[0].From);
        }

        if (!isValidMerge || mergeInputs.Distinct(StringComparer.Ordinal).Count() != mergeInputs.Count)
        {
          continue;
        }

        var junctionId = $"{fanout.SelectorId}_merge_junction";
        var busPointIds = new List<string>();
        var branches = new List<SelectCaseMergeBranch>();
        var middleIndex = mergeInputs.Count / 2;

        for (var index = 0; index < mergeInputs.Count; index++)
        {
          if (mergeInputs.Count % 2 == 0 && index == middleIndex)
          {
            busPointIds.Add(junctionId);
          }

          var tapId = mergeInputs.Count % 2 == 1 && index == middleIndex
              ? junctionId
              : $"{fanout.SelectorId}_merge_tap_{index}";
          busPointIds.Add(tapId);
          branches.Add(new SelectCaseMergeBranch(
              mergeInputs[index],
              tapId,
              GetLinearPath(
                  fanout.Branches[index].TargetId,
                  mergeInputs[index],
                  presentation.Edges)));
        }

        foreach (var pointId in busPointIds.Distinct(StringComparer.Ordinal))
        {
          ConfigureInvisibleRoutingNode(graph.AddNode(pointId));
        }

        merges.Add(new SelectCaseMerge(
            junctionId,
            commonNodeId,
            busPointIds,
            branches));
        break;
      }
    }

    return merges;
  }

  private static Dictionary<string, int> GetReachableDistances(
      string startNodeId,
      IReadOnlyList<ControlFlowPresentationEdge> edges)
  {
    var distances = new Dictionary<string, int>(StringComparer.Ordinal)
    {
      [startNodeId] = 0
    };
    var pending = new Queue<string>();
    pending.Enqueue(startNodeId);

    while (pending.Count > 0)
    {
      var current = pending.Dequeue();
      foreach (var edge in edges.Where(edge => edge.From == current && !edge.IsBackEdge))
      {
        if (distances.ContainsKey(edge.To))
        {
          continue;
        }

        distances.Add(edge.To, distances[current] + 1);
        pending.Enqueue(edge.To);
      }
    }

    return distances;
  }

  private static IReadOnlyList<string> GetLinearPath(
      string startNodeId,
      string targetNodeId,
      IReadOnlyList<ControlFlowPresentationEdge> edges)
  {
    var path = new List<string> { startNodeId };
    var visited = new HashSet<string>(StringComparer.Ordinal) { startNodeId };
    var current = startNodeId;

    while (current != targetNodeId)
    {
      var nextNodeIds = edges
          .Where(edge => edge.From == current && !edge.IsBackEdge)
          .Select(edge => edge.To)
          .Distinct(StringComparer.Ordinal)
          .Where(nodeId => CanReach(nodeId, targetNodeId, edges))
          .ToArray();
      if (nextNodeIds.Length != 1 || !visited.Add(nextNodeIds[0]))
      {
        return Array.Empty<string>();
      }

      current = nextNodeIds[0];
      path.Add(current);
    }

    return path;
  }

  private static bool CanReach(
      string startNodeId,
      string targetNodeId,
      IReadOnlyList<ControlFlowPresentationEdge> edges)
  {
    if (startNodeId == targetNodeId)
    {
      return true;
    }

    var visited = new HashSet<string>(StringComparer.Ordinal) { startNodeId };
    var pending = new Queue<string>();
    pending.Enqueue(startNodeId);

    while (pending.Count > 0)
    {
      var current = pending.Dequeue();
      foreach (var edge in edges.Where(edge => edge.From == current && !edge.IsBackEdge))
      {
        if (edge.To == targetNodeId)
        {
          return true;
        }

        if (visited.Add(edge.To))
        {
          pending.Enqueue(edge.To);
        }
      }
    }

    return false;
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
    foreach (var branch in merge.Branches)
    {
      ConfigureMergeRoutingEdge(graph.AddEdge(branch.InputId, branch.TapId), false);
    }

    for (var index = 0; index < merge.BusPointIds.Count - 1; index++)
    {
      ConfigureMergeRoutingEdge(graph.AddEdge(
          merge.BusPointIds[index],
          merge.BusPointIds[index + 1]), false);
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
        merge.Branches.Any(branch => branch.InputId == edge.From));
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
      string JunctionId,
      IReadOnlyList<string> BusPointIds,
      IReadOnlyList<SelectCaseFanoutBranch> Branches);

  private sealed record SelectCaseFanoutBranch(
      string TapId,
      string TargetId);

  private sealed record SelectCaseMerge(
      string JunctionId,
      string TargetId,
      IReadOnlyList<string> BusPointIds,
      IReadOnlyList<SelectCaseMergeBranch> Branches);

  private sealed record SelectCaseMergeBranch(
      string InputId,
      string TapId,
      IReadOnlyList<string> PathNodeIds);
}
