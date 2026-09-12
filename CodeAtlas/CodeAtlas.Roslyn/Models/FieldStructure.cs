namespace CodeAtlas.Roslyn.Models;

public sealed record FieldStructure(
    string Name,
    string Type,
    string Accessibility,
    bool IsShared,
    string? FilePath,
    bool IsGenerated,
    TextSpanInfo Span);
