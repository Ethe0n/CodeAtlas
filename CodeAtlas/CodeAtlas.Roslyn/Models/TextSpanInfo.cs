namespace CodeAtlas.Roslyn.Models;

public sealed record TextSpanInfo(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
