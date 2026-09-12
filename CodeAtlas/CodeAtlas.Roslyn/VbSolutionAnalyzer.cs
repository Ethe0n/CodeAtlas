using CodeAtlas.Roslyn.Models;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeAtlas.Roslyn;

public sealed class VbSolutionAnalyzer
{
    private readonly VbSyntaxStructureExtractor _structureExtractor;

    public VbSolutionAnalyzer()
        : this(new VbSyntaxStructureExtractor())
    {
    }

    public VbSolutionAnalyzer(VbSyntaxStructureExtractor structureExtractor)
    {
        _structureExtractor = structureExtractor;
    }

    public async Task<SolutionStructure> AnalyzeAsync(
        string solutionFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(solutionFilePath))
        {
            throw new ArgumentException("Solution file path is required.", nameof(solutionFilePath));
        }

        var fullSolutionPath = Path.GetFullPath(solutionFilePath);
        if (!File.Exists(fullSolutionPath))
        {
            throw new FileNotFoundException("Solution file was not found.", fullSolutionPath);
        }

        EnsureMSBuildRegistered();

        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(
            fullSolutionPath,
            progress: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var projects = new List<ProjectStructure>();
        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.VisualBasic))
        {
            projects.Add(await AnalyzeProjectAsync(project, cancellationToken).ConfigureAwait(false));
        }

        return new SolutionStructure(fullSolutionPath, projects);
    }

    private async Task<ProjectStructure> AnalyzeProjectAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var types = new List<TypeStructure>();
        foreach (var document in project.Documents.Where(IsVisualBasicDocument))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                continue;
            }

            types.AddRange(_structureExtractor.Extract(root, document.FilePath));
        }

        return new ProjectStructure(
            project.Id.Id.ToString(),
            project.Name,
            project.FilePath,
            project.Language,
            types);
    }

    private static bool IsVisualBasicDocument(Document document)
    {
        return string.Equals(Path.GetExtension(document.FilePath), ".vb", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureMSBuildRegistered()
    {
        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
        if (instances.Length > 0)
        {
            MSBuildLocator.RegisterInstance(instances[0]);
            return;
        }

        var dotnetSdkPath = TryFindDotNetSdkPath();
        if (dotnetSdkPath is not null)
        {
            MSBuildLocator.RegisterMSBuildPath(dotnetSdkPath);
            return;
        }

        MSBuildLocator.RegisterDefaults();
    }

    private static string? TryFindDotNetSdkPath()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            dotnetRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet");
        }

        var sdkRoot = Path.Combine(dotnetRoot, "sdk");
        if (!Directory.Exists(sdkRoot))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(sdkRoot)
            .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
            .Where(sdk => sdk.Version is not null && File.Exists(Path.Combine(sdk.Path, "MSBuild.dll")))
            .OrderByDescending(sdk => sdk.Version)
            .FirstOrDefault()
            ?.Path;
    }

    private static Version? ParseVersion(string value)
    {
        return Version.TryParse(value, out var version)
            ? version
            : null;
    }
}
