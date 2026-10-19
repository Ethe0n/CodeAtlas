using System.Text;

namespace CodeAtlas.Roslyn;

public sealed record AnalysisResourceLimits
{
    public int MaxProjects { get; init; } = 512;
    public int MaxProjectReferenceDepth { get; init; } = 64;
    public int MaxSourceFiles { get; init; } = 20_000;
    public long MaxSolutionBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxProjectBytes { get; init; } = 4 * 1024 * 1024;
    public int MaxSourceFileBytes { get; init; } = 8 * 1024 * 1024;
    public long MaxTotalSourceBytes { get; init; } = 256L * 1024 * 1024;
    public TimeSpan ProjectTimeout { get; init; } = TimeSpan.FromMinutes(5);

    internal void Validate()
    {
        if (MaxProjects <= 0 || MaxProjectReferenceDepth <= 0 || MaxSourceFiles <= 0 ||
            MaxSolutionBytes <= 0 || MaxProjectBytes <= 0 || MaxSourceFileBytes <= 0 ||
            MaxTotalSourceBytes <= 0 || ProjectTimeout <= TimeSpan.Zero ||
            ProjectTimeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(AnalysisResourceLimits), "Analysis limits must be positive and finite.");
        }
    }

    internal static long CheckFileSize(string path, long maxBytes)
    {
        var length = new FileInfo(path).Length;
        if (length > maxBytes)
        {
            throw new InvalidDataException($"Input exceeds the {maxBytes:N0}-byte safety limit: {path}");
        }

        return length;
    }

    internal static async Task<(string Text, long Bytes)> ReadLimitedTextAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length > maxBytes)
        {
            throw new InvalidDataException($"Input exceeds the {maxBytes:N0}-byte safety limit: {path}");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await file.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException($"Input grew beyond its safety limit: {path}");
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return (await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false), buffer.Length);
    }
}
