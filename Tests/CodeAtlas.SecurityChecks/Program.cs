using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using CodeAtlas.Roslyn;
using CodeAtlas.Roslyn.Models;
using CodeAtlas.UI;
using CodeAtlas.Lint;
using Microsoft.Build.Locator;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;

internal static class Program
{
  private static int _passed;
  private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

  [STAThread]
  private static int Main(string[] args)
  {
    var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
    try
    {
      CheckTrust(root);
      CheckLoaderLimits(root);
      var solution = new VbSolutionAnalyzer().AnalyzeAsync(
          Path.Combine(root, "sample_project", "sample_project.sln"), trustProjects: true).GetAwaiter().GetResult();
      Assert(solution.Projects.Any(project => project.Types.Count > 0), "Explicitly trusted sample analysis");
      var timedOut = new VbSolutionAnalyzer(new VbSyntaxStructureExtractor(), new AnalysisResourceLimits
      {
        ProjectTimeout = TimeSpan.FromMilliseconds(1)
      }).AnalyzeAsync(Path.Combine(root, "sample_project", "sample_project.sln"), trustProjects: true).GetAwaiter().GetResult();
      Assert(timedOut.Projects.All(project => project.AnalysisStatus == ProjectAnalysisStatus.Failed &&
          project.Diagnostics.Any(diagnostic => diagnostic.Stage == "ResourceLimit")), "Project deadline becomes a per-project failure");
      var legacy = new VbSolutionAnalyzer().AnalyzeAsync(Path.Combine(root,
          "TestProjects", "MsbuildComFailure", "MsbuildComFailure.sln"), trustProjects: true).GetAwaiter().GetResult();
      Assert(legacy.Projects.Count == 2 && legacy.Projects.All(project => project.Types.Count > 0), "Legacy COM-failure fallback preserves both project results");
      SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
      CheckToolbar();
      CheckGraphIsolation(solution);
      CheckUiGraphs(solution, root);
      CheckDiagnosticsUi(solution, root);
      Console.WriteLine($"PASS: {_passed} security/regression checks");
      return 0;
    }
    catch (Exception exception)
    {
      Console.Error.WriteLine(exception);
      return 1;
    }
  }

  private static void CheckTrust(string root)
  {
    var analyzer = new VbSolutionAnalyzer();
    Throws<InvalidOperationException>(() => analyzer.AnalyzeAsync(
        Path.Combine(root, "sample_project", "sample_project.sln")).GetAwaiter().GetResult(), "Untrusted API request rejected");
    Assert(!MSBuildLocator.IsRegistered, "Trust guard runs before MSBuild registration");
  }

