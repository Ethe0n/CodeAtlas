using CodeAtlas.Roslyn.Models;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;
using Microsoft.Msagl.Layout.Layered;

namespace CodeAtlas.UI;

public sealed class ControlFlowGraphView : UserControl
{
    private const bool ShowBlockIds = false;

    private readonly GViewer _viewer = new();
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

    public void ShowGraph(ControlFlowInfo? controlFlow)
    {
        if (controlFlow is null)
        {
            ClearGraph();
            return;
        }

        var graph = new Graph(controlFlow.MethodName)
        {
            Directed = true
        };
        ConfigureGraphLayout(graph);

        foreach (var node in controlFlow.Nodes)
        {
            var graphNode = graph.AddNode(GetNodeId(node.Id));
            ConfigureNode(graphNode, node);
        }

        foreach (var edge in controlFlow.Edges)
        {
            var graphEdge = graph.AddEdge(
                GetNodeId(edge.From),
                FormatEdgeLabel(edge),
                GetNodeId(edge.To));
            ConfigureEdge(graphEdge, edge);
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
        graph.Attr.LayerDirection = LayerDirection.TB;
        graph.Attr.NodeSeparation = 48;
        graph.Attr.LayerSeparation = 70;
        graph.Attr.MinNodeHeight = 48;
        graph.Attr.MinNodeWidth = 170;
        graph.Attr.AspectRatio = 0.8;

        graph.LayoutAlgorithmSettings = new SugiyamaLayoutSettings
        {
            NodeSeparation = 48,
            LayerSeparation = 70,
            EdgeRoutingSettings =
            {
                EdgeRoutingMode = Microsoft.Msagl.Core.Routing.EdgeRoutingMode.Spline,
                ConeAngle = 25,
                Padding = 8
            }
        };
    }

    private static void ConfigureNode(Node graphNode, ControlFlowNode node)
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

    private static void ConfigureEdge(Edge graphEdge, ControlFlowEdge edge)
    {
        graphEdge.Attr.ArrowheadAtTarget = ArrowStyle.Normal;
        graphEdge.Attr.LineWidth = edge.Kind is "ConditionalTrue" or "ConditionalFalse" ? 1.4 : 1;
        graphEdge.Attr.Color = edge.Kind switch
        {
            "ConditionalTrue" => Microsoft.Msagl.Drawing.Color.ForestGreen,
            "ConditionalFalse" => Microsoft.Msagl.Drawing.Color.Firebrick,
            _ => Microsoft.Msagl.Drawing.Color.DimGray
        };

        if (IsBackEdge(edge))
        {
            graphEdge.Attr.Color = Microsoft.Msagl.Drawing.Color.SteelBlue;
            graphEdge.Attr.LineWidth = 1.6;
        }
    }

    private static string GetNodeId(int id)
    {
        return $"block_{id}";
    }

    private static string FormatNodeLabel(ControlFlowNode node)
    {
        if (node.Kind is "Entry" or "Exit")
        {
            return ShowBlockIds
                ? $"{node.Kind}{Environment.NewLine}Block {node.Id}"
                : node.Kind;
        }

        var body = node.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.StartsWith("Condition: ", StringComparison.Ordinal)
                ? line["Condition: ".Length..]
                : line)
            .ToArray();

        return body.Length == 0
            ? FormatBlockId(node)
            : ShowBlockIds
                ? $"{string.Join(Environment.NewLine, body)}{Environment.NewLine}{FormatBlockId(node)}"
                : string.Join(Environment.NewLine, body);
    }

    private static string FormatEdgeLabel(ControlFlowEdge edge)
    {
        return edge.Kind switch
        {
            "ConditionalTrue" => "True",
            "ConditionalFalse" => "False",
            _ => string.Empty
        };
    }

    private static bool IsConditionNode(ControlFlowNode node)
    {
        return node.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.StartsWith("Condition: ", StringComparison.Ordinal));
    }

    private static bool IsBackEdge(ControlFlowEdge edge)
    {
        return edge.To <= edge.From;
    }

    private static string FormatBlockId(ControlFlowNode node)
    {
        return $"Block {node.Id}";
    }
}
