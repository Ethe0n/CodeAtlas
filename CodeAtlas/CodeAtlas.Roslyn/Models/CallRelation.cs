namespace CodeAtlas.Roslyn.Models;

public sealed record CallRelation(
    string CallerMethodSymbolId,
    string CallerDisplayName,
    string CalleeMethodSymbolId,
    string CalleeDisplayName,
    bool IsProjectInternal,
    string? CalleeAssemblyName,
    string? FilePath,
    TextSpanInfo Span);
