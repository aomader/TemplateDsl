; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TDSL001 | TemplateDsl | Error | Template must be partial
TDSL002 | TemplateDsl | Error | Template must be a sealed class
TDSL003 | TemplateDsl | Error | Template must be a top-level type
TDSL004 | TemplateDsl | Error | Unsupported construct in Build
TDSL005 | TemplateDsl | Error | Template must implement exactly one template interface
TDSL006 | TemplateDsl | Error | Invalid format declaration
TDSL007 | TemplateDsl | Error | Template nodes can only be used in Build
TDSL008 | TemplateDsl | Error | A nested template needs a context its parent doesn't have
