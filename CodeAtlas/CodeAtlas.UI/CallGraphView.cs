using CodeAtlas.Roslyn.Models;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;
using Microsoft.Msagl.Layout.Layered;
using MsaglColor = Microsoft.Msagl.Drawing.Color;

namespace CodeAtlas.UI;

public sealed class CallGraphView : UserControl
{
  private readonly GViewer _viewer = new();
  private readonly System.Windows.Forms.Label _emptyLabel = new()
  {
    Dock = DockStyle.Fill,
    Text = "No outgoing calls",
    TextAlign = ContentAlignment.MiddleCenter
  };

  public CallGraphView()
  {
    Dock = DockStyle.Fill;

    _viewer.Dock = DockStyle.Fill;
    _viewer.ToolBarIsVisible = true;

    Controls.Add(_viewer);
    Controls.Add(_emptyLabel);

    ClearGraph();
  }

  public void ShowGraph(MethodStructure method, IReadOnlyList<CallRelation> outgoingCalls)
  {
    var graph = new Graph(method.Name)
    {
      Directed = true
    };
    ConfigureGraphLayout(graph);

    var currentNodeId = GetMethodNodeId(method.SymbolId);
    var currentNode = graph.AddNode(currentNodeId);
    ConfigureCurrentMethodNode(currentNode, method);


    var groupedCalls = outgoingCalls
    .GroupBy(call => GetCallNodeId(call));

    foreach (var group in groupedCalls)
    {
      var call = group.First();
      var callCount = group.Count();

      var calleeNodeId = GetCallNodeId(call);
      var calleeNode = graph.FindNode(calleeNodeId) ?? graph.AddNode(calleeNodeId);

      ConfigureCalleeNode(calleeNode, call);

      var edgeLabel = callCount > 1
          ? $"¡¿{callCount}"
          : string.Empty;

      var edge = graph.AddEdge(
          currentNodeId,
          edgeLabel,
          calleeNodeId);

      ConfigureEdge(edge, call);

      graph.LayerConstraints.AddLeftRightConstraint(
          currentNode,
          calleeNode);
    }

    _viewer.Graph = graph;
    _viewer.Visible = true;
    _emptyLabel.Visible = false;
    BeginInvoke(new Action(() => _viewer.ZoomF = 1.0));
  }

  public void ClearGraph()
  {
    _viewer.Graph = null;
    _viewer.Visible = false;
    _emptyLabel.Visible = true;
  }

  private static void ConfigureGraphLayout(Graph graph)
  {
    graph.Attr.LayerDirection = LayerDirection.LR;
    graph.Attr.NodeSeparation = 56;
    graph.Attr.LayerSeparation = 90;
    graph.Attr.MinNodeHeight = 42;
    graph.Attr.MinNodeWidth = 170;
    graph.Attr.AspectRatio = 1.6;

    graph.LayoutAlgorithmSettings = new SugiyamaLayoutSettings
    {
      NodeSeparation = 56,
      LayerSeparation = 90,
      MinNodeHeight = 42,
      MinNodeWidth = 170,
      AspectRatio = 1.6,
      EdgeRoutingSettings =
            {
                EdgeRoutingMode = Microsoft.Msagl.Core.Routing.EdgeRoutingMode.SugiyamaSplines,
                Padding = 10
            }
    };
  }

  private static void ConfigureCurrentMethodNode(Node node, MethodStructure method)
  {
    node.LabelText = ShortMethodName(method.Name);
    node.Attr.Shape = Shape.Box;
    node.Attr.Padding = 12;
    node.Attr.LineWidth = 2.2;
    node.Attr.Color = MsaglColor.MidnightBlue;
    node.Attr.FillColor = new MsaglColor(225, 237, 255);
  }

  private static void ConfigureCalleeNode(Node node, CallRelation call)
  {
    node.LabelText = ShortCallName(call.CalleeDisplayName);
    node.Attr.Shape = Shape.Box;
    node.Attr.Padding = 12;
    node.Attr.LineWidth = call.IsProjectInternal ? 1.6 : 1.8;
    node.Attr.Color = call.IsProjectInternal
        ? MsaglColor.ForestGreen
        : MsaglColor.DarkOrange;
    node.Attr.FillColor = call.IsProjectInternal
        ? new MsaglColor(229, 247, 235)
        : new MsaglColor(255, 244, 220);
  }

  private static void ConfigureEdge(Edge edge, CallRelation call)
  {
    edge.Attr.ArrowheadAtTarget = ArrowStyle.Normal;
    edge.Attr.ArrowheadLength = 10;
    edge.Attr.LineWidth = call.IsProjectInternal ? 1.5 : 1.7;
    edge.Attr.Color = call.IsProjectInternal
        ? MsaglColor.ForestGreen
        : MsaglColor.DarkOrange;
  }

  private static string GetMethodNodeId(string methodId)
  {
    return $"method:{methodId}";
  }

  private static string GetCallNodeId(CallRelation call)
  {
    var scope = call.IsProjectInternal ? "internal" : "external";
    return $"callee:{scope}:{call.CalleeMethodSymbolId}";
  }

  private static string ShortMethodName(string methodName)
  {
    return methodName.EndsWith("()", StringComparison.Ordinal)
        ? methodName
        : $"{methodName}()";
  }

  private static string ShortCallName(string displayName)
  {
    var parenIndex = displayName.IndexOf('(', StringComparison.Ordinal);
    var signature = parenIndex >= 0 ? displayName[parenIndex..] : string.Empty;
    var namePart = parenIndex >= 0 ? displayName[..parenIndex] : displayName;
    var parts = namePart.Split('.', StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length >= 2)
    {
      return $"{parts[^2]}.{parts[^1]}{signature}";
    }

    return $"{namePart}{signature}";
  }
}
