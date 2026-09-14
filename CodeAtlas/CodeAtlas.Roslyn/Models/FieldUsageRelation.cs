namespace CodeAtlas.Roslyn.Models;

public sealed record FieldUsageRelation(
    string FieldSymbolId,
    string MethodSymbolId,
    FieldUsageKind UsageKind,
    string? FilePath,
    TextSpanInfo Span);

public enum FieldUsageKind
{
    Read,
    Write,
    ReadWrite
}
