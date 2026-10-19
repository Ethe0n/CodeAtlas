using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml;
using CodeAtlas.Roslyn.Models;
using Microsoft.Msagl.Drawing;

namespace CodeAtlas.UI;

internal sealed record GraphLayoutRequest
{
  public string Kind { get; init; } = "";
  public ControlFlowInfo? Flow { get; init; }
  public MethodStructure? Method { get; init; }
  public CallRelation[] Incoming { get; init; } = [];
  public CallRelation[] Outgoing { get; init; } = [];
  public TypeStructure? SelectedType { get; init; }
  public TypeStructure[] Types { get; init; } = [];
  public TypeDependencyRelation[] Dependencies { get; init; } = [];
  public string ProjectName { get; init; } = "";

  public static TypeStructure SlimType(TypeStructure type) => type with
  {
    Fields = [], Properties = [], Methods = [], GeneratedMethods = [],
    UiControls = [], UiEventHandlers = [], UiPreview = null, FilePaths = []
  };

  public void Validate()
  {
    if (Types.Length > GraphLayoutWorker.MaxNodes || Incoming.Length + (long)Outgoing.Length > GraphLayoutWorker.MaxRelations ||
        Dependencies.Length > GraphLayoutWorker.MaxRelations || Flow?.Nodes.Count > GraphLayoutWorker.MaxNodes ||
        Flow?.Edges.Count > GraphLayoutWorker.MaxEdges)
    {
      throw new InvalidDataException("Graph input exceeds its safety limit. The graph was not truncated.");
    }
  }
}

internal sealed record GraphLayoutResponse(string? GraphXml, Dictionary<string, string> Symbols, string? Error = null);

internal static class GraphLayoutWorker
{
  internal const string Argument = "--graph-layout-worker";
  internal const int MaxRequestBytes = 16 * 1024 * 1024;
  internal const int MaxResponseBytes = 32 * 1024 * 1024;
  internal const int MaxNodes = 2_000;
  internal const int MaxEdges = 10_000;
  internal const int MaxRelations = 200_000;
  internal static bool IsWorker { get; private set; }
  private static readonly SemaphoreSlim WorkerSlots = new(2);

  public static int Run()
  {
    IsWorker = true;
    try
    {
      var bytes = ReadBoundedAsync(Console.OpenStandardInput(), MaxRequestBytes, CancellationToken.None).GetAwaiter().GetResult();
      var request = JsonSerializer.Deserialize<GraphLayoutRequest>(bytes)
          ?? throw new InvalidDataException("Missing graph request.");
      request.Validate();
      using var view = CreateView(request);
      _ = view.Handle;
      Graph? graph;
      IReadOnlyDictionary<string, string> symbols;
      switch (view)
      {
        case ControlFlowGraphView flow:
          flow.ShowGraph(request.Flow);
          graph = flow.LayoutGraph;
          symbols = new Dictionary<string, string>();
          break;
        case CallGraphView calls:
          calls.ShowGraph(request.Method!, request.Incoming, request.Outgoing);
          graph = calls.LayoutGraph;
          symbols = calls.LayoutSymbols;
          break;
        case ClassDependencyView types:
          types.ShowGraph(request.SelectedType!, request.Types, request.Dependencies);
          graph = types.LayoutGraph;
          symbols = types.LayoutSymbols;
          break;
        case ProjectDependencyView project:
          project.ShowGraph(new ProjectStructure("", request.ProjectName, null, "Visual Basic",
              request.Types, [], [], request.Dependencies, []));
          graph = project.LayoutGraph;
          symbols = project.LayoutSymbols;
          break;
        default:
          throw new InvalidDataException("Unsupported graph view.");
      }

      string? graphXml = null;
      if (graph is not null)
      {
        // Only geometry and drawing attributes cross the process boundary, never arbitrary UserData types.
        graph.UserData = null;
        foreach (var node in graph.Nodes) node.UserData = null;
        foreach (var edge in graph.Edges) edge.UserData = null;
        using var stream = new MemoryStream();
        graph.WriteToStream(stream);
        if (stream.Length > MaxResponseBytes / 2)
          throw new InvalidDataException("Calculated graph exceeds the output safety limit.");
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        graphXml = reader.ReadToEnd();
      }

      WriteResponse(new GraphLayoutResponse(graphXml, new Dictionary<string, string>(symbols)));
      return 0;
    }
    catch (Exception exception)
    {
      WriteResponse(new GraphLayoutResponse(null, new(), $"Graph layout failed: {exception.Message}"));
      return 1;
    }
  }

  private static UserControl CreateView(GraphLayoutRequest request) => request.Kind switch
  {
    "Flow" when request.Flow is not null => new ControlFlowGraphView(),
    "Calls" when request.Method is not null => new CallGraphView(),
    "Class" when request.SelectedType is not null => new ClassDependencyView(),
    "Project" => new ProjectDependencyView(),
    _ => throw new InvalidDataException("Invalid graph request.")
  };

  internal static void ValidateGraph(Graph graph)
  {
    if (graph.NodeCount > MaxNodes || graph.EdgeCount > MaxEdges ||
        graph.Nodes.Sum(node => (long)(node.LabelText?.Length ?? 0)) > 2_000_000)
    {
      throw new InvalidDataException("Graph exceeds its node, edge, or text safety limit. The graph was not truncated.");
    }
  }

