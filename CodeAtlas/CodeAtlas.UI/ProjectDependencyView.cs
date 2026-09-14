using CodeAtlas.Roslyn.Models;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;
using Microsoft.Msagl.Layout.Layered;
using MsaglColor = Microsoft.Msagl.Drawing.Color;

namespace CodeAtlas.UI;

public sealed class ProjectDependencyView : UserControl
{
  private readonly GViewer _viewer = new();
  private readonly Dictionary<string, string> _nodeTypeSymbols = new(StringComparer.Ordinal);
  private readonly System.Windows.Forms.Label _emptyLabel = new()
  {
    Dock = DockStyle.Fill,
    Text = "No project dependencies available",
    TextAlign = ContentAlignment.MiddleCenter
  };

  public event Action<string>? TypeSelected;

  public ProjectDependencyView()
  {
    Dock = DockStyle.Fill;

    _viewer.Dock = DockStyle.Fill;
    _viewer.ToolBarIsVisible = true;
    _viewer.MouseDoubleClick += Viewer_MouseDoubleClick;

    Controls.Add(_viewer);
    Controls.Add(_emptyLabel);

    ClearGraph();
  }

  public void ShowGraph(ProjectStructure project)
  {
    _nodeTypeSymbols.Clear();

    var typesById = project.Types
        .GroupBy(type => type.SymbolId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var duplicateTypeNames = project.Types
        .GroupBy(type => type.Name, StringComparer.Ordinal)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToHashSet(StringComparer.Ordinal);
    var dependencyGroups = project.TypeDependencies
        .Where(dependency =>
            typesById.ContainsKey(dependency.SourceTypeSymbolId) &&
            typesById.ContainsKey(dependency.TargetTypeSymbolId) &&
            !string.Equals(dependency.SourceTypeSymbolId, dependency.TargetTypeSymbolId, StringComparison.Ordinal))
        .GroupBy(
            dependency => new TypeDependencyPair(dependency.SourceTypeSymbolId, dependency.TargetTypeSymbolId),
            dependency => dependency,
            TypeDependencyPairComparer.Instance)
        .ToArray();

    if (dependencyGroups.Length == 0)
    {
      ClearGraph();
      return;
    }

    var graph = new Graph(project.Name)
    {
      Directed = true
    };
    ConfigureGraphLayout(graph);

    foreach (var group in dependencyGroups)
    {
      var sourceType = typesById[group.Key.SourceTypeSymbolId];
      var targetType = typesById[group.Key.TargetTypeSymbolId];
      var sourceNodeId = GetTypeNodeId(sourceType.SymbolId);
      var targetNodeId = GetTypeNodeId(targetType.SymbolId);
      var sourceNode = graph.FindNode(sourceNodeId) ?? graph.AddNode(sourceNodeId);
      var targetNode = graph.FindNode(targetNodeId) ?? graph.AddNode(targetNodeId);

      _nodeTypeSymbols[sourceNodeId] = sourceType.SymbolId;
      _nodeTypeSymbols[targetNodeId] = targetType.SymbolId;
      ConfigureTypeNode(sourceNode, sourceType, duplicateTypeNames);
      ConfigureTypeNode(targetNode, targetType, duplicateTypeNames);

      var edge = graph.AddEdge(sourceNodeId, FormatDependencyCount(group.Count()), targetNodeId);
      ConfigureDependencyEdge(edge);
      graph.LayerConstraints.AddUpDownConstraint(sourceNode, targetNode);
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
    graph.Attr.NodeSeparation = 64;
    graph.Attr.LayerSeparation = 105;
    graph.Attr.MinNodeHeight = 42;
    graph.Attr.MinNodeWidth = 160;
    graph.Attr.AspectRatio = 0;

    graph.LayoutAlgorithmSettings = new SugiyamaLayoutSettings
    {
      NodeSeparation = 64,
      LayerSeparation = 105,
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

  private static void ConfigureTypeNode(
      Node node,
      TypeStructure type,
      IReadOnlySet<string> duplicateTypeNames)
  {
    node.LabelText = GetTypeLabel(type, duplicateTypeNames);
    node.Attr.Shape = Shape.Box;
    node.Attr.Padding = 12;
    node.Attr.LineWidth = 1.6;
    node.Attr.Color = MsaglColor.DarkSlateBlue;
    node.Attr.FillColor = new MsaglColor(237, 242, 252);
  }

  private static void ConfigureDependencyEdge(Edge edge)
  {
    edge.Attr.ArrowheadAtTarget = ArrowStyle.Normal;
    edge.Attr.ArrowheadLength = 10;
    edge.Attr.LineWidth = 1.5;
    edge.Attr.Color = MsaglColor.ForestGreen;
  }

  private static string GetTypeNodeId(string typeSymbolId)
  {
    return $"project-type:{typeSymbolId}";
  }

  private static string FormatDependencyCount(int count)
  {
    return count > 1 ? $"x{count}" : string.Empty;
  }

  private static string GetTypeLabel(
      TypeStructure type,
      IReadOnlySet<string> duplicateTypeNames)
  {
    if (duplicateTypeNames.Contains(type.Name))
    {
      return type.FullName;
    }

    if (type.ContainingTypeSymbolId is not null)
    {
      if (!string.IsNullOrWhiteSpace(type.Namespace))
      {
        var namespacePrefix = $"{type.Namespace}.";
        if (type.FullName.StartsWith(namespacePrefix, StringComparison.Ordinal))
        {
          return type.FullName[namespacePrefix.Length..];
        }
      }

      return type.FullName;
    }

    return type.Name;
  }

  private sealed record TypeDependencyPair(
      string SourceTypeSymbolId,
      string TargetTypeSymbolId);

  private sealed class TypeDependencyPairComparer : IEqualityComparer<TypeDependencyPair>
  {
    public static readonly TypeDependencyPairComparer Instance = new();

    public bool Equals(TypeDependencyPair? x, TypeDependencyPair? y)
    {
      return x is not null &&
          y is not null &&
          string.Equals(x.SourceTypeSymbolId, y.SourceTypeSymbolId, StringComparison.Ordinal) &&
          string.Equals(x.TargetTypeSymbolId, y.TargetTypeSymbolId, StringComparison.Ordinal);
    }

    public int GetHashCode(TypeDependencyPair obj)
    {
      return HashCode.Combine(
          StringComparer.Ordinal.GetHashCode(obj.SourceTypeSymbolId),
          StringComparer.Ordinal.GetHashCode(obj.TargetTypeSymbolId));
    }
  }
}

