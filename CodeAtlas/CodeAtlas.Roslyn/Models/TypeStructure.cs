namespace CodeAtlas.Roslyn.Models;

public sealed record TypeStructure(
    string Name,
    string FullName,
    string SymbolId,
    string? Namespace,
    string? ContainingTypeSymbolId,
    string Kind,
    string Accessibility,
    string? BaseType,
    IReadOnlyList<string> FilePaths,
    TextSpanInfo Span,
    IReadOnlyList<FieldStructure> Fields,
    IReadOnlyList<PropertyStructure> Properties,
    IReadOnlyList<MethodStructure> Methods,
    IReadOnlyList<MethodStructure> GeneratedMethods,
    IReadOnlyList<UiControlInfo> UiControls,
    IReadOnlyList<UiEventHandlerInfo> UiEventHandlers);
