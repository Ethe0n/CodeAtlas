namespace CodeAtlas.Roslyn.Models;

public sealed record MethodStructure(
    string Name,
    string SymbolId,
    string ContainingTypeSymbolId,
    VbMethodKind Kind,
    string Accessibility,
    string? FilePath,
    bool IsGenerated,
    TextSpanInfo Span);

public enum VbMethodKind
{
    Sub,
    Function
}
