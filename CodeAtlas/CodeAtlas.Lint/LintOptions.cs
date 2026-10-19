namespace CodeAtlas.Lint;

public sealed record LintOptions
{
    public int MaxConditionalBlocks { get; init; } = 12;
}