  internal static Graph ReadGeometry(string xml)
  {
    if (xml.Length > MaxResponseBytes / 2)
      throw new InvalidDataException("Graph geometry exceeds its safety limit.");
    using (var input = new StringReader(xml))
    using (var reader = XmlReader.Create(input, new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxResponseBytes / 2
    }))
    {
      while (reader.Read())
      {
        if (reader.Depth > 128 || (reader.NodeType == XmlNodeType.Element &&
            reader.LocalName.Contains("UserData", StringComparison.OrdinalIgnoreCase)))
        {
          throw new InvalidDataException("Graph geometry must not contain serialized UserData or excessive nesting.");
        }
      }
    }

    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var graph = Graph.ReadGraphFromStream(stream);
    // MSAGL defaults omitted empty labels to Node.Id when reading routing points.
    foreach (var node in graph.Nodes.Where(node => node.Attr.Shape == Shape.Point))
      node.LabelText = string.Empty;
    ValidateGraph(graph);
    return graph;
  }

  internal static async Task<(Graph? Graph, Dictionary<string, string> Symbols)> CalculateAsync(
      GraphLayoutRequest request, CancellationToken cancellationToken,
      TimeSpan? timeout = null, long maxWorkingSetBytes = 768L * 1024 * 1024)
  {
    request.Validate();
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
    var token = deadline.Token;
    try
    {
      await WorkerSlots.WaitAsync(token).ConfigureAwait(false);
      try
      {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request);
        if (bytes.Length > MaxRequestBytes) throw new InvalidDataException("Graph request exceeds its byte safety limit.");
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = CreateStartInfo() };
        if (!process.Start()) throw new InvalidOperationException("Could not start graph layout worker.");
        using var stop = token.Register(() => Stop(process));
        Task? memoryGuard = null;
        try
        {
          var output = ReadBoundedAsync(process.StandardOutput.BaseStream, MaxResponseBytes, token);
          memoryGuard = MonitorMemoryAsync(process, maxWorkingSetBytes, token);
          await process.StandardInput.BaseStream.WriteAsync(bytes, token).ConfigureAwait(false);
          process.StandardInput.Close();
          // Observe pipe errors immediately; never leave a worker blocked on a full output pipe.
          var responseBytes = await output.ConfigureAwait(false);
          await process.WaitForExitAsync(token).ConfigureAwait(false);
          await memoryGuard.ConfigureAwait(false);
          var response = JsonSerializer.Deserialize<GraphLayoutResponse>(responseBytes)
              ?? throw new InvalidDataException("Missing graph worker response.");
          if (response.Error is not null) throw new InvalidOperationException(response.Error);
          if (process.ExitCode != 0) throw new InvalidOperationException("Graph layout worker exited unexpectedly.");
          token.ThrowIfCancellationRequested();
          return (response.GraphXml is null ? null : ReadGeometry(response.GraphXml), response.Symbols);
        }
        finally
        {
          Stop(process);
          await process.WaitForExitAsync().ConfigureAwait(false);
          if (memoryGuard is not null) await memoryGuard.ConfigureAwait(false);
        }
      }
      finally
      {
        WorkerSlots.Release();
      }
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      throw new TimeoutException("Graph layout exceeded its time limit and was stopped.");
    }
  }

  private static ProcessStartInfo CreateStartInfo()
  {
    var assemblyPath = Assembly.GetExecutingAssembly().Location;
    var executable = Path.ChangeExtension(assemblyPath, ".exe");
    var useHost = !File.Exists(executable);
    if (useHost)
    {
      var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)
          ?? throw new InvalidOperationException("Runtime path is unavailable.");
      executable = Path.GetFullPath(Path.Combine(runtimePath, "..", "..", "..", "dotnet.exe"));
    }
    var start = new ProcessStartInfo(executable)
    {
      UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
      RedirectStandardInput = true, RedirectStandardOutput = true
    };
    if (useHost) start.ArgumentList.Add(assemblyPath);
    start.ArgumentList.Add(Argument);
    return start;
  }

  private static async Task MonitorMemoryAsync(Process process, long maxBytes, CancellationToken token)
  {
    while (!process.HasExited)
    {
      token.ThrowIfCancellationRequested();
      process.Refresh();
      if (!process.HasExited && process.WorkingSet64 > maxBytes)
      {
        Stop(process);
        throw new InvalidOperationException("Graph layout exceeded its memory safety limit and was stopped.");
      }
      await Task.Delay(100, token).ConfigureAwait(false);
    }
  }

  private static void Stop(Process process)
  {
    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    catch (InvalidOperationException) { }
    catch (System.ComponentModel.Win32Exception) { }
  }

  private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken token)
  {
    using var buffer = new MemoryStream();
    var chunk = new byte[8192];
    int read;
    while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) != 0)
    {
      if (buffer.Length + read > maxBytes) throw new InvalidDataException("Graph pipe exceeds its byte safety limit.");
      buffer.Write(chunk, 0, read);
    }
    return buffer.ToArray();
  }

  private static void WriteResponse(GraphLayoutResponse response)
  {
    var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
    if (bytes.Length > MaxResponseBytes) throw new InvalidDataException("Graph response exceeds its byte safety limit.");
    using var output = Console.OpenStandardOutput();
    output.Write(bytes);
  }
}
