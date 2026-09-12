namespace CodeAtlas.Roslyn.Models;

public sealed record TypeStructure(
    string Name,
    string FullName,
    string? Namespace,
    string? FilePath,
    TextSpanInfo Span,
    IReadOnlyList<MethodStructure> Methods);