  private static void CheckLoaderLimits(string root)
  {
    var directory = Path.Combine(root, "artifacts", "security-fixtures", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var sourcePath = Path.Combine(directory, "Sample.vb");
    File.WriteAllText(sourcePath, "Public Class Sample\nEnd Class\n");
    var projectPath = Path.Combine(directory, "Sample.vbproj");
    File.WriteAllText(projectPath, "<Project><PropertyGroup><TargetFrameworkVersion>v4.5.2</TargetFrameworkVersion></PropertyGroup><ItemGroup><Compile Include=\"Sample.vb\"/></ItemGroup></Project>");
    using (var loaded = new VbProjectFallbackLoader().LoadAsync(projectPath, default).GetAwaiter().GetResult())
      Assert(loaded.Project.Documents.Count() == 1, "Bounded fallback still loads normal source");
    Throws<InvalidDataException>(() => new VbProjectFallbackLoader(new AnalysisResourceLimits { MaxSourceFileBytes = 4 })
        .LoadAsync(projectPath, default).GetAwaiter().GetResult(), "Oversized source rejected");
    Throws<InvalidDataException>(() => new VbProjectFallbackLoader(new AnalysisResourceLimits { MaxProjectBytes = 4 })
        .LoadAsync(projectPath, default).GetAwaiter().GetResult(), "Oversized project XML rejected");
    Throws<InvalidDataException>(() => new VbProjectFallbackLoader(new AnalysisResourceLimits { MaxTotalSourceBytes = 4 })
        .LoadAsync(projectPath, default).GetAwaiter().GetResult(), "Total source budget enforced");
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    Throws<OperationCanceledException>(() => new VbProjectFallbackLoader()
        .LoadAsync(projectPath, canceled.Token).GetAwaiter().GetResult(), "Fallback cancellation before discovery");
    File.WriteAllText(projectPath, "<!DOCTYPE Project [<!ENTITY external SYSTEM 'file:///nonexistent'>]><Project>&external;</Project>");
    Throws<XmlException>(() => new VbProjectFallbackLoader().LoadAsync(projectPath, default).GetAwaiter().GetResult(), "DTD/external entities prohibited");
    for (var index = 0; index < 4; index++)
      File.WriteAllText(Path.Combine(directory, $"P{index}.vbproj"), index == 3 ? "<Project/>" :
          $"<Project><ItemGroup><ProjectReference Include=\"P{index + 1}.vbproj\"/></ItemGroup></Project>");
    Throws<InvalidDataException>(() => new VbProjectFallbackLoader(new AnalysisResourceLimits { MaxProjectReferenceDepth = 1 })
        .LoadAsync(Path.Combine(directory, "P0.vbproj"), default).GetAwaiter().GetResult(), "Reference depth bounded without recursion");
    Throws<InvalidDataException>(() => new VbProjectFallbackLoader(new AnalysisResourceLimits { MaxProjects = 2 })
        .LoadAsync(Path.Combine(directory, "P0.vbproj"), default).GetAwaiter().GetResult(), "Referenced project count bounded");
  }

  private static void CheckToolbar()
  {
    foreach (var view in new UserControl[] { new CallGraphView(), new ClassDependencyView(), new ProjectDependencyView(), new ControlFlowGraphView() })
    {
      using (view)
      {
        var viewer = Viewer(view);
        var handler = typeof(GViewer).GetMethod("OpenButtonPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // Default importer would show a blocking file dialog if the event failed to handle the request.
        handler.Invoke(viewer, null);
        Assert(viewer.ToolBarIsVisible && viewer.Graph is null, $"{view.GetType().Name}: Open blocked; toolbar retained");
      }
    }
  }

  private static void CheckGraphIsolation(SolutionStructure solution)
  {
    var flow = solution.Projects.SelectMany(project => project.ControlFlows).First(flow => flow.MethodName == "TestSelectCase");
    var request = new GraphLayoutRequest { Kind = "Flow", Flow = flow };
    var before = JsonSerializer.Serialize(flow, Json);
    var result = Task.Run(() => GraphLayoutWorker.CalculateAsync(request, default)).GetAwaiter().GetResult();
    Assert(result.Graph?.GeometryGraph is not null, "Isolated CFG worker returns laid-out geometry");
    Assert(before == JsonSerializer.Serialize(flow, Json), "Raw CFG unchanged by worker");
    Throws<TimeoutException>(() => Task.Run(() => GraphLayoutWorker.CalculateAsync(request, default,
        TimeSpan.FromMilliseconds(100))).GetAwaiter().GetResult(), "Worker timeout stops the process");
    using var cancellation = new CancellationTokenSource(100);
    Throws<OperationCanceledException>(() => Task.Run(() => GraphLayoutWorker.CalculateAsync(request, cancellation.Token))
        .GetAwaiter().GetResult(), "Worker cancellation stops the process");
    Throws<InvalidOperationException>(() => Task.Run(() => GraphLayoutWorker.CalculateAsync(request, default,
        maxWorkingSetBytes: 1)).GetAwaiter().GetResult(), "Worker memory guard stops the process");
    Throws<InvalidDataException>(() => GraphLayoutWorker.ReadGeometry("<Graph><UserData><Type>Unexpected</Type></UserData></Graph>"), "Serialized UserData prohibited at geometry boundary");
    Throws<XmlException>(() => GraphLayoutWorker.ReadGeometry("<!DOCTYPE Graph [<!ENTITY x SYSTEM 'file:///nonexistent'>]><Graph>&x;</Graph>"), "Geometry DTD prohibited");
    Throws<InvalidDataException>(() => new GraphLayoutRequest { Types = new TypeStructure[2_001] }.Validate(), "Oversized graph request rejected, not truncated");
  }

  private static void CheckUiGraphs(SolutionStructure solution, string root)
  {
    using var form = new Form { ClientSize = new System.Drawing.Size(1200, 850) };
    using var view = new ControlFlowGraphView();
    form.Controls.Add(view);
    _ = form.Handle;
    _ = view.Handle;
    var viewer = Viewer(view);
    var flows = solution.Projects.SelectMany(project => project.ControlFlows).ToArray();
    var flow = flows.First(flow => flow.MethodName == "TestSelectCase");
    var builder = new ControlFlowPresentationBuilder();
    var names = new[] { "TestFlow", "TestSelectCase", "TryCatchTest", "UsingTest" };
    foreach (var name in names)
    {
      var fixture = flows.FirstOrDefault(candidate => candidate.MethodName == name);
      if (fixture is null) continue;
      view.ShowGraph(fixture);
      WaitForGraph(view);
      var graph = viewer.Graph;
      var presentation = builder.Build(fixture);
      Assert(graph.Nodes.Count(node => node.Attr.Shape != Shape.Point) == presentation.Nodes.Count, $"{name}: visible nodes preserved");
      CheckTopology(graph, presentation);
      CheckRenderingRoundTrip(fixture, graph);
      Assert(view.CanExport, $"{name}: export available after layout");
    }

    view.ShowGraph(flow);
    view.ClearGraph();
    PumpUntil(() => Session(view)._GetCurrent() is null, TimeSpan.FromSeconds(2));
    Thread.Sleep(300);
    Application.DoEvents();
    Assert(viewer.Graph is null, "Clear cancels pending graph; stale result cannot reappear");
    var first = flows.First(candidate => candidate.MethodName == "TestFlow");
    view.ShowGraph(flow);
    view.ShowGraph(first);
    WaitForGraph(view);
    Assert(viewer.Graph!.Label?.Text == first.MethodName, "Rapid selection displays only latest graph");
    var graphBefore = viewer.Graph!;
    var exportPath = Path.Combine(root, "artifacts", "security-cfg.drawio");
    DrawIoControlFlowExporter.Export(graphBefore, exportPath);
    Assert(File.Exists(exportPath) && ReferenceEquals(graphBefore, viewer.Graph), "draw.io export preserves displayed graph");
    var pngPath = Path.Combine(root, "artifacts", "security-cfg.png");
    typeof(ControlFlowGraphView).GetMethod("ExportPng", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, [graphBefore, pngPath]);
    Assert(new FileInfo(pngPath).Length > 0, "PNG export succeeds with worker geometry");

    var project = solution.Projects.First(project => project.TypeDependencies.Count > 0);
    using var projectView = new ProjectDependencyView();
    form.Controls.Add(projectView);
    _ = projectView.Handle;
    projectView.ShowGraph(project);
    WaitForGraph(projectView);
    Assert(projectView.LayoutSymbols.Count > 0 && projectView.LayoutSymbols.Values.All(id => project.Types.Any(type => type.SymbolId == id)), "Project graph SymbolId navigation map preserved");
    var selectedType = project.Types.First(type => project.TypeDependencies.Any(dependency => dependency.SourceTypeSymbolId == type.SymbolId));
    using var typeView = new ClassDependencyView();
    form.Controls.Add(typeView);
    _ = typeView.Handle;
    typeView.ShowGraph(selectedType, project.Types, project.TypeDependencies);
    WaitForGraph(typeView);
    Assert(typeView.LayoutSymbols.Values.Contains(selectedType.SymbolId), "Class graph center SymbolId preserved");
    var method = project.Types.SelectMany(type => type.Methods).First(method => project.Calls.Any(call => call.CallerMethodSymbolId == method.SymbolId && call.IsProjectInternal));
    using var callView = new CallGraphView();
    form.Controls.Add(callView);
    _ = callView.Handle;
    callView.ShowGraph(method, project.Calls.Where(call => call.CalleeMethodSymbolId == method.SymbolId && call.IsProjectInternal).ToArray(),
        project.Calls.Where(call => call.CallerMethodSymbolId == method.SymbolId).ToArray());
    WaitForGraph(callView);
    Assert(callView.LayoutSymbols.Values.Contains(method.SymbolId), "Call graph center SymbolId preserved");
  }

  private static void CheckRenderingRoundTrip(ControlFlowInfo flow, Graph actual)
  {
    var mode = typeof(GraphLayoutWorker).GetProperty(nameof(GraphLayoutWorker.IsWorker), BindingFlags.Static | BindingFlags.NonPublic)!;
    mode.SetValue(null, true);
    try
    {
      using var reference = new ControlFlowGraphView();
      _ = reference.Handle;
      reference.ShowGraph(flow);
      var expected = reference.LayoutGraph!;
      expected.UserData = null;
      foreach (var node in expected.Nodes) node.UserData = null;
      foreach (var edge in expected.Edges) edge.UserData = null;
      using var stream = new MemoryStream();
      expected.WriteToStream(stream);
      stream.Position = 0;
      using var reader = new StreamReader(stream);
      var transferred = GraphLayoutWorker.ReadGeometry(reader.ReadToEnd());
      foreach (var node in expected.Nodes)
      {
        var copy = actual.FindNode(node.Id);
        if (copy is null || Normalize(copy.LabelText) != Normalize(node.LabelText) || copy.Attr.Shape != node.Attr.Shape ||
            copy.Attr.FillColor != node.Attr.FillColor || copy.Attr.Color != node.Attr.Color ||
            (transferred.FindNode(node.Id).GeometryNode.Center - node.GeometryNode.Center).Length > 0.01)
          throw new InvalidOperationException($"Node drawing or position changed during graph transfer: {node.Id}; " +
              $"shape {node.Attr.Shape}/{copy?.Attr.Shape}; fill {node.Attr.FillColor}/{copy?.Attr.FillColor}; " +
              $"color {node.Attr.Color}/{copy?.Attr.Color}; center {node.GeometryNode.Center}/{copy?.GeometryNode.Center}; " +
              $"label {node.LabelText}/{copy?.LabelText}");
      }
      var expectedEdges = expected.Edges.OrderBy(edge => edge.Source, StringComparer.Ordinal).ThenBy(edge => edge.Target, StringComparer.Ordinal).ThenBy(edge => edge.LabelText, StringComparer.Ordinal).ToArray();
      var actualEdges = actual.Edges.OrderBy(edge => edge.Source, StringComparer.Ordinal).ThenBy(edge => edge.Target, StringComparer.Ordinal).ThenBy(edge => edge.LabelText, StringComparer.Ordinal).ToArray();
      if (expectedEdges.Length != actualEdges.Length) throw new InvalidOperationException("Rendered edge count changed.");
      for (var index = 0; index < expectedEdges.Length; index++)
      {
        var edge = expectedEdges[index];
        var copy = actualEdges[index];
        if (edge.Source != copy.Source || edge.Target != copy.Target || Normalize(edge.LabelText) != Normalize(copy.LabelText) ||
            edge.Attr.Color != copy.Attr.Color || edge.Attr.LineWidth != copy.Attr.LineWidth ||
            edge.Attr.ArrowheadAtTarget != copy.Attr.ArrowheadAtTarget ||
            (edge.Label is not null && edge.Label.FontColor != copy.Label.FontColor))
          throw new InvalidOperationException($"Edge drawing changed during graph transfer: {edge.Source}->{edge.Target}/{copy.Source}->{copy.Target}; " +
              $"color {edge.Attr.Color}/{copy.Attr.Color}; width {edge.Attr.LineWidth}/{copy.Attr.LineWidth}; " +
              $"font {edge.Label?.FontColor}/{copy.Label?.FontColor}; label {edge.LabelText}/{copy.LabelText}");
      }
      Assert(true, $"{flow.MethodName}: node/edge styles and serialized geometry preserved");
    }
    finally { mode.SetValue(null, false); }
  }

  private static void CheckDiagnosticsUi(SolutionStructure solution, string root)
  {
    var result = new LintAnalyzer().Analyze(solution);
    using var form = new MainForm();
    _ = form.Handle;
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var formType = typeof(MainForm);
    var tabs = (TabControl)formType.GetField("_detailsTabs", flags)!.GetValue(form)!;
    Assert(tabs.TabPages.Cast<TabPage>().Select(tab => tab.Text).SequenceEqual(
        new[] { "Overview", "Relations", "Flow", "Diagnostics" }), "Diagnostics added without replacing graph tabs");
    var diagnostics = (DiagnosticsView)formType.GetField("_diagnosticsView", flags)!.GetValue(form)!;
    diagnostics.ShowResult(result);
    var grid = (DataGridView)typeof(DiagnosticsView).GetField("_grid", flags)!.GetValue(diagnostics)!;
    Assert(grid.VirtualMode && grid.RowCount == result.Diagnostics.Count, "Diagnostic rows use the existing virtual grid");
    var severity = (ComboBox)typeof(DiagnosticsView).GetField("_severity", flags)!.GetValue(diagnostics)!;
    severity.SelectedItem = LintSeverity.Warning;
    Assert(grid.RowCount == result.Diagnostics.Count(item => item.Severity == LintSeverity.Warning), "Diagnostics severity filter uses model values");
    severity.SelectedIndex = 0;
    if (grid.RowCount > 0)
    {
      grid.CurrentCell = grid.Rows[0].Cells[0];
      var details = (TextBox)typeof(DiagnosticsView).GetField("_details", flags)!.GetValue(diagnostics)!;
      Assert(details.Text.Contains("Recommendation", StringComparison.Ordinal), "Selected diagnostic displays evidence and recommendation");
    }

    var owner = solution.Projects.First(project => project.Types.Count > 0);
    var copy = owner with { Id = "duplicate-project-id", Name = owner.Name };
    var duplicated = solution with { Projects = [owner, copy] };
    formType.GetField("_solution", flags)!.SetValue(form, duplicated);
    formType.GetMethod("PopulateProjectExplorer", flags)!.Invoke(form, [duplicated]);
    var methodFinding = result.Diagnostics.First(item => item.RuleId == "CAL1001") with { ProjectId = copy.Id };
    formType.GetMethod("SelectDiagnostic", flags)!.Invoke(form, [methodFinding]);
    var tree = (TreeView)formType.GetField("_projectExplorer", flags)!.GetValue(form)!;
    var selected = tree.SelectedNode;
    var projectNode = selected;
    while (projectNode?.Parent is not null) projectNode = projectNode.Parent;
    Assert(projectNode?.Tag is ProjectStructure project && project.Id == copy.Id &&
        selected?.Tag?.GetType().GetProperty("Method")?.GetValue(selected.Tag) is MethodStructure method &&
        method.SymbolId == methodFinding.SymbolId, "Diagnostic navigation uses ProjectId plus SymbolId, not names");
    Assert(tabs.SelectedTab?.Text == "Overview", "Diagnostic activation returns to the existing Overview");
    tabs.SelectedIndex = 3;
    tree.SelectedNode = tree.Nodes[0];
    Assert(tabs.SelectedTab?.Text == "Diagnostics", "Ordinary TreeView selections preserve the Diagnostics tab");
    formType.GetField("_solution", flags)!.SetValue(form, solution);
    formType.GetMethod("PopulateProjectExplorer", flags)!.Invoke(form, [solution]);
    diagnostics.ShowResult(result);
    grid.CurrentCell = grid.Rows[0].Cells[0];
    tabs.SelectedIndex = 3;
    form.Show();
    Application.DoEvents();
    using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
    bitmap.Save(Path.Combine(root, "artifacts", "lint-diagnostics.png"));
    form.Hide();
  }

  private static void CheckTopology(Graph graph, ControlFlowPresentation presentation)
  {
    var visible = presentation.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
    foreach (var source in visible)
    {
      var reached = new HashSet<string>(StringComparer.Ordinal);
      var visited = new HashSet<string>(StringComparer.Ordinal);
      var pending = new Queue<string>(graph.Edges.Where(edge => edge.Source == source).Select(edge => edge.Target));
      while (pending.TryDequeue(out var id))
      {
        if (visible.Contains(id)) { reached.Add(id); continue; }
        if (!visited.Add(id)) continue;
        foreach (var edge in graph.Edges.Where(edge => edge.Source == id)) pending.Enqueue(edge.Target);
        foreach (var edge in graph.Edges.Where(edge => edge.Target == id && !visible.Contains(edge.Source) && edge.Attr.ArrowheadAtTarget == ArrowStyle.None)) pending.Enqueue(edge.Source);
      }
      if (!reached.SetEquals(presentation.Edges.Where(edge => edge.From == source).Select(edge => edge.To)))
        throw new InvalidOperationException($"Visible CFG topology changed at {presentation.MethodName}/{source}");
    }
    Assert(true, $"{presentation.MethodName}: visible edge topology preserved");
  }

  private static GViewer Viewer(UserControl view) => (GViewer)view.GetType().GetField("_viewer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
  private static string? Normalize(string? text) => text?.Replace("\r\n", "\n", StringComparison.Ordinal);
  private static GraphLayoutSession Session(UserControl view) => (GraphLayoutSession)view.GetType().GetField("_layoutSession", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
  private static object? _GetCurrent(this GraphLayoutSession session) => typeof(GraphLayoutSession).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session);
  private static void WaitForGraph(UserControl view)
  {
    PumpUntil(() => Session(view)._GetCurrent() is null, TimeSpan.FromSeconds(35));
    if (Viewer(view).Graph is null)
      throw new InvalidOperationException("Graph failed: " + ((System.Windows.Forms.Label)view.GetType().GetField("_emptyLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!).Text);
  }

  private static void PumpUntil(Func<bool> done, TimeSpan timeout)
  {
    var watch = Stopwatch.StartNew();
    while (!done())
    {
      if (watch.Elapsed > timeout) throw new TimeoutException("UI test timed out.");
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Application.DoEvents();
  }

  private static void Throws<T>(Action action, string message) where T : Exception
  {
    try { action(); }
    catch (T) { Assert(true, message); return; }
    throw new InvalidOperationException(message + " did not throw " + typeof(T).Name);
  }

  private static void Assert(bool condition, string message)
  {
    if (!condition) throw new InvalidOperationException(message);
    _passed++;
    Console.WriteLine("PASS " + message);
  }
}
