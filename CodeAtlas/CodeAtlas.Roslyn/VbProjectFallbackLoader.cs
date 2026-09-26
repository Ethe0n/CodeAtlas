using System.Xml.Linq;
using CodeAtlas.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace CodeAtlas.Roslyn;

internal sealed class VbProjectFallbackLoader
{
    public async Task<FallbackProjectLoadResult> LoadAsync(
        string projectFilePath,
        CancellationToken cancellationToken)
    {
        var rootProjectPath = Path.GetFullPath(projectFilePath);
        var diagnostics = new List<ProjectAnalysisDiagnostic>();
        var specifications = new Dictionary<string, ProjectSpecification>(StringComparer.OrdinalIgnoreCase);

        LoadProjectSpecification(rootProjectPath, specifications, diagnostics);

        var workspace = new AdhocWorkspace();
        try
        {
            var solution = workspace.CurrentSolution;
            var projectIds = specifications.Keys.ToDictionary(
                path => path,
                _ => ProjectId.CreateNewId(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var specification in specifications.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var projectInfo = ProjectInfo.Create(
                    projectIds[specification.FilePath],
                    VersionStamp.Create(),
                    specification.Name,
                    specification.AssemblyName,
                    LanguageNames.VisualBasic,
                    filePath: specification.FilePath,
                    outputFilePath: null,
                    compilationOptions: CreateCompilationOptions(specification),
                    parseOptions: VisualBasicParseOptions.Default,
                    documents: null,
                    projectReferences: null,
                    metadataReferences: ResolveMetadataReferences(specification, diagnostics));

                solution = solution.AddProject(projectInfo);
            }

            foreach (var specification in specifications.Values)
            {
                var sourceProjectId = projectIds[specification.FilePath];
                foreach (var referencedProjectPath in specification.ProjectReferences)
                {
                    if (projectIds.TryGetValue(referencedProjectPath, out var targetProjectId))
                    {
                        solution = solution.AddProjectReference(
                            sourceProjectId,
                            new ProjectReference(targetProjectId));
                    }
                }

                foreach (var sourceFilePath in specification.SourceFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(sourceFilePath))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            ProjectAnalysisDiagnosticSeverity.Warning,
                            "FallbackLoad",
                            $"Source file was not found: {sourceFilePath}",
                            specification.FilePath));
                        continue;
                    }

                    var sourceText = SourceText.From(
                        await File.ReadAllTextAsync(sourceFilePath, cancellationToken).ConfigureAwait(false));
                    solution = solution.AddDocument(
                        DocumentId.CreateNewId(sourceProjectId),
                        Path.GetFileName(sourceFilePath),
                        sourceText,
                        filePath: sourceFilePath);
                }
            }

            if (!workspace.TryApplyChanges(solution))
            {
                throw new InvalidOperationException("Could not apply the fallback project graph to the Roslyn workspace.");
            }

            var rootProject = workspace.CurrentSolution.GetProject(projectIds[rootProjectPath])
                ?? throw new InvalidOperationException($"Fallback project was not created: {rootProjectPath}");

            return new FallbackProjectLoadResult(workspace, rootProject, diagnostics);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static void LoadProjectSpecification(
        string projectFilePath,
        IDictionary<string, ProjectSpecification> specifications,
        ICollection<ProjectAnalysisDiagnostic> diagnostics)
    {
        var fullProjectPath = Path.GetFullPath(projectFilePath);
        if (specifications.ContainsKey(fullProjectPath))
        {
            return;
        }

        if (!File.Exists(fullProjectPath))
        {
            throw new FileNotFoundException("Referenced project was not found.", fullProjectPath);
        }

        var document = XDocument.Load(fullProjectPath, LoadOptions.SetLineInfo);
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException($"Project directory was not found: {fullProjectPath}");

        var sourceFiles = Elements(document, "Compile")
            .Select(element => ResolvePath(projectDirectory, (string?)element.Attribute("Include")))
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var projectReferences = Elements(document, "ProjectReference")
            .Select(element => ResolvePath(projectDirectory, (string?)element.Attribute("Include")))
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var assemblyReferences = Elements(document, "Reference")
            .Select(element => new AssemblyReferenceSpecification(
                GetSimpleAssemblyName((string?)element.Attribute("Include")),
                ResolvePath(projectDirectory, ChildValue(element, "HintPath"))))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Name))
            .ToArray();

        var imports = Elements(document, "Import")
            .Where(element => element.Attribute("Include") is not null)
            .Select(element => (string)element.Attribute("Include")!)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var name = PropertyValue(document, "AssemblyName")
            ?? Path.GetFileNameWithoutExtension(fullProjectPath);
        var specification = new ProjectSpecification(
            fullProjectPath,
            name,
            name,
            PropertyValue(document, "RootNamespace") ?? string.Empty,
            PropertyValue(document, "TargetFrameworkVersion") ?? "v4.5.2",
            PropertyValue(document, "OutputType") ?? "Library",
            sourceFiles,
            projectReferences,
            assemblyReferences,
            imports);

        specifications.Add(fullProjectPath, specification);

        foreach (var comReference in Elements(document, "COMReference"))
        {
            var referenceName = (string?)comReference.Attribute("Include") ?? "(unnamed COM reference)";
            diagnostics.Add(CreateDiagnostic(
                ProjectAnalysisDiagnosticSeverity.Warning,
                "FallbackLoad",
                $"COM reference was skipped: {referenceName}",
                fullProjectPath));
        }

        foreach (var referencedProjectPath in projectReferences)
        {
            if (!File.Exists(referencedProjectPath))
            {
                diagnostics.Add(CreateDiagnostic(
                    ProjectAnalysisDiagnosticSeverity.Warning,
                    "FallbackLoad",
                    $"Referenced project was not found: {referencedProjectPath}",
                    fullProjectPath));
                continue;
            }

            LoadProjectSpecification(referencedProjectPath, specifications, diagnostics);
        }
    }

    private static VisualBasicCompilationOptions CreateCompilationOptions(ProjectSpecification specification)
    {
        var outputKind = specification.OutputType.ToUpperInvariant() switch
        {
            "EXE" => OutputKind.ConsoleApplication,
            "WINEXE" => OutputKind.WindowsApplication,
            _ => OutputKind.DynamicallyLinkedLibrary
        };

        var globalImports = specification.Imports
            .Select(GlobalImport.Parse)
            .ToArray();

        return new VisualBasicCompilationOptions(
            outputKind,
            rootNamespace: specification.RootNamespace,
            globalImports: globalImports);
    }

    private static IReadOnlyList<MetadataReference> ResolveMetadataReferences(
        ProjectSpecification specification,
        ICollection<ProjectAnalysisDiagnostic> diagnostics)
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        var frameworkDirectory = GetFrameworkReferenceDirectory(specification.TargetFrameworkVersion);

        if (frameworkDirectory is null)
        {
            diagnostics.Add(CreateDiagnostic(
                ProjectAnalysisDiagnosticSeverity.Error,
                "FallbackLoad",
                $".NET Framework reference assemblies were not found for {specification.TargetFrameworkVersion}.",
                specification.FilePath));
        }
        else
        {
            foreach (var assemblyPath in Directory.EnumerateFiles(frameworkDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                AddMetadataReference(references, assemblyPath);
            }
        }

        foreach (var reference in specification.AssemblyReferences)
        {
            var assemblyPath = reference.HintPath;
            if (assemblyPath is null && frameworkDirectory is not null)
            {
                assemblyPath = Path.Combine(frameworkDirectory, $"{reference.Name}.dll");
            }

            if (assemblyPath is not null && File.Exists(assemblyPath))
            {
                AddMetadataReference(references, assemblyPath);
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                ProjectAnalysisDiagnosticSeverity.Warning,
                "FallbackLoad",
                $"Assembly reference was not found: {reference.Name}",
                specification.FilePath));
        }

        return references.Values.ToArray();
    }

    private static void AddMetadataReference(
        IDictionary<string, MetadataReference> references,
        string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        references.TryAdd(fullPath, MetadataReference.CreateFromFile(fullPath));
    }

    private static string? GetFrameworkReferenceDirectory(string targetFrameworkVersion)
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var frameworkDirectory = Path.Combine(
            programFilesX86,
            "Reference Assemblies",
            "Microsoft",
            "Framework",
            ".NETFramework",
            targetFrameworkVersion);

        return Directory.Exists(frameworkDirectory)
            ? frameworkDirectory
            : null;
    }

    private static IEnumerable<XElement> Elements(XDocument document, string localName)
    {
        return document.Descendants().Where(element => element.Name.LocalName == localName);
    }

    private static string? PropertyValue(XDocument document, string localName)
    {
        return Elements(document, localName)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);
    }

    private static string? ChildValue(XElement element, string localName)
    {
        return element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == localName)
            ?.Value
            .Trim();
    }

    private static string? ResolvePath(string projectDirectory, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['*', '?']) >= 0)
        {
            return null;
        }

        var normalizedValue = value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(projectDirectory, normalizedValue));
    }

    private static string GetSimpleAssemblyName(string? include)
    {
        return include?.Split(',')[0].Trim() ?? string.Empty;
    }

    private static ProjectAnalysisDiagnostic CreateDiagnostic(
        ProjectAnalysisDiagnosticSeverity severity,
        string stage,
        string message,
        string projectFilePath)
    {
        return new ProjectAnalysisDiagnostic(severity, stage, message, projectFilePath);
    }

    private sealed record ProjectSpecification(
        string FilePath,
        string Name,
        string AssemblyName,
        string RootNamespace,
        string TargetFrameworkVersion,
        string OutputType,
        IReadOnlyList<string> SourceFiles,
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<AssemblyReferenceSpecification> AssemblyReferences,
        IReadOnlyList<string> Imports);

    private sealed record AssemblyReferenceSpecification(string Name, string? HintPath);
}

internal sealed class FallbackProjectLoadResult(
    AdhocWorkspace workspace,
    Project project,
    IReadOnlyList<ProjectAnalysisDiagnostic> diagnostics) : IDisposable
{
    public Project Project { get; } = project;

    public IReadOnlyList<ProjectAnalysisDiagnostic> Diagnostics { get; } = diagnostics;

    public void Dispose()
    {
        workspace.Dispose();
    }
}
