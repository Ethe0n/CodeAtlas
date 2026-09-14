using CodeAtlas.Roslyn.Models;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;
using Microsoft.Msagl.Layout.Layered;
using MsaglColor = Microsoft.Msagl.Drawing.Color;

namespace CodeAtlas.UI;

public sealed class ClassDependencyView : UserControl
{
  private readonly GViewer _viewer = new();
  private readonly Dictionary<string, string> _nodeTypeSymbols = new(StringComparer.Ordinal);
  private readonly System.Windows.Forms.Label _emptyLabel = new()
  {
    Dock = DockStyle.Fill,
    Text = "No dependencies available",
    TextAlign = ContentAlignment.MiddleCenter
  };

  public event Action<string>? TypeSelected;

  public ClassDependencyView()
  {
    Dock = DockStyle.Fill;

    _viewer.Dock = DockStyle.Fill;
    _viewer.ToolBarIsVisible = true;
    _viewer.MouseDoubleClick += Viewer_MouseDoubleClick;

    Controls.Add(_viewer);
    Controls.Add(_emptyLabel);

    ClearGraph();
  }

  public void ShowGraph(
      TypeStructure selectedType,
      IReadOnlyList<TypeStructure> types,
      IReadOnlyList<TypeDependencyRelation> dependencies)
  {
    _nodeTypeSymbols.Clear();

    var incomingDependencies = dependencies
        .Where(dependency => string.Equals(dependency.TargetTypeSymbolId, selectedType.SymbolId, StringComparison.Ordinal))
        .ToArray();
    var outgoingDependencies = dependencies
        .Where(dependency => string.Equals(dependency.SourceTypeSymbolId, selectedType.SymbolId, StringComparison.Ordinal))
        .ToArray();

    if (incomingDependencies.Length == 0 && outgoingDependencies.Length == 0)
    {
      ClearGraph();
      return;
    }

    var typesById = types
        .GroupBy(type => type.SymbolId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    var graph = new Graph(selectedType.Name)
    {
      Directed = true
    };
    ConfigureGraphLayout(graph);

    var selectedNodeId = GetSelectedNodeId(selectedType.SymbolId);
    var selectedNode = graph.AddNode(selectedNodeId);
    _nodeTypeSymbols[selectedNodeId] = selectedType.SymbolId;
    ConfigureSelectedTypeNode(selectedNode, selectedType);

    foreach (var group in incomingDependencies.GroupBy(dependency => dependency.SourceTypeSymbolId, StringComparer.Ordinal))
    {
      if (!typesById.TryGetValue(group.Key, out var sourceType))
      {
        continue;
      }

      var sourceNodeId = GetIncomingNodeId(sourceType.SymbolId);
      var sourceNode = graph.FindNode(sourceNodeId) ?? graph.AddNode(sourceNodeId);
      _nodeTypeSymbols[sourceNodeId] = sourceType.SymbolId;
      ConfigureRelatedTypeNode(sourceNode, sourceType, isIncoming: true);

      var edge = graph.AddEdge(sourceNodeId, FormatDependencyCount(group.Count()), selectedNodeId);
      ConfigureIncomingEdge(edge);
      graph.LayerConstraints.AddUpDownConstraint(sourceNode, selectedNode);
    }

    foreach (var group in outgoingDependencies.GroupBy(dependency => dependency.TargetTypeSymbolId, StringComparer.Ordinal))
    {
      if (!typesById.TryGetValue(group.Key, out var targetType))
      {
        continue;
      }

      var targetNodeId = GetOutgoingNodeId(targetType.SymbolId);
      var targetNode = graph.FindNode(targetNodeId) ?? graph.AddNode(targetNodeId);
      _nodeTypeSymbols[targetNodeId] = targetType.SymbolId;
      ConfigureRelatedTypeNode(targetNode, targetType, isIncoming: false);

      var edge = graph.AddEdge(selectedNodeId, FormatDependencyCount(group.Count()), targetNodeId);
      ConfigureOutgoingEdge(edge);
      graph.LayerConstraints.AddUpDownConstraint(selectedNode, targetNode);
    }

    _viewer.Graph = graph;
    _viewer.Visible = true;
    _emptyLabel.Visible = false;
    BeginInvoke(new Action(FitGraphToViewport));
  }

  public void ClearGraph()
  {
    _nodeTypeSymbols.Clear();
    _viewer.Graph = null;
    _viewer.Visible = false;
    _emptyLabel.Visible = true;
  }

  private void Viewer_MouseDoubleClick(object? sender, MouseEventArgs args)
  {
    if (_viewer.ObjectUnderMouseCursor?.DrawingObject is not Node node)
    {
      return;
    }

    if (_nodeTypeSymbols.TryGetValue(node.Id, out var typeSymbolId))
    {
      TypeSelected?.Invoke(typeSymbolId);
    }
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
    graph.Attr.LayerDirection = LayerDirection.LR;
    graph.Attr.NodeSeparation = 60;
    graph.Attr.LayerSeparation = 100;
    graph.Attr.MinNodeHeight = 42;
    graph.Attr.MinNodeWidth = 160;
    graph.Attr.AspectRatio = 0;

    graph.LayoutAlgorithmSettings = new SugiyamaLayoutSettings
    {
      NodeSeparation = 60,
      LayerSeparation = 100,
      MinNodeHeight = 42,
      MinNodeWidth = 160,
      AspectRatio = 0,
      EdgeRoutingSettings =
      {
        EdgeRoutingMode = Microsoft.Msagl.Core.Routing.EdgeRoutingMode.Rectilinear,
        Padding = 10
      }
    };
  }

  private static void ConfigureSelectedTypeNode(Node node, TypeStructure type)
  {
    node.LabelText = type.Name;
    node.Attr.Shape = Shape.Box;
    node.Attr.Padding = 12;
    node.Attr.LineWidth = 2.2;
    node.Attr.Color = MsaglColor.MidnightBlue;
    node.Attr.FillColor = new MsaglColor(225, 237, 255);
  }

  private static void ConfigureRelatedTypeNode(Node node, TypeStructure type, bool isIncoming)
  {
    node.LabelText = type.Name;
    node.Attr.Shape = Shape.Box;
    node.Attr.Padding = 12;
    node.Attr.LineWidth = 1.6;
    node.Attr.Color = isIncoming ? MsaglColor.DarkSlateBlue : MsaglColor.ForestGreen;
    node.Attr.FillColor = isIncoming
        ? new MsaglColor(237, 232, 255)
        : new MsaglColor(229, 247, 235);
  }

  private static void ConfigureIncomingEdge(Edge edge)
  {
    edge.Attr.ArrowheadAtTarget = ArrowStyle.Normal;
    edge.Attr.ArrowheadLength = 10;
    edge.Attr.LineWidth = 1.5;
    edge.Attr.Color = MsaglColor.DarkSlateBlue;
  }

  private static void ConfigureOutgoingEdge(Edge edge)
  {
    edge.Attr.ArrowheadAtTarget = ArrowStyle.Normal;
    edge.Attr.ArrowheadLength = 10;
    edge.Attr.LineWidth = 1.5;
    edge.Attr.Color = MsaglColor.ForestGreen;
  }

  private static string GetSelectedNodeId(string typeSymbolId)
  {
    return $"type:selected:{typeSymbolId}";
  }

  private static string GetIncomingNodeId(string typeSymbolId)
  {
    return $"type:incoming:{typeSymbolId}";
  }

  private static string GetOutgoingNodeId(string typeSymbolId)
  {
    return $"type:outgoing:{typeSymbolId}";
  }

  private static string FormatDependencyCount(int count)
  {
    return $"x{count}";
  }
}
