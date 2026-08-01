// PROTOTYPE — §9.1 entry data model sketch. Throwaway; not for the repo.
// Goal: prove the five-field model composes with WPF binding, and that the
// four downstream tickets (dupes / quotes / %VAR% / ambiguous) all read from it.

using System.Collections.ObjectModel;

namespace Sketch;

public enum PathScope { User, System }

public enum ExistenceStatus { Unknown, Exists, Missing, Inaccessible }

// §9.1 Diagnostics: a LIST of warnings, replacing one Reason string + one flag pair.
public enum DiagnosticKind
{
    // parse-time (from RawToken)
    SurroundingWhitespace,   // #12
    SurroundingQuotes,       // #12
    EmptyToken,              // #12
    // validate-time (from ExpandedValue)
    UnresolvedVariable,      // #13  <- today reported as "folder does not exist"
    FolderMissing,
    FolderInaccessible,
    NoExecutables,
    ExceedsMaxPath,          // #22
    // structural (§9.2)
    StructurallyAmbiguous,   // #22  one token -> N effective directories
    // cross-entry
    DuplicateL1, DuplicateL2, DuplicateL3, DuplicateL4,   // #11
}

public enum Severity { Info, Warning, Error }

public record Diagnostic(DiagnosticKind Kind, Severity Severity, string Message)
{
    // THE KEY PROPERTY. Today: IsChecked = (Confidence == High), so an L3 duplicate,
    // a quoted entry, and an unresolved %VAR% all arrive PRE-CHECKED FOR DELETION.
    // Safe-to-autoselect is a property of the diagnostic, not of a confidence rank.
    public bool SafeToAutoSelect => Kind is
        DiagnosticKind.EmptyToken or
        DiagnosticKind.FolderMissing or
        DiagnosticKind.DuplicateL1 or
        DiagnosticKind.DuplicateL2;
    // NOT in the list, deliberately:
    //   DuplicateL3          collapses two independently-maintained refs (#11)
    //   DuplicateL4          advisory only, never auto-remove (§9.3)
    //   UnresolvedVariable   fix is "define the var", not "delete the entry" (#13)
    //   SurroundingQuotes    a legitimate entry, just quoted (#12)
    //   StructurallyAmbiguous never auto-edit (§9.2)
    //   FolderInaccessible   may exist and be readable by another process
}

public sealed class PathEntrySketch
{
    // ---- STORED: the only authoritative field ----------------------------
    private string _rawToken;

    // ---- DERIVED: recomputed whenever RawToken changes --------------------
    public string ParsedValue { get; private set; } = "";
    public string ExpandedValue { get; private set; } = "";
    public string ComparisonKey { get; private set; } = "";

    public ExistenceStatus ExistenceStatus { get; set; } = ExistenceStatus.Unknown;
    public PathScope SourceScope { get; }
    public int OriginalIndex { get; }
    public ObservableCollection<Diagnostic> Diagnostics { get; } = new();

    // Q2: what replaces the Path setter. RawToken is the single write point;
    // every derived field recomputes here, so no caller can set them inconsistently.
    public string RawToken
    {
        get => _rawToken;
        set { _rawToken = value; Recompute(); /* + OnPropertyChanged for all four */ }
    }

    public PathEntrySketch(string rawToken, PathScope scope, int originalIndex)
    {
        _rawToken = rawToken;
        SourceScope = scope;
        OriginalIndex = originalIndex;
        Recompute();
    }

    private void Recompute()
    {
        ParsedValue = Parse(_rawToken);
        ExpandedValue = Environment.ExpandEnvironmentVariables(ParsedValue);
        ComparisonKey = Fold(ExpandedValue);
    }

    // §9.1: quotes/whitespace stripped for DISPLAY only. RawToken untouched.
    private static string Parse(string raw)
    {
        var t = raw.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"') t = t[1..^1];
        return t;
    }

    // §9.3 L2: case-folded + trailing separator normalized. C:\ must survive.
    private static string Fold(string expanded)
    {
        var t = expanded.TrimEnd('\\', '/');
        if (t.Length == 2 && t[1] == ':') t += '\\';
        return t.ToLowerInvariant();
    }

    // ---- What the grid binds to -------------------------------------------
    public string DisplayValue => ParsedValue;
    // §16: "Show RawToken and ExpandedValue distinctly; never conflate."
    public bool DisplayDiffersFromRaw => ParsedValue != RawToken;
    public bool HasAttention => Diagnostics.Count > 0;
    public Severity? WorstSeverity =>
        Diagnostics.Count == 0 ? null : Diagnostics.Max(d => d.Severity);

    // Replaces IsChecked = (Confidence == High).
    public bool IsAutoSelectable =>
        Diagnostics.Count > 0 && Diagnostics.All(d => d.SafeToAutoSelect);
}

// ---- The operation contract from §9.1, proven expressible ----------------
public static class OperationContract
{
    public static string ForScan(PathEntrySketch e) => e.RawToken;        // preserve exactly
    public static string ForDisplay(PathEntrySketch e) => e.ParsedValue;  // cleaned
    public static string ForValidate(PathEntrySketch e) => e.ExpandedValue;
    public static string ForDuplicates(PathEntrySketch e) => e.ComparisonKey;
    public static string ForSave(PathEntrySketch e) => e.RawToken;        // byte-identical round-trip
    // Normalize = an explicit command that ASSIGNS RawToken. Never a parse side effect.
    public static void Normalize(PathEntrySketch e, string canonical) => e.RawToken = canonical;
}
