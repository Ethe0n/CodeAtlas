using CodeAtlas.Roslyn.Models;
using Microsoft.Build.Locator;
using Microsoft.Build.Construction;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Runtime.CompilerServices;

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

        var solutionProjects = ReadSolutionProjects(fullSolutionPath);
        var projects = new List<ProjectStructure>();
        foreach (var solutionProject in solutionProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            projects.Add(await AnalyzeProjectFileAsync(solutionProject, cancellationToken).ConfigureAwait(false));
        }

        return new SolutionStructure(fullSolutionPath, projects);
    }

    private async Task<ProjectStructure> AnalyzeProjectFileAsync(
        SolutionProjectDescriptor solutionProject,
        CancellationToken cancellationToken)
    {
        var monitor = new ProjectLoadMonitor(solutionProject.FilePath);

        using (var workspace = MSBuildWorkspace.Create())
        {
            workspace.WorkspaceFailed += (_, args) => monitor.ReportWorkspaceDiagnostic(args.Diagnostic);

            Project? project = null;
            try
            {
                project = await workspace.OpenProjectAsync(
                    solutionProject.FilePath,
                    monitor,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                monitor.ReportException("MSBuildLoad", exception);
            }

            if (project is not null && !monitor.HasFailures)
            {
                try
                {
                    var analyzedProject = await AnalyzeProjectAsync(project, cancellationToken).ConfigureAwait(false);
                    return analyzedProject with
                    {
                        AnalysisStatus = ProjectAnalysisStatus.Full,
                        Diagnostics = monitor.GetDiagnostics()
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    monitor.ReportException("ProjectAnalysis", exception);
                }
            }
        }

        return await AnalyzeFallbackProjectAsync(
            solutionProject,
            monitor.GetDiagnostics(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProjectStructure> AnalyzeFallbackProjectAsync(
        SolutionProjectDescriptor solutionProject,
        IReadOnlyList<ProjectAnalysisDiagnostic> loadDiagnostics,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ProjectAnalysisDiagnostic>(loadDiagnostics);

        try
        {
            var loader = new VbProjectFallbackLoader();
            using var fallbackProject = await loader.LoadAsync(
                solutionProject.FilePath,
                cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(fallbackProject.Diagnostics);

            var analyzedProject = await AnalyzeProjectAsync(
                fallbackProject.Project,
                cancellationToken).ConfigureAwait(false);
            return analyzedProject with
            {
                Id = solutionProject.Id,
                Name = solutionProject.Name,
                FilePath = solutionProject.FilePath,
                AnalysisStatus = ProjectAnalysisStatus.Partial,
                Diagnostics = diagnostics.ToArray()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Add(new ProjectAnalysisDiagnostic(
                ProjectAnalysisDiagnosticSeverity.Error,
                "FallbackLoad",
                GetExceptionMessages(exception),
                solutionProject.FilePath));

            return CreateFailedProject(solutionProject, diagnostics);
        }
    }

    private static ProjectStructure CreateFailedProject(
        SolutionProjectDescriptor solutionProject,
        IReadOnlyList<ProjectAnalysisDiagnostic> diagnostics)
    {
        return new ProjectStructure(
            solutionProject.Id,
            solutionProject.Name,
            solutionProject.FilePath,
            LanguageNames.VisualBasic,
            Array.Empty<TypeStructure>(),
            Array.Empty<CallRelation>(),
            Array.Empty<FieldUsageRelation>(),
            Array.Empty<TypeDependencyRelation>(),
            Array.Empty<ControlFlowInfo>())
        {
            AnalysisStatus = ProjectAnalysisStatus.Failed,
            Diagnostics = diagnostics
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<SolutionProjectDescriptor> ReadSolutionProjects(string solutionFilePath)
    {
        return SolutionFile.Parse(solutionFilePath)
            .ProjectsInOrder
            .Where(project => string.Equals(
                Path.GetExtension(project.AbsolutePath),
                ".vbproj",
                StringComparison.OrdinalIgnoreCase))
            .Select(project => new SolutionProjectDescriptor(
                project.ProjectGuid,
                project.ProjectName,
                Path.GetFullPath(project.AbsolutePath)))
            .ToArray();
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
                Array.Empty<FieldUsageRelation>(),
                Array.Empty<TypeDependencyRelation>(),
                Array.Empty<ControlFlowInfo>());
        }

        var types = new List<TypeStructure>();
        var calls = new List<CallRelation>();
        var fieldUsages = new List<FieldUsageRelation>();
        var typeDependencies = new List<TypeDependencyRelation>();
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
            fieldUsages.AddRange(_structureExtractor.ExtractFieldUsages(root, semanticModel, document.FilePath, projectDirectory));
            typeDependencies.AddRange(_structureExtractor.ExtractTypeDependencies(root, semanticModel, document.FilePath, projectDirectory));
            controlFlows.AddRange(_structureExtractor.ExtractControlFlows(root, semanticModel, document.FilePath));
        }

        var mergedTypes = MergePartialTypes(types);
        var internalTypeIds = mergedTypes
            .Select(type => type.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        var generatedMethodIds = mergedTypes
            .SelectMany(type => type.GeneratedMethods)
            .Select(method => method.SymbolId)
            .ToHashSet(StringComparer.Ordinal);

        var filteredCalls = calls
            .Where(call =>
                !generatedMethodIds.Contains(call.CallerMethodSymbolId) &&
                !generatedMethodIds.Contains(call.CalleeMethodSymbolId))
            .ToArray();

        var filteredFieldUsages = fieldUsages
            .Where(usage => !generatedMethodIds.Contains(usage.MethodSymbolId))
            .ToArray();

        var filteredTypeDependencies = typeDependencies
            .Where(dependency =>
                internalTypeIds.Contains(dependency.SourceTypeSymbolId) &&
                internalTypeIds.Contains(dependency.TargetTypeSymbolId) &&
                !string.Equals(dependency.SourceTypeSymbolId, dependency.TargetTypeSymbolId, StringComparison.Ordinal))
            .ToArray();

        return new ProjectStructure(
            project.Id.Id.ToString(),
            project.Name,
            project.FilePath,
            project.Language,
            mergedTypes,
            filteredCalls,
            filteredFieldUsages,
            filteredTypeDependencies,
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

    private static string GetExceptionMessages(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                messages.Add(current.Message);
            }
        }

        return string.Join(" -> ", messages.Distinct(StringComparer.Ordinal));
    }

    private sealed record SolutionProjectDescriptor(string Id, string Name, string FilePath);

    private sealed class ProjectLoadMonitor(string projectFilePath) : IProgress<ProjectLoadProgress>
    {
        private readonly object _syncRoot = new();
        private readonly List<ProjectAnalysisDiagnostic> _diagnostics = new();
        private string _stage = "MSBuildLoad";
        private string _currentProjectFilePath = projectFilePath;
        private bool _hasFailures;

        public bool HasFailures
        {
            get
            {
                lock (_syncRoot)
                {
                    return _hasFailures;
                }
            }
        }

        public void Report(ProjectLoadProgress value)
        {
            lock (_syncRoot)
            {
                _stage = value.Operation.ToString();
                if (!string.IsNullOrWhiteSpace(value.FilePath))
                {
                    _currentProjectFilePath = value.FilePath;
                }
            }
        }

        public void ReportWorkspaceDiagnostic(WorkspaceDiagnostic diagnostic)
        {
            lock (_syncRoot)
            {
                var severity = diagnostic.Kind == WorkspaceDiagnosticKind.Failure
                    ? ProjectAnalysisDiagnosticSeverity.Error
                    : ProjectAnalysisDiagnosticSeverity.Warning;
                _hasFailures |= severity == ProjectAnalysisDiagnosticSeverity.Error;
                _diagnostics.Add(new ProjectAnalysisDiagnostic(
                    severity,
                    _stage,
                    diagnostic.Message,
                    _currentProjectFilePath));
            }
        }

        public void ReportException(string stage, Exception exception)
        {
            lock (_syncRoot)
            {
                _hasFailures = true;
                _diagnostics.Add(new ProjectAnalysisDiagnostic(
                    ProjectAnalysisDiagnosticSeverity.Error,
                    stage,
                    GetExceptionMessages(exception),
                    _currentProjectFilePath));
            }
        }

        public IReadOnlyList<ProjectAnalysisDiagnostic> GetDiagnostics()
        {
            lock (_syncRoot)
            {
                return _diagnostics
                    .Distinct()
                    .ToArray();
            }
        }
    }
}
