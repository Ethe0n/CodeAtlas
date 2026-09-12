namespace CodeAtlas.Roslyn.Models;

public sealed record UiControlInfo(
    string Name,
    string Type,
    string? DeclaringFile);
