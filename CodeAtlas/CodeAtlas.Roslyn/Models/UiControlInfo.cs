namespace CodeAtlas.Roslyn.Models;

public sealed record UiControlInfo(
    string Name,
    string Type,
    string? DeclaringFile)
{
    public UiControlBounds? Bounds { get; init; }

    public string? DisplayText { get; init; }

    public string? ParentName { get; init; }

    public int? TabIndex { get; init; }
}
