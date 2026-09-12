using CodeAtlas.Roslyn.Models;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;

namespace CodeAtlas.UI;

public sealed class ControlFlowGraphView : UserControl
{
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
        graph.Attr.LayerDirection = LayerDirection.TB;

        foreach (var node in controlFlow.Nodes)
        {
            var graphNode = graph.AddNode(GetNodeId(node.Id));
            graphNode.LabelText = FormatNodeLabel(node);
            graphNode.Attr.Shape = Shape.Box;
            graphNode.Attr.Padding = 8;
            graphNode.Attr.FillColor = node.Kind switch
            {
                "Entry" => Microsoft.Msagl.Drawing.Color.LightGreen,
                "Exit" => Microsoft.Msagl.Drawing.Color.LightGray,
                _ => Microsoft.Msagl.Drawing.Color.White
            };
        }

        foreach (var edge in controlFlow.Edges)
        {
            var graphEdge = graph.AddEdge(
                GetNodeId(edge.From),
                FormatEdgeLabel(edge),
                GetNodeId(edge.To));
            graphEdge.Attr.ArrowheadAtTarget = ArrowStyle.Normal;
        }

        _viewer.Graph = graph;
        _viewer.Visible = true;
        _emptyLabel.Visible = false;
        _viewer.ZoomF = 1.0;
    }

    public void ClearGraph()
    {
        _viewer.Graph = null;
        _viewer.Visible = false;
        _emptyLabel.Visible = true;
    }

    private static string GetNodeId(int id)
    {
        return $"block_{id}";
    }

    private static string FormatNodeLabel(ControlFlowNode node)
    {
        var title = node.Kind is "Entry" or "Exit"
            ? $"Block {node.Id} [{node.Kind}]"
            : $"Block {node.Id}";

        var body = node.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.StartsWith("Condition: ", StringComparison.Ordinal)
                ? line["Condition: ".Length..]
                : line)
            .ToArray();

        return body.Length == 0
            ? title
            : $"{title}{Environment.NewLine}{string.Join(Environment.NewLine, body)}";
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
}
