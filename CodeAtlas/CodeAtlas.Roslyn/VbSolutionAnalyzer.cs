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
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
        {
            return new ProjectStructure(
                project.Id.Id.ToString(),
                project.Name,
                project.FilePath,
                project.Language,
                Array.Empty<TypeStructure>(),
                Array.Empty<CallRelation>(),
                Array.Empty<ControlFlowInfo>());
        }

        var types = new List<TypeStructure>();
        var calls = new List<CallRelation>();
        var controlFlows = new List<ControlFlowInfo>();
        var projectDirectory = project.FilePath is null
            ? null
            : Path.GetDirectoryName(project.FilePath);

        foreach (var document in project.Documents.Where(IsVisualBasicDocument))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                continue;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
            {
                continue;
            }

            types.AddRange(_structureExtractor.ExtractTypes(root, semanticModel, document.FilePath, projectDirectory));
            calls.AddRange(_structureExtractor.ExtractCalls(root, semanticModel, compilation, document.FilePath, projectDirectory));
            controlFlows.AddRange(_structureExtractor.ExtractControlFlows(root, semanticModel, document.FilePath));
        }

        var mergedTypes = MergePartialTypes(types);
        var generatedMethodIds = mergedTypes
            .SelectMany(type => type.GeneratedMethods)
            .Select(method => method.SymbolId)
            .ToHashSet(StringComparer.Ordinal);

        var filteredCalls = calls
            .Where(call =>
                !generatedMethodIds.Contains(call.CallerMethodSymbolId) &&
                !generatedMethodIds.Contains(call.CalleeMethodSymbolId))
            .ToArray();

        return new ProjectStructure(
            project.Id.Id.ToString(),
            project.Name,
            project.FilePath,
            project.Language,
            mergedTypes,
            filteredCalls,
            controlFlows
                .Where(flow => !generatedMethodIds.Contains(flow.MethodId))
                .OrderBy(flow => flow.MethodName, StringComparer.Ordinal)
                .ToArray());
    }

    private static IReadOnlyList<TypeStructure> MergePartialTypes(IEnumerable<TypeStructure> types)
    {
        return types
            .GroupBy(type => type.SymbolId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var filePaths = group
                    .SelectMany(type => type.FilePaths)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var methods = group
                    .SelectMany(type => type.Methods)
                    .OrderBy(method => method.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(method => method.Span.StartLine)
                    .ThenBy(method => method.Span.StartColumn)
                    .ToArray();

                var generatedMethods = group
                    .SelectMany(type => type.GeneratedMethods)
                    .OrderBy(method => method.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(method => method.Span.StartLine)
                    .ThenBy(method => method.Span.StartColumn)
                    .ToArray();

                var fields = group
                    .SelectMany(type => type.Fields)
                    .OrderBy(field => field.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(field => field.Span.StartLine)
                    .ThenBy(field => field.Span.StartColumn)
                    .ToArray();

                var properties = group
                    .SelectMany(type => type.Properties)
                    .OrderBy(property => property.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(property => property.Span.StartLine)
                    .ThenBy(property => property.Span.StartColumn)
                    .ToArray();

                var uiControls = group
                    .SelectMany(type => type.UiControls)
                    .DistinctBy(control => $"{control.DeclaringFile}|{control.Name}", StringComparer.OrdinalIgnoreCase)
                    .OrderBy(control => control.DeclaringFile, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(control => control.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var uiEventHandlers = group
                    .SelectMany(type => type.UiEventHandlers)
                    .DistinctBy(handler => $"{handler.DeclaringFile}|{handler.ControlName}|{handler.EventName}|{handler.HandlerMethodSymbolId}", StringComparer.OrdinalIgnoreCase)
                    .OrderBy(handler => handler.DeclaringFile, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(handler => handler.ControlName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(handler => handler.EventName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return first with
                {
                    FilePaths = filePaths,
                    Fields = fields,
                    Properties = properties,
                    Methods = methods,
                    GeneratedMethods = generatedMethods,
                    UiControls = uiControls,
                    UiEventHandlers = uiEventHandlers
                };
            })
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
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
