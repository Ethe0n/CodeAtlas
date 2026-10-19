using CodeAtlas.Roslyn.Models;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;
using Microsoft.Msagl.Layout.Layered;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace CodeAtlas.UI;

public sealed class ControlFlowGraphView : UserControl
{
  private const bool ShowBlockIds = false;
  private const int ExportMargin = 64;
  private const int MaxExportWidth = 8_000;
  private const int MaxExportHeight = 12_000;
  private const long MaxExportPixels = 40_000_000;

  private readonly GViewer _viewer = new();
  private readonly GraphLayoutSession? _layoutSession;
  internal Graph? LayoutGraph => _viewer.Graph;
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
    _viewer.CustomOpenButtonPressed += (_, args) => args.Handled = true;

    Controls.Add(_viewer);
    Controls.Add(_emptyLabel);
    if (!GraphLayoutWorker.IsWorker)
      _layoutSession = new GraphLayoutSession(this, _viewer, _emptyLabel);

    ClearGraph();
  }

  private static void ApplyLayoutConstraints(
    Graph graph,
    ControlFlowPresentation presentation,
    IReadOnlyList<SelectCaseFanout> selectCaseFanouts,
    IReadOnlyList<SelectCaseMerge> selectCaseMerges,
    IReadOnlyDictionary<string, string> conditionalMergeTargets)
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

      ApplySelectCaseLaneConstraints(graph, presentation, fanout, conditionalMergeTargets);
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
            if (lane.Inputs.Count == 1)
            {
              graph.LayerConstraints.AddUpDownVerticalConstraint(source, tap);
            }
            else
            {
              graph.LayerConstraints.AddUpDownConstraint(source, tap);
            }
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

        if (lane.OutputId != lane.GlobalTapId &&
            laneOutput is not null &&
            globalTap is not null)
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

    if (_layoutSession is not null)
    {
      _layoutSession.Show(new GraphLayoutRequest { Kind = "Flow", Flow = controlFlow });
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
    if (selectCaseFanouts.Count > 0 && graph.LayoutAlgorithmSettings is SugiyamaLayoutSettings settings)
    {
      graph.Attr.NodeSeparation = settings.NodeSeparation = 32;
      graph.Attr.LayerSeparation = settings.LayerSeparation = 50;
    }
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
      graphEdge.UserData = edge;
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

    var conditionalMergeTargets = CreateConditionalMergeRouting(graph, presentation, selectCaseFanouts);
    ApplyLayoutConstraints(
        graph,
        presentation,
        selectCaseFanouts,
        selectCaseMerges,
        conditionalMergeTargets);
    ApplyExceptionRegionConstraints(graph, presentation);

    GraphLayoutWorker.ValidateGraph(graph);
    if (selectCaseFanouts.Count > 0)
    {
      _viewer.CalculateLayout(graph);
      RouteSelectCaseEdges(graph);
      _viewer.NeedToCalculateLayout = false;
      try
      {
        _viewer.Graph = graph;
      }
      finally
      {
        _viewer.NeedToCalculateLayout = true;
      }
    }
    else
    {
      _viewer.Graph = graph;
    }
    _viewer.Visible = true;
    _emptyLabel.Visible = false;
    BeginInvoke(new Action(FitGraphToViewport));
  }

  public bool CanExport => _viewer.Graph?.GeometryGraph is not null;
  public Task WaitForLayoutAsync() => _layoutSession?.Completion ?? Task.CompletedTask;

  public void ExportImage(IWin32Window owner)
  {
    var graph = _viewer.Graph;
    if (graph?.GeometryGraph is null)
    {
      return;
    }

    using var dialog = new SaveFileDialog
    {
      AddExtension = true,
      DefaultExt = "png",
      FileName = $"{CreateSafeFileName(graph.Label?.Text)}_CFG.png",
      Filter = "PNG Image (*.png)|*.png|SVG Vector Image (*.svg)|*.svg",
      OverwritePrompt = true,
      Title = "Export Control Flow"
    };
    if (dialog.ShowDialog(owner) != DialogResult.OK)
    {
      return;
    }

    try
    {
      if (string.Equals(Path.GetExtension(dialog.FileName), ".svg", StringComparison.OrdinalIgnoreCase))
      {
        ExportSvg(graph, dialog.FileName);
      }
      else
      {
        ExportPng(graph, dialog.FileName);
      }
    }
    catch (Exception exception)
    {
      MessageBox.Show(
          owner,
          exception.Message,
          "Export Control Flow",
          MessageBoxButtons.OK,
          MessageBoxIcon.Error);
    }
  }

  public void ExportDrawIo(IWin32Window owner)
  {
    var graph = _viewer.Graph;
    if (graph?.GeometryGraph is null)
    {
      return;
    }

    using var dialog = new SaveFileDialog
    {
      AddExtension = true,
      DefaultExt = "drawio",
      FileName = $"{CreateSafeFileName(graph.Label?.Text)}_CFG.drawio",
      Filter = "draw.io Diagram (*.drawio)|*.drawio|draw.io XML (*.xml)|*.xml",
      OverwritePrompt = true,
      Title = "Export Control Flow to draw.io"
    };
    if (dialog.ShowDialog(owner) != DialogResult.OK)
    {
      return;
    }

    try
    {
      DrawIoControlFlowExporter.Export(graph, dialog.FileName);
    }
    catch (Exception exception)
    {
      MessageBox.Show(
          owner,
          exception.Message,
          "Export Control Flow to draw.io",
          MessageBoxButtons.OK,
          MessageBoxIcon.Error);
    }
  }

  public void ClearGraph()
  {
    _layoutSession?.Cancel();
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

  private static void RouteSelectCaseEdges(Graph graph)
  {
    var geometry = graph.GeometryGraph!;
    var boundaries = geometry.Nodes.ToDictionary(node => node, node => node.BoundaryCurve);
    var edgeStates = geometry.Edges.Select(edge => new
    {
      Edge = edge,
      edge.Curve,
      edge.UnderlyingPolyline,
      edge.SourcePort,
      edge.TargetPort,
      SourceTip = edge.EdgeGeometry.SourceArrowhead?.TipPosition,
      TargetTip = edge.EdgeGeometry.TargetArrowhead?.TipPosition,
      Labels = edge.Labels.Select(label => (Label: label, label.Center)).ToArray()
    }).ToArray();

    try
    {
      // Rectangular routing obstacles avoid MSAGL's diamond-port direction error.
      // Restore the real boundaries afterwards; only edge geometry is changed.
      foreach (var node in geometry.Nodes)
      {
        node.BoundaryCurve = Microsoft.Msagl.Core.Geometry.Curves.CurveFactory
            .CreateRectangle(node.BoundingBox);
      }

      foreach (var edge in geometry.Edges)
      {
        edge.SourcePort = null;
        edge.TargetPort = null;
      }

      var routing = graph.LayoutAlgorithmSettings.EdgeRoutingSettings;
      routing.EdgeRoutingMode = Microsoft.Msagl.Core.Routing.EdgeRoutingMode.Rectilinear;
      routing.Padding = 6;
      routing.CornerRadius = 4;
      Microsoft.Msagl.Miscellaneous.LayoutHelpers.RouteAndLabelEdges(
          geometry, graph.LayoutAlgorithmSettings, geometry.Edges, 0, null);
    }
    catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
    {
      System.Diagnostics.Debug.WriteLine($"CFG routing retained the original layout: {exception.Message}");
      foreach (var state in edgeStates)
      {
        state.Edge.Curve = state.Curve;
        state.Edge.UnderlyingPolyline = state.UnderlyingPolyline;
        if (state.SourceTip is { } sourceTip)
        {
          state.Edge.EdgeGeometry.SourceArrowhead.TipPosition = sourceTip;
        }

        if (state.TargetTip is { } targetTip)
        {
          state.Edge.EdgeGeometry.TargetArrowhead.TipPosition = targetTip;
        }

        foreach (var (label, center) in state.Labels)
        {
          label.Center = center;
        }
      }
    }
    finally
    {
      foreach (var (node, boundary) in boundaries)
      {
        node.BoundaryCurve = boundary;
      }

      foreach (var state in edgeStates)
      {
        state.Edge.SourcePort = state.SourcePort;
        state.Edge.TargetPort = state.TargetPort;
      }

      geometry.UpdateBoundingBox();
    }
  }

  private static void ApplyExceptionRegionConstraints(
      Graph graph,
      ControlFlowPresentation presentation)
  {
    var regionGroups = presentation.Nodes
        .Where(node => !string.IsNullOrWhiteSpace(node.ExceptionRegionId))
        .GroupBy(node => node.ExceptionRegionId!, StringComparer.Ordinal)
        .Select(group => new
        {
          Nodes = group
              .OrderBy(node => node.SourceBlockId)
              .ThenBy(node => node.SegmentIndex)
              .ToArray(),
          First = group
              .OrderBy(node => node.SourceBlockId)
              .ThenBy(node => node.SegmentIndex)
              .First()
        })
        .ToArray();

    foreach (var region in regionGroups)
    {
      var regionNodeIds = region.Nodes
          .Select(node => node.Id)
          .ToHashSet(StringComparer.Ordinal);

      foreach (var edge in presentation.Edges.Where(edge =>
          regionNodeIds.Contains(edge.From) &&
          regionNodeIds.Contains(edge.To) &&
          !edge.IsBackEdge))
      {
        var outgoingCount = presentation.Edges.Count(candidate =>
            candidate.From == edge.From &&
            regionNodeIds.Contains(candidate.To) &&
            !candidate.IsBackEdge);
        var incomingCount = presentation.Edges.Count(candidate =>
            candidate.To == edge.To &&
            regionNodeIds.Contains(candidate.From) &&
            !candidate.IsBackEdge);
        if (outgoingCount != 1 || incomingCount != 1)
        {
          continue;
        }

        var source = graph.FindNode(edge.From);
        var target = graph.FindNode(edge.To);
        if (source is not null && target is not null)
        {
          graph.LayerConstraints.AddUpDownVerticalConstraint(source, target);
        }
      }
    }

    foreach (var catches in regionGroups
        .Where(region => region.First.ExceptionRegionKind == "Catch")
        .GroupBy(region => region.First.ExceptionGroupId, StringComparer.Ordinal))
    {
      var catchEntries = catches
          .Select(region => graph.FindNode(region.First.Id))
          .Where(node => node is not null)
          .Select(node => node!)
          .ToArray();
      if (catchEntries.Length < 2)
      {
        continue;
      }

      graph.LayerConstraints.PinNodesToSameLayer(catchEntries);
      for (var index = 0; index < catchEntries.Length - 1; index++)
      {
        graph.LayerConstraints.AddLeftRightConstraint(
            catchEntries[index],
            catchEntries[index + 1]);
      }
    }
  }

  private static void ExportPng(Graph graph, string filePath)
  {
    var size = CalculateExportSize(graph.GeometryGraph!.BoundingBox.Width, graph.GeometryGraph.BoundingBox.Height);
    using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);

    using (var graphics = Graphics.FromImage(bitmap))
    {
      graphics.Clear(System.Drawing.Color.White);
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

      // GraphRenderer.Render recalculates layout. Draw the existing viewer geometry instead.
      using var renderer = new GViewer { NeedToCalculateLayout = false };
      renderer.Graph = graph;
      var bounds = graph.GeometryGraph.BoundingBox;
      var scale = Math.Min(
          (size.Width - (ExportMargin * 2d)) / bounds.Width,
          (size.Height - (ExportMargin * 2d)) / bounds.Height);
      using var transform = new Matrix(
          (float)scale, 0, 0, (float)-scale,
          (float)((size.Width / 2d) - ((bounds.Left + (bounds.Width / 2)) * scale)),
          (float)((size.Height / 2d) + ((bounds.Bottom + (bounds.Height / 2)) * scale)));
      graphics.Transform = transform;
      Draw.DrawPrecalculatedLayoutObject(graphics, renderer.ViewerGraph);
    }

    bitmap.Save(filePath, ImageFormat.Png);
  }

  private static void ExportSvg(Graph graph, string filePath)
  {
    var labels = GetGraphLabels(graph)
        .ToArray();

    try
    {
      foreach (var (owner, label) in labels)
      {
        SetGraphLabel(owner, new Microsoft.Msagl.Drawing.Label(EscapeSvgText(label.Text))
        {
          Owner = owner,
          FontName = label.FontName,
          FontSize = label.FontSize,
          FontStyle = label.FontStyle,
          FontColor = label.FontColor,
          Width = label.Width,
          Height = label.Height,
          GeometryLabel = label.GeometryLabel,
          IsVisible = label.IsVisible
        });
      }

      using var stream = File.Create(filePath);
      var writer = new SvgGraphWriter(stream, graph);
      writer.Write();
    }
    finally
    {
      foreach (var (owner, label) in labels)
      {
        SetGraphLabel(owner, label);
      }
    }
  }

  private static IEnumerable<(DrawingObject Owner, Microsoft.Msagl.Drawing.Label Label)> GetGraphLabels(Graph graph)
  {
    if (graph.Label is not null)
    {
      yield return (graph, graph.Label);
    }

    foreach (var node in graph.Nodes)
    {
      if (node.Label is not null)
      {
        yield return (node, node.Label);
      }
    }

    foreach (var edge in graph.Edges)
    {
      if (edge.Label is not null)
      {
        yield return (edge, edge.Label);
      }
    }
  }

  private static void SetGraphLabel(DrawingObject owner, Microsoft.Msagl.Drawing.Label label)
  {
    switch (owner)
    {
      case Graph graph:
        graph.Label = label;
        break;
      case Node node:
        node.Label = label;
        break;
      case Edge edge:
        edge.Label = label;
        break;
    }
  }

  private static string EscapeSvgText(string text)
  {
    return text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
  }

  private static Size CalculateExportSize(double graphWidth, double graphHeight)
  {
    graphWidth = Math.Max(1, graphWidth);
    graphHeight = Math.Max(1, graphHeight);

    var scale = Math.Max(2, 1_600 / Math.Max(graphWidth, graphHeight));
    scale = Math.Min(scale, (MaxExportWidth - (ExportMargin * 2d)) / graphWidth);
    scale = Math.Min(scale, (MaxExportHeight - (ExportMargin * 2d)) / graphHeight);

    var width = CalculateExportDimension(graphWidth, scale, MaxExportWidth);
    var height = CalculateExportDimension(graphHeight, scale, MaxExportHeight);
    for (var attempt = 0; attempt < 8 && (long)width * height > MaxExportPixels; attempt++)
    {
      var pixelCount = (long)width * height;
      scale *= Math.Sqrt(MaxExportPixels / (double)pixelCount) * 0.999;
      width = CalculateExportDimension(graphWidth, scale, MaxExportWidth);
      height = CalculateExportDimension(graphHeight, scale, MaxExportHeight);
    }

    return new Size(width, height);
  }

  private static int CalculateExportDimension(double graphDimension, double scale, int maximum)
  {
    return Math.Clamp(
        (int)Math.Ceiling(graphDimension * scale) + (ExportMargin * 2),
        ExportMargin * 2 + 1,
        maximum);
  }

  private static string CreateSafeFileName(string? value)
  {
    var name = string.IsNullOrWhiteSpace(value) ? "ControlFlow" : value;
    foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
    {
      name = name.Replace(invalidCharacter, '_');
    }

    return name;
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
        lanes.Add(new SelectCaseMergeLane(
            candidate.CaseNodeId,
            globalTapId,
            globalTapId,
            Array.Empty<string>(),
            candidate.Edges
                .Select(edge => new SelectCaseMergeInput(edge, globalTapId))
                .ToArray()));
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

  private static IReadOnlyDictionary<string, string> CreateConditionalMergeRouting(
      Graph graph,
      ControlFlowPresentation presentation,
      IReadOnlyList<SelectCaseFanout> fanouts)
  {
    var plans = new List<(string ConditionId, string TargetId, HashSet<ControlFlowPresentationEdge> Inputs)>();
    foreach (var fanout in fanouts)
    {
      var groupIds = presentation.Nodes
          .Where(node => node.ControlStructureGroupId == fanout.GroupId)
          .Select(node => node.Id)
          .ToHashSet(StringComparer.Ordinal);
      foreach (var branch in fanout.Branches)
      {
        var depths = GetReachableDepthsWithinGroup(branch.TargetId, groupIds, presentation.Edges);
        var laneIds = depths.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var conditionId in laneIds)
        {
          var trueEdge = presentation.Edges.FirstOrDefault(edge =>
              edge.From == conditionId && edge.Kind == "ConditionalTrue" && laneIds.Contains(edge.To));
          var falseEdge = presentation.Edges.FirstOrDefault(edge =>
              edge.From == conditionId && edge.Kind == "ConditionalFalse" && laneIds.Contains(edge.To));
          if (trueEdge is null || falseEdge is null || trueEdge.To == falseEdge.To)
          {
            continue;
          }

          var trueBranch = GetReachableWithinGroup(trueEdge.To, laneIds, presentation.Edges);
          var falseBranch = GetReachableWithinGroup(falseEdge.To, laneIds, presentation.Edges);
          var targetId = trueBranch.Intersect(falseBranch, StringComparer.Ordinal)
              .OrderBy(id => depths[id])
              .FirstOrDefault();
          if (targetId is null)
          {
            continue;
          }

          var armIds = trueBranch.Union(falseBranch, StringComparer.Ordinal)
              .Append(conditionId).ToHashSet(StringComparer.Ordinal);
          var inputs = presentation.Edges.Where(edge =>
              edge.To == targetId && armIds.Contains(edge.From) && !edge.IsBackEdge &&
              edge.Kind is "FallThrough" or "ConditionalTrue" or "ConditionalFalse")
              .ToHashSet();
          if (inputs.Count > 1)
          {
            plans.Add((conditionId, targetId, inputs));
          }
        }
      }
    }

    var representedEdges = graph.Edges
        .Where(edge => edge.UserData is ControlFlowPresentationEdge)
        .ToDictionary(edge => edge, edge => new HashSet<ControlFlowPresentationEdge>
        {
          (ControlFlowPresentationEdge)edge.UserData
        });
    var mergeTargets = new Dictionary<string, string>(StringComparer.Ordinal);
    // Route inner joins first, then use their output as an input to the enclosing join.
    foreach (var plan in plans.OrderBy(plan => plan.Inputs.Count))
    {
      var inputs = graph.Edges.Where(edge => edge.Target == plan.TargetId &&
          representedEdges.TryGetValue(edge, out var represented) && represented.IsSubsetOf(plan.Inputs))
          .ToArray();
      if (inputs.Length == 0 ||
          !inputs.SelectMany(edge => representedEdges[edge]).ToHashSet().SetEquals(plan.Inputs))
      {
        continue;
      }

      if (inputs.Length == 1)
      {
        mergeTargets[plan.ConditionId] = inputs[0].Source;
        continue;
      }

      var junctionId = $"{plan.ConditionId}_branch_merge";
      var junction = graph.AddNode(junctionId);
      ConfigureInvisibleRoutingNode(junction);
      foreach (var input in inputs)
      {
        graph.RemoveEdge(input);
        var routed = graph.AddEdge(input.Source, junctionId);
        routed.Attr = input.Attr;
        routed.Attr.ArrowheadAtTarget = ArrowStyle.None;
        routed.Label = input.Label;
        if (routed.Label is not null)
        {
          routed.Label.Owner = routed;
        }

        representedEdges[routed] = representedEdges[input];
        graph.LayerConstraints.AddUpDownConstraint(graph.FindNode(input.Source), junction);
      }

      var output = graph.AddEdge(junctionId, plan.TargetId);
      ConfigureMergeRoutingEdge(output, true);
      representedEdges[output] = plan.Inputs;
      graph.LayerConstraints.AddUpDownConstraint(junction, graph.FindNode(plan.TargetId));
      mergeTargets[plan.ConditionId] = junctionId;
    }

    var junctionIds = mergeTargets.Values.ToHashSet(StringComparer.Ordinal);
    foreach (var edge in graph.Edges.Where(edge => junctionIds.Contains(edge.Source)))
    {
      if (graph.FindNode(edge.Target) is { } target && target.Attr.Shape != Shape.Point)
      {
        graph.LayerConstraints.AddUpDownVerticalConstraint(graph.FindNode(edge.Source), target);
      }
    }

    return mergeTargets;
  }

  private static void ApplySelectCaseLaneConstraints(
      Graph graph,
      ControlFlowPresentation presentation,
      SelectCaseFanout fanout,
      IReadOnlyDictionary<string, string> conditionalMergeTargets)
  {
    var groupNodeIds = presentation.Nodes
        .Where(node => string.Equals(
            node.ControlStructureGroupId,
            fanout.GroupId,
            StringComparison.Ordinal))
        .Select(node => node.Id)
        .ToHashSet(StringComparer.Ordinal);
    var laneDepths = fanout.Branches
        .Select(branch => GetReachableDepthsWithinGroup(
            branch.TargetId,
            groupNodeIds,
            presentation.Edges))
        .ToArray();

    void SeparateBranches(IEnumerable<string> leftIds, IEnumerable<string> rightIds)
    {
      foreach (var leftId in leftIds)
      {
        foreach (var rightId in rightIds)
        {
          var left = graph.FindNode(leftId);
          var right = graph.FindNode(rightId);
          if (left is not null && right is not null && left != right)
          {
            graph.LayerConstraints.AddLeftRightConstraint(left, right);
          }
        }
      }
    }

    var ownershipCounts = laneDepths.SelectMany(depths => depths.Keys)
        .GroupBy(id => id, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    var exclusiveLanes = laneDepths
        .Select(depths => depths.Keys.Where(id => ownershipCounts[id] == 1).ToArray())
        .ToArray();
    for (var index = 0; index < exclusiveLanes.Length - 1; index++)
    {
      SeparateBranches(exclusiveLanes[index], exclusiveLanes[index + 1]);
    }

    for (var laneIndex = 0; laneIndex < fanout.Branches.Count; laneIndex++)
    {
      var laneNodeIds = laneDepths[laneIndex].Keys.ToHashSet(StringComparer.Ordinal);
      var currentId = fanout.Branches[laneIndex].TargetId;

      while (true)
      {
        var outgoing = presentation.Edges
            .Where(edge =>
                edge.From == currentId &&
                !edge.IsBackEdge &&
                laneNodeIds.Contains(edge.To))
            .ToArray();
        if (outgoing.Length != 1)
        {
          break;
        }

        var targetId = outgoing[0].To;
        var incomingCount = presentation.Edges.Count(edge =>
            !edge.IsBackEdge &&
            laneNodeIds.Contains(edge.From) &&
            edge.To == targetId);
        if (incomingCount != 1)
        {
          break;
        }

        var source = graph.FindNode(currentId);
        var target = graph.FindNode(targetId);
        if (source is not null && target is not null)
        {
          graph.LayerConstraints.AddUpDownVerticalConstraint(source, target);
        }

        currentId = targetId;
      }

      foreach (var conditionId in laneNodeIds)
      {
        var trueEdge = presentation.Edges.FirstOrDefault(edge =>
            edge.From == conditionId &&
            edge.Kind == "ConditionalTrue" &&
            laneNodeIds.Contains(edge.To));
        var falseEdge = presentation.Edges.FirstOrDefault(edge =>
            edge.From == conditionId &&
            edge.Kind == "ConditionalFalse" &&
            laneNodeIds.Contains(edge.To));
        if (trueEdge is null || falseEdge is null || trueEdge.To == falseEdge.To)
        {
          continue;
        }

        var condition = graph.FindNode(conditionId);
        var trueTarget = graph.FindNode(trueEdge.To);
        var falseTarget = graph.FindNode(falseEdge.To);
        if (condition is null || trueTarget is null || falseTarget is null)
        {
          continue;
        }

        var trueBranch = GetReachableWithinGroup(trueEdge.To, laneNodeIds, presentation.Edges);
        var falseBranch = GetReachableWithinGroup(falseEdge.To, laneNodeIds, presentation.Edges);
        var trueArm = trueBranch.Except(falseBranch, StringComparer.Ordinal).ToArray();
        var falseArm = falseBranch.Except(trueBranch, StringComparer.Ordinal).ToArray();
        SeparateBranches(trueArm, falseArm);
        var mergeId = trueBranch.Intersect(falseBranch, StringComparer.Ordinal)
            .OrderBy(id => laneDepths[laneIndex][id])
            .FirstOrDefault();
        var layoutMergeId = conditionalMergeTargets.GetValueOrDefault(conditionId) ?? mergeId;
        if (layoutMergeId is not null && graph.FindNode(layoutMergeId) is { } merge)
        {
          graph.LayerConstraints.AddUpDownVerticalConstraint(condition, merge);
        }

        if (trueBranch.Contains(falseEdge.To) || falseBranch.Contains(trueEdge.To))
        {
          continue;
        }

        graph.LayerConstraints.AddUpDownVerticalConstraint(
            condition,
            trueArm.Length >= falseArm.Length ? trueTarget : falseTarget);
        graph.LayerConstraints.PinNodesToSameLayer(trueTarget, falseTarget);
        graph.LayerConstraints.AddLeftRightConstraint(trueTarget, falseTarget);
      }
    }

    var maximumDepth = laneDepths.SelectMany(depths => depths.Values).DefaultIfEmpty().Max();
    for (var depth = 0; depth <= maximumDepth; depth++)
    {
      var layerNodes = laneDepths.SelectMany(depths => depths
              .Where(item => item.Value == depth)
              .Select(item => graph.FindNode(item.Key)))
          .Where(node => node is not null)
          .Select(node => node!)
          .Distinct()
          .ToArray();
      if (layerNodes.Length > 1)
      {
        graph.LayerConstraints.PinNodesToSameLayer(layerNodes);
      }
    }
  }

  private static IReadOnlyDictionary<string, int> GetReachableDepthsWithinGroup(
      string startNodeId,
      IReadOnlySet<string> groupNodeIds,
      IReadOnlyList<ControlFlowPresentationEdge> edges)
  {
    var reachable = GetReachableWithinGroup(startNodeId, groupNodeIds, edges);
    var forwardEdges = edges
        .Where(edge => !edge.IsBackEdge && reachable.Contains(edge.From) && reachable.Contains(edge.To))
        .ToArray();
    var successors = forwardEdges.ToLookup(edge => edge.From, StringComparer.Ordinal);
    var incomingCounts = reachable.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
    foreach (var edge in forwardEdges)
    {
      incomingCounts[edge.To]++;
    }

    var longestDepths = reachable.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
    var depths = new Dictionary<string, int>(StringComparer.Ordinal);
    var pending = new Queue<string>(reachable.Where(id => incomingCounts[id] == 0));

    while (pending.Count > 0)
    {
      var current = pending.Dequeue();
      depths[current] = longestDepths[current];
      foreach (var edge in successors[current])
      {
        // A merge must follow every forward predecessor, not just the shortest branch.
        longestDepths[edge.To] = Math.Max(longestDepths[edge.To], depths[current] + 1);
        if (--incomingCounts[edge.To] == 0)
        {
          pending.Enqueue(edge.To);
        }
      }
    }

    return depths;
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
    // Label.Text expands literal backslash-n; the constructor preserves code escapes.
    graphNode.Label = new Microsoft.Msagl.Drawing.Label(FormatNodeLabel(node))
    {
      Owner = graphNode
    };
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
      _ when node.ExceptionRegionKind == "Try" => new Microsoft.Msagl.Drawing.Color(70, 112, 170),
      _ when node.ExceptionRegionKind == "Catch" => new Microsoft.Msagl.Drawing.Color(176, 76, 76),
      _ when node.ExceptionRegionKind == "Finally" => new Microsoft.Msagl.Drawing.Color(112, 88, 150),
      _ when node.Kind == "UsingResource" || node.ExceptionRegionKind == "Using" => new Microsoft.Msagl.Drawing.Color(35, 125, 125),
      _ => Microsoft.Msagl.Drawing.Color.SlateGray
    };
    graphNode.Attr.FillColor = node.Kind switch
    {
      "Entry" => new Microsoft.Msagl.Drawing.Color(224, 247, 232),
      "Exit" => new Microsoft.Msagl.Drawing.Color(238, 238, 238),
      _ when IsConditionNode(node) => new Microsoft.Msagl.Drawing.Color(255, 247, 224),
      _ when node.ExceptionRegionKind == "Try" => new Microsoft.Msagl.Drawing.Color(242, 247, 253),
      _ when node.ExceptionRegionKind == "Catch" => new Microsoft.Msagl.Drawing.Color(253, 244, 244),
      _ when node.ExceptionRegionKind == "Finally" => new Microsoft.Msagl.Drawing.Color(247, 244, 251),
      _ when node.Kind == "UsingResource" || node.ExceptionRegionKind == "Using" => new Microsoft.Msagl.Drawing.Color(239, 249, 248),
      _ => Microsoft.Msagl.Drawing.Color.White
    };
  }

  private static void ConfigureEdge(Edge graphEdge, ControlFlowPresentationEdge edge)
  {
    var edgeColor = GetEdgeColor(edge.Kind);

    graphEdge.Attr.ArrowheadAtTarget = ArrowStyle.Normal;
    graphEdge.Attr.LineWidth = edge.Kind is
        "ConditionalTrue" or
        "ConditionalFalse" or
        "ControlStructureLink" or
        "Exception"
            ? 1.4
            : 1;
    graphEdge.Attr.Color = edgeColor;

    if (graphEdge.Label is not null &&
        edge.Kind is "ConditionalTrue" or "ConditionalFalse" or "ControlStructureLink" or "Exception")
    {
      graphEdge.Label.FontColor = edgeColor;
    }

    if (edge.Kind == "Exception")
    {
      graphEdge.Attr.AddStyle(Style.Dashed);
    }
    else if (IsBackEdge(edge))
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
      "Exception" => Microsoft.Msagl.Drawing.Color.Firebrick,
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
