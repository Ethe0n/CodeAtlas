# Security Boundaries

## Trusted Solutions Only

Opening a project with MSBuild can execute project, imported target, referenced
project, and package build logic with the current user's permissions. CodeAtlas
is not a sandbox. Review the entire input and its dependencies first.

- The UI asks for trust on each open; the default answer is No.
- The library rejects analysis unless `trustProjects: true` is explicitly passed.
- The console application requires `--trust-projects`.
- The source fallback is recovery from load failures, not a security boundary:
  MSBuild has already attempted to load the project before fallback runs.

Keep the locally installed .NET SDK / Visual Studio / MSBuild updated as well as
NuGet packages. MSBuildLocator uses the installed engine; updating a package does
not patch an installed SDK.

## Graph Files

MSAGL's toolbar Open action is intercepted in all four graph views. CodeAtlas
does not import arbitrary MSAGL graph files or their serialized UserData.
The existing zoom, pan, print, and CodeAtlas export features remain available.

## Resource Limits

`AnalysisResourceLimits` allows library callers to configure the analysis budget.
Defaults per loaded project/reference graph:

| Resource | Limit |
| --- | --- |
| Solution file | 16 MiB |
| Project XML file | 4 MiB |
| Projects | 512 |
| Fallback project-reference depth | 64 |
| Source documents | 20,000 |
| Source file | 8 MiB |
| Total source bytes | 256 MiB |
| Project analysis | 5 minutes, cooperative cancellation |

Fallback discovery is iterative, checks cancellation while traversing references,
and uses bounded reads. DTDs and external XML entities are prohibited. Exceeding
a project budget is reported as a project failure; other projects can continue.
MSBuild's own loading/evaluation and Roslyn cancellation remain cooperative:
these limits are not an OS memory quota or a hostile-code sandbox.

Graph construction and layout run in hidden, disposable processes, with at most
two concurrent workers. New selections and the Cancel layout button cancel the
old worker. The parent terminates workers on cancellation, a 30-second deadline,
or a monitored working set above 768 MiB. The memory guard samples every 100 ms;
it is not an instantaneous OS-enforced allocation ceiling.

Graph safety budgets are centralized in `GraphLayoutWorker`: 2,000 nodes,
10,000 edges, 200,000 input relations, 2,000,000 node-label characters,
16 MiB request, and 32 MiB response. Exceeding a budget displays an error; it
never silently truncates or rewires the graph.

The worker reuses the existing graph builders/styles/routing. Only precomputed
drawing geometry and SymbolId navigation mappings return to the UI. UserData is
removed, and the XML is checked for serialized UserData, DTDs, byte size, and
depth before MSAGL reads it. The UI does not perform layout again when displaying
the result. Auto-fit runs once for the matching completed graph.

## Verification

The dependency-free regression runner covers trust denial before MSBuild
registration, fallback limits/cancellation/XML handling, toolbar import blocking,
worker cancellation/timeout/memory limits, stale selections, graph topology and
styles, navigation maps, and export. It explicitly trusts only the repository's
sample input; do not point it at an unreviewed checkout.

```powershell
dotnet build CodeAtlas/CodeAtlas.sln
dotnet build Tests/CodeAtlas.SecurityChecks/CodeAtlas.SecurityChecks.csproj -p:UseAppHost=false -p:OutDir=C:\workspace\CodeAtlas\artifacts\security-checks\
dotnet artifacts/security-checks/CodeAtlas.SecurityChecks.dll C:\workspace\CodeAtlas
dotnet list CodeAtlas/CodeAtlas.sln package --vulnerable --include-transitive
```

For a trusted solution through the console:

```powershell
dotnet run --project CodeAtlas/CodeAtlas.App -- sample_project/sample_project.sln --trust-projects
```
