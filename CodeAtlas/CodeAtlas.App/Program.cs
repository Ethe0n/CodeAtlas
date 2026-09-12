using CodeAtlas.Roslyn;

var solutionPath = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "sample_project", "sample_project.sln"));

var analyzer = new VbSolutionAnalyzer();
var structure = await analyzer.AnalyzeAsync(solutionPath);

Console.WriteLine($"Solution: {structure.FilePath}");

foreach (var project in structure.Projects)
{
    Console.WriteLine($"Project: {project.Name}");

    foreach (var type in project.Types)
    {
        Console.WriteLine($"  Type: {type.FullName}");

        foreach (var method in type.Methods)
        {
            Console.WriteLine($"    Method: {method.Kind} {method.Name} ({method.Accessibility})");
        }
    }
}
