using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Drawing;
using System.Globalization;
using System.Xml.Linq;
using MsaglPoint = Microsoft.Msagl.Core.Geometry.Point;
using MsaglRectangle = Microsoft.Msagl.Core.Geometry.Rectangle;

namespace CodeAtlas.UI;

internal static class DrawIoControlFlowExporter
{
  private const double CanvasMargin = 40;
  private const double MinimumRoutingNodeSize = 1;

  public static void Export(Graph graph, string filePath)
  {
    if (graph.GeometryGraph is null)
    {
      throw new InvalidOperationException("The control flow graph has not been laid out.");
    }

    var graphBounds = graph.GeometryGraph.BoundingBox;
    var nodeIds = graph.Nodes
        .Select((node, index) => (node, Id: $"node_{index + 1}"))
        .ToDictionary(item => item.node.Id, item => item.Id, StringComparer.Ordinal);
    var root = new XElement(
        "root",
        new XElement("mxCell", new XAttribute("id", "0")),
        new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));

    foreach (var node in graph.Nodes)
    {
      root.Add(CreateNodeCell(node, nodeIds[node.Id], graphBounds));
    }

    var edgeIndex = 0;
    foreach (var edge in graph.Edges)
    {
      if (!nodeIds.TryGetValue(edge.Source, out var sourceId) ||
          !nodeIds.TryGetValue(edge.Target, out var targetId))
      {
        continue;
      }

      edgeIndex++;
      root.Add(CreateEdgeCell(
          edge,
          $"edge_{edgeIndex}",
          sourceId,
          targetId,
          graphBounds));
    }

    var canvasWidth = Math.Ceiling(graphBounds.Width + (CanvasMargin * 2));
    var canvasHeight = Math.Ceiling(graphBounds.Height + (CanvasMargin * 2));
    var model = new XElement(
        "mxGraphModel",
        new XAttribute("dx", FormatNumber(canvasWidth)),
        new XAttribute("dy", FormatNumber(canvasHeight)),
        new XAttribute("grid", "1"),
        new XAttribute("gridSize", "10"),
        new XAttribute("guides", "1"),
        new XAttribute("tooltips", "1"),
        new XAttribute("connect", "1"),
        new XAttribute("arrows", "1"),
        new XAttribute("fold", "1"),
        new XAttribute("page", "0"),
        new XAttribute("pageScale", "1"),
        new XAttribute("pageWidth", FormatNumber(canvasWidth)),
        new XAttribute("pageHeight", FormatNumber(canvasHeight)),
        new XAttribute("math", "0"),
        new XAttribute("shadow", "0"),
        root);
    var diagramName = string.IsNullOrWhiteSpace(graph.Label?.Text)
        ? "Control Flow"
        : graph.Label.Text;
    var document = new XDocument(
        new XDeclaration("1.0", "utf-8", null),
        new XElement(
            "mxfile",
            new XAttribute("host", "CodeAtlas"),
            new XAttribute("compressed", "false"),
            new XAttribute("pages", "1"),
            new XElement(
                "diagram",
                new XAttribute("id", Guid.NewGuid().ToString("N")),
                new XAttribute("name", diagramName),
                model)));

