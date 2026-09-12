namespace CodeAtlas.Roslyn.Models;

public sealed record PropertyStructure(
    string Name,
    string Type,
    string Accessibility,
    bool IsShared,
    bool IsReadOnly,
    bool IsWriteOnly,
    string? FilePath,
    bool IsGenerated,
    TextSpanInfo Span);
