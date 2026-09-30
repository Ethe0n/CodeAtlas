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
    ControlFlowPresentation presentation)
  {
    foreach (var edge in presentation.Edges)
    {
      if (IsBackEdge(edge))
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

    foreach (var selector in presentation.Nodes.Where(node => node.Kind == "SelectCase"))
    {
      var branchTargets = presentation.Edges
          .Where(edge => edge.From == selector.Id && edge.Kind == "ControlStructureLink")
          .Select(edge => graph.FindNode(edge.To))
          .Where(node => node is not null)
          .Select(node => node!)
          .Distinct()
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

    foreach (var edge in presentation.Edges)
    {
      var graphEdge = graph.AddEdge(
          edge.From,
          FormatEdgeLabel(edge),
          edge.To);
      ConfigureEdge(graphEdge, edge);
    }

    ApplyLayoutConstraints(graph, presentation);

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
    graph.Attr.MinNodeHeight = 30;
    graph.Attr.MinNodeWidth = 50;
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
}
