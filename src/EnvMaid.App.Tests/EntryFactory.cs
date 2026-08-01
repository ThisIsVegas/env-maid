using EnvMaid.App.Models;

namespace EnvMaid.App.Tests;

/// <summary>
/// Builds entries carrying specific diagnostics, so ViewModel tests can set up a state without
/// running the analysis service against a real filesystem.
/// </summary>
internal static class EntryFactory
{
    public static PathEntry With(string rawToken, PathScope scope, params DiagnosticKind[] kinds)
    {
        var entry = new PathEntry(rawToken, scope);
        foreach (var kind in kinds)
            entry.Diagnostics.Add(new Diagnostic(kind, SeverityFor(kind), kind.ToString()));
        return entry;
    }

    public static PathEntry Missing(string rawToken, PathScope scope) =>
        With(rawToken, scope, DiagnosticKind.FolderMissing);

    public static PathEntry Empty(PathScope scope) =>
        With(string.Empty, scope, DiagnosticKind.EmptyToken);

    public static PathEntry Duplicate(string rawToken, PathScope scope) =>
        With(rawToken, scope, DiagnosticKind.DuplicateL1);

    private static Severity SeverityFor(DiagnosticKind kind) => kind switch
    {
        DiagnosticKind.FolderMissing => Severity.Error,
        DiagnosticKind.EmptyToken or DiagnosticKind.UnresolvedVariable => Severity.Warning,
        _ when kind.ToString().StartsWith("Duplicate", StringComparison.Ordinal) => Severity.Warning,
        _ => Severity.Info,
    };
}