    document.Save(filePath);
  }

  private static XElement CreateNodeCell(
      Node node,
      string cellId,
      MsaglRectangle graphBounds)
  {
    var bounds = node.BoundingBox;
    var isRoutingNode = node.Attr.Shape == Shape.Point || node.Attr.Color.A == 0;
    var width = isRoutingNode
        ? Math.Max(MinimumRoutingNodeSize, bounds.Width)
        : bounds.Width;
    var height = isRoutingNode
        ? Math.Max(MinimumRoutingNodeSize, bounds.Height)
        : bounds.Height;
    var x = bounds.Left - graphBounds.Left + CanvasMargin;
    var y = graphBounds.Top - bounds.Top + CanvasMargin;

    return new XElement(
        "mxCell",
        new XAttribute("id", cellId),
        new XAttribute("value", isRoutingNode ? string.Empty : node.LabelText),
        new XAttribute("style", CreateNodeStyle(node, isRoutingNode)),
        new XAttribute("vertex", "1"),
        new XAttribute("parent", "1"),
        new XElement(
            "mxGeometry",
            new XAttribute("x", FormatNumber(x)),
            new XAttribute("y", FormatNumber(y)),
            new XAttribute("width", FormatNumber(width)),
            new XAttribute("height", FormatNumber(height)),
            new XAttribute("as", "geometry")));
  }

  private static XElement CreateEdgeCell(
      Edge edge,
      string cellId,
      string sourceId,
      string targetId,
      MsaglRectangle graphBounds)
  {
    var geometry = new XElement(
        "mxGeometry",
        new XAttribute("relative", "1"),
        new XAttribute("as", "geometry"));
    var waypoints = GetWaypoints(edge.GeometryEdge?.Curve)
        .Select(point => TransformPoint(point, graphBounds))
        .ToArray();
    if (waypoints.Length > 0)
    {
      geometry.Add(new XElement(
          "Array",
          new XAttribute("as", "points"),
          waypoints.Select(point => new XElement(
              "mxPoint",
              new XAttribute("x", FormatNumber(point.X)),
              new XAttribute("y", FormatNumber(point.Y))))));
    }

    return new XElement(
        "mxCell",
        new XAttribute("id", cellId),
        new XAttribute("value", edge.LabelText ?? string.Empty),
        new XAttribute("style", CreateEdgeStyle(edge)),
        new XAttribute("edge", "1"),
        new XAttribute("parent", "1"),
        new XAttribute("source", sourceId),
        new XAttribute("target", targetId),
        geometry);
  }

  private static string CreateNodeStyle(Node node, bool isRoutingNode)
  {
    if (isRoutingNode)
    {
      return "shape=ellipse;opacity=0;fillOpacity=0;strokeOpacity=0;resizable=0;connectable=1;html=0;";
    }

    var shape = node.Attr.Shape switch
    {
      Shape.Ellipse => "ellipse;",
      Shape.Diamond => "rhombus;",
      _ => "rounded=1;arcSize=8;"
    };
    var fontName = string.IsNullOrWhiteSpace(node.Label?.FontName)
        ? "Arial"
        : node.Label.FontName;
    var fontSize = node.Label?.FontSize > 0 ? node.Label.FontSize : 16;
    var fontColor = node.Label is null
        ? "#000000"
        : FormatColor(node.Label.FontColor);

    return string.Concat(
        shape,
        "whiteSpace=wrap;html=0;align=center;verticalAlign=middle;spacing=6;",
        $"fillColor={FormatColor(node.Attr.FillColor)};",
        $"strokeColor={FormatColor(node.Attr.Color)};",
        $"strokeWidth={FormatNumber(node.Attr.LineWidth)};",
        $"fontColor={fontColor};",
        $"fontFamily={fontName};",
        $"fontSize={FormatNumber(fontSize)};");
  }

  private static string CreateEdgeStyle(Edge edge)
  {
    var dashed = edge.Attr.Styles.Contains(Style.Dashed) ? "dashed=1;" : string.Empty;
    var endArrow = edge.Attr.ArrowheadAtTarget == ArrowStyle.None ? "none" : "block";
    var startArrow = edge.Attr.ArrowheadAtSource == ArrowStyle.None ? "none" : "block";
    var fontColor = edge.Label is null
        ? FormatColor(edge.Attr.Color)
        : FormatColor(edge.Label.FontColor);

    return string.Concat(
        "edgeStyle=orthogonalEdgeStyle;rounded=1;orthogonalLoop=1;jettySize=auto;html=0;",
        $"strokeColor={FormatColor(edge.Attr.Color)};",
        $"strokeWidth={FormatNumber(edge.Attr.LineWidth)};",
        $"fontColor={fontColor};",
        $"startArrow={startArrow};startFill=1;",
        $"endArrow={endArrow};endFill=1;",
        dashed);
  }

  private static IReadOnlyList<MsaglPoint> GetWaypoints(ICurve? curve)
  {
    if (curve is null)
    {
      return Array.Empty<MsaglPoint>();
    }

    var points = curve switch
    {
      Curve composite => GetCompositeCurvePoints(composite),
      Polyline polyline => polyline.PolylinePoints.Select(point => point.Point).ToList(),
      _ => SampleCurve(curve)
    };
    if (points.Count <= 2)
    {
      return Array.Empty<MsaglPoint>();
    }

    return points.Skip(1).Take(points.Count - 2).ToArray();
  }

  private static List<MsaglPoint> GetCompositeCurvePoints(Curve curve)
  {
    var points = new List<MsaglPoint>();
    foreach (var segment in curve.Segments)
    {
      AddPointIfDistinct(points, segment.Start);
      AddPointIfDistinct(points, segment.End);
    }

    return points;
  }

  private static List<MsaglPoint> SampleCurve(ICurve curve)
  {
    const int sampleCount = 12;
    var points = new List<MsaglPoint>(sampleCount + 1);
    for (var index = 0; index <= sampleCount; index++)
    {
      var parameter = curve.ParStart + ((curve.ParEnd - curve.ParStart) * index / sampleCount);
      AddPointIfDistinct(points, curve[parameter]);
    }

    return points;
  }

  private static void AddPointIfDistinct(List<MsaglPoint> points, MsaglPoint point)
  {
    if (points.Count == 0 ||
        Math.Abs(points[^1].X - point.X) > 0.01 ||
        Math.Abs(points[^1].Y - point.Y) > 0.01)
    {
      points.Add(point);
    }
  }

  private static MsaglPoint TransformPoint(MsaglPoint point, MsaglRectangle graphBounds)
  {
    return new MsaglPoint(
        point.X - graphBounds.Left + CanvasMargin,
        graphBounds.Top - point.Y + CanvasMargin);
  }

  private static string FormatColor(Microsoft.Msagl.Drawing.Color color)
  {
    return $"#{color.R:x2}{color.G:x2}{color.B:x2}";
  }

  private static string FormatNumber(double value)
  {
    return Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
  }
}
