namespace CodeAtlas.Roslyn.Models;

public sealed record SolutionStructure(
    string FilePath,
    IReadOnlyList<ProjectStructure> Projects);
