namespace CodeAtlas.Roslyn.Models;

public sealed record FieldStructure(
    string SymbolId,
    string Name,
    string Type,
    string Accessibility,
    bool IsShared,
    bool IsReadOnly,
    bool IsConst,
    string DeclaringTypeSymbolId,
    string? FilePath,
    bool IsGenerated,
    TextSpanInfo Span,
    string? Initializer);
