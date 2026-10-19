# CodeAtlas.Lint v1

Model-based diagnostics for VB.NET / CATIA automation projects. This library
consumes an existing `ProjectStructure` (or `SolutionStructure` for explicit batch callers); it never opens, compiles, executes, or
rewrites the target solution. `CodeAtlas.Roslyn` does not reference this project.
The WinForms UI runs `AnalyzeProject` off the UI thread only when Diagnostics is
opened for a selected project. Selecting a type, method, field, or group uses its
owning project. Unselected projects are not linted. Results are cached by
ProjectId for the current loaded solution, never mixed between projects, and
cleared on solution replacement. Switching projects cancels an unfinished lint
task; a late result cannot overwrite the newly selected project's rows.

## Rules

| ID | Level | Finding | Input |
| --- | --- | --- | --- |
| CAL0001 | Warning | Partial/failed analysis or missing method CFG input | Project status, methods, CFG IDs |
| CAL1001 | Warning | On Error Resume Next needs review | Existing error-handling operation role and text |
| CAL1002 | Info | Broad Catch needs review | Raw CFG catch regions and exception types |
| CAL2001 | Warning | BinaryFormatter deserialization API | External call target documentation ID |
| CAL2002 | Info | Process.Start / VB Shell needs review | External call target documentation ID |
| CAL2003 | Info | ReleaseComObject / FinalReleaseComObject needs ownership review | External call target documentation ID |
| CAL3001 | Info | Public/protected mutable Shared field | Existing field symbol ID and metadata |
| CAL3002 | Info | More than 12 conditional BasicBlocks | Distinct branch-source node IDs |

`LintOptions.MaxConditionalBlocks` configures CAL3002. That count includes lowered
compiler conditions and is not cyclomatic complexity. The UI currently uses the
default threshold.

## Identity And Results

Results carry rule, severity, category, assessment, message, recommendation,
ProjectId, SymbolId, owner Type SymbolId, original file/line span, and a reference
link. The UI navigates by ProjectId plus SymbolId; display names are not identity.
Catch locations refer to the first available source location in the catch region,
not necessarily the Catch keyword itself.

Diagnostic titles, explanations, and recommendations are provided in Korean.
The UI also uses Korean severity/category labels, filters, and status messages;
rule IDs, enum values, SymbolIds, file paths, and API names remain unchanged.

Generated methods/fields are excluded using existing flags. Identical repeated
records at the same source site are deduplicated, but separate call sites remain
separate findings. Raw input models and CFG topology are not changed.

## Interpretation And Limits

- A detected pattern is not proof that a vulnerability can be exploited.
- Process execution and manual COM release can be intentional. These produce
  manual-review findings, not command-injection or use-after-release verdicts.
- On Error Resume Next can be intentional with immediate Err checks. CAL1001
  does not analyze those checks or the effective error-handling state.
- CAL1002 reports a broad catch pattern even if it logs or rethrows; it does not
  claim the exception is swallowed or implement Microsoft's CA1031 in full.
- ReadOnly, Const, private, generated, and event fields are not CAL3001 findings.
- Partial/failed projects and absent method CFGs produce coverage warnings.
  No findings means only that these v1 rules found no matching input patterns.
- Coverage is limited to methods/calls/fields/CFGs present in the current models.
  Constructors, property accessors, lambdas, late-bound COM calls, and other
  unsupported analysis inputs are not newly analyzed by the linter.
- No taint/data-flow analysis, Nothing-safety proof, CATIA lifetime proof, package
  CVE scan, secret detection, automatic fixes, AI, or target code execution.

Rules follow the existing semantic SymbolIds rather than searching source text.
The Resume Next rule matches only text already classified by Roslyn as an
error-handling directive; it does not scan comments, string literals, or files.

## Checks

```powershell
dotnet run --project Tests/CodeAtlas.LintChecks -- C:\workspace\CodeAtlas
```

The runner verifies each rule, negative cases, generated-code filtering,
overloads, same symbols in different projects, cancellation, deterministic
results, duplicate sites, real Roslyn-extracted API calls, and the existing
sample_project. Test security API calls are analyzed statically, never invoked.
Running the sample analyzer explicitly trusts this repository's sample only.

UI/graph/security regressions remain in `Tests/CodeAtlas.SecurityChecks`.

## References

- [VB On Error](https://learn.microsoft.com/dotnet/visual-basic/language-reference/statements/on-error-statement)
- [General exception handling](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1031)
- [BinaryFormatter security](https://learn.microsoft.com/dotnet/standard/serialization/binaryformatter-security-guide)
- [Manual COM release](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.releasecomobject)
- [Visible mutable static fields](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2211)
