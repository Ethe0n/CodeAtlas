namespace CodeAtlas.Roslyn.Models;

public sealed record MethodStructure(
    string Name,
    VbMethodKind Kind,
    string Accessibility,
    string? FilePath,
    TextSpanInfo Span);

public enum VbMethodKind
{
    Sub,
    Function
}
