using CodeAtlas.Roslyn;
using System.Text.Json;
using System.Text.Json.Serialization;

var parsedArgs = ParseArgs(args);
var solutionPath = parsedArgs.SolutionPath is not null
    ? parsedArgs.SolutionPath
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "sample_project", "sample_project.sln"));

var analyzer = new VbSolutionAnalyzer();
var structure = await analyzer.AnalyzeAsync(solutionPath);

if (parsedArgs.JsonOutputPath is not null)
{
    var jsonPath = Path.GetFullPath(parsedArgs.JsonOutputPath);
    var jsonDirectory = Path.GetDirectoryName(jsonPath);
    if (!string.IsNullOrWhiteSpace(jsonDirectory))
    {
        Directory.CreateDirectory(jsonDirectory);
    }

    var json = JsonSerializer.Serialize(structure, new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    });
    await File.WriteAllTextAsync(jsonPath, json);
    Console.WriteLine($"JSON: {jsonPath}");
}

Console.WriteLine($"Solution: {structure.FilePath}");

foreach (var project in structure.Projects)
{
    Console.WriteLine($"Project: {project.Name}");

    foreach (var type in project.Types)
    {
        Console.WriteLine($"  Type: {type.FullName}");
        Console.WriteLine($"    Files: {string.Join(", ", type.FilePaths.Select(Path.GetFileName))}");

        foreach (var field in type.Fields)
        {
            var generated = field.IsGenerated ? " Generated" : string.Empty;
            var shared = field.IsShared ? " Shared" : string.Empty;
            Console.WriteLine($"    Field:{generated}{shared} {field.Accessibility} {field.Name} As {field.Type} ({field.FilePath}:{field.Span.StartLine}-{field.Span.EndLine})");
        }

        foreach (var property in type.Properties)
        {
            var generated = property.IsGenerated ? " Generated" : string.Empty;
            var shared = property.IsShared ? " Shared" : string.Empty;
            var accessor = property.IsReadOnly
                ? " ReadOnly"
                : property.IsWriteOnly
                    ? " WriteOnly"
                    : string.Empty;
            Console.WriteLine($"    Property:{generated}{shared}{accessor} {property.Accessibility} {property.Name} As {property.Type} ({property.FilePath}:{property.Span.StartLine}-{property.Span.EndLine})");
        }

        foreach (var method in type.Methods)
        {
            var generated = method.IsGenerated ? " Generated" : string.Empty;
            Console.WriteLine($"    Method:{generated} {method.Kind} {method.Name} ({method.Accessibility}) ({method.FilePath}:{method.Span.StartLine}-{method.Span.EndLine})");
        }

        if (type.UiControls.Count > 0)
        {
            Console.WriteLine("    UI Controls:");
            foreach (var control in type.UiControls)
            {
                Console.WriteLine($"      {control.Name} : {control.Type}");
            }
        }

        if (type.UiEventHandlers.Count > 0)
        {
            Console.WriteLine("    UI Event Handlers:");
            foreach (var handler in type.UiEventHandlers)
            {
                Console.WriteLine($"      {handler.ControlName}.{handler.EventName} -> {handler.HandlerMethodName}");
            }
        }

        if (type.GeneratedMethods.Count > 0)
        {
            Console.WriteLine($"    Generated Methods: {string.Join(", ", type.GeneratedMethods.Select(method => method.Name))}");
        }
    }

    Console.WriteLine("  Calls:");
    foreach (var call in project.Calls)
    {
        var scope = call.IsProjectInternal
            ? "Internal"
            : $"External:{call.CalleeAssemblyName ?? "Unknown"}";

        Console.WriteLine($"    [{scope}] {call.CallerDisplayName} -> {call.CalleeDisplayName}");
    }

    Console.WriteLine("  Control Flows:");
    foreach (var controlFlow in project.ControlFlows)
    {
        Console.WriteLine($"    CFG: {controlFlow.MethodName}");

        foreach (var node in controlFlow.Nodes)
        {
            var nodeLabel = node.Kind is "Entry" or "Exit"
                ? $"Block {node.Id} [{node.Kind}]"
                : $"Block {node.Id}";

            Console.WriteLine($"      {nodeLabel}");

            foreach (var line in node.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                Console.WriteLine($"        {line}");
            }

            foreach (var edge in controlFlow.Edges.Where(edge => edge.From == node.Id))
            {
                var condition = string.IsNullOrWhiteSpace(edge.Condition)
                    ? string.Empty
                    : $" ({edge.Condition})";
                Console.WriteLine($"        {edge.Kind}{condition} -> Block {edge.To}");
            }
        }
    }
}

static (string? SolutionPath, string? JsonOutputPath) ParseArgs(string[] args)
{
    string? solutionPath = null;
    string? jsonOutputPath = null;

    for (var index = 0; index < args.Length; index++)
    {
        var arg = args[index];
        if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
        {
            jsonOutputPath = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "codeatlas-analysis.json";
            continue;
        }

        solutionPath ??= arg;
    }

    return (solutionPath, jsonOutputPath);
}
