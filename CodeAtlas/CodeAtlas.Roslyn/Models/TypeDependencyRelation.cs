namespace CodeAtlas.Roslyn.Models;

public sealed record TypeDependencyRelation(
    string SourceTypeSymbolId,
    string TargetTypeSymbolId,
    TypeDependencyKind Kind,
    string? FilePath,
    TextSpanInfo Span);

public enum TypeDependencyKind
{
    FieldType,
    PropertyType,
    ParameterType,
    ReturnType,
    ObjectCreation,
    MethodCall
}
