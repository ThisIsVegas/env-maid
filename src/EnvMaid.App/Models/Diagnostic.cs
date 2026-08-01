namespace EnvMaid.App.Models;

/// <summary>What is notable about a PATH entry. One entry can carry several.</summary>
public enum DiagnosticKind
{
    // Parse-time — derived from RawToken alone, no disk access.
    SurroundingWhitespace,
    SurroundingQuotes,
    EmptyToken,

    // Validate-time — derived from ExpandedValue, touches disk.
    UnresolvedVariable,
    FolderMissing,
    FolderInaccessible,
    NoExecutables,
    ExceedsMaxPath,

    /// <summary>One token that resolves to more than one effective directory.</summary>
    StructurallyAmbiguous,

    // Cross-entry duplicates, by how certain the duplication is.
    DuplicateL1,
    DuplicateL2,
    DuplicateL3,
    DuplicateL4,
}

/// <summary>How much a diagnostic matters. Replaces the old confidence rank.</summary>
public enum Severity
{
    Info,
    Warning,
    Error,
}

/// <summary>One finding about one entry.</summary>
/// <remarks>
/// A list of these replaces the old single flag plus single reason string, so an entry that is
/// both missing and duplicated says both instead of picking one.
/// </remarks>
public record Diagnostic(DiagnosticKind Kind, Severity Severity, string Message)
{
    /// <summary>
    /// Whether an entry carrying <em>only</em> this finding may be pre-checked for removal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Safety is a property of the finding, not of how confident we are about it. The previous
    /// rule — pre-check anything at high confidence — pre-checked duplicates that were merely
    /// probable, entries whose variable failed to expand, and entries that were simply quoted.
    /// Each of those is a case where deleting is the wrong fix.
    /// </para>
    /// <para>
    /// Excluded on purpose: <see cref="DiagnosticKind.DuplicateL3"/> collapses two references
    /// that may be maintained independently; <see cref="DiagnosticKind.DuplicateL4"/> is
    /// advisory; <see cref="DiagnosticKind.UnresolvedVariable"/> is fixed by defining the
    /// variable, not by deleting the entry; <see cref="DiagnosticKind.SurroundingQuotes"/> marks
    /// a legitimate entry that happens to be quoted; <see cref="DiagnosticKind.StructurallyAmbiguous"/>
    /// must never be auto-edited; and <see cref="DiagnosticKind.FolderInaccessible"/> may name a
    /// folder that exists and works for another process.
    /// </para>
    /// </remarks>
    public bool SafeToAutoSelect => Kind is
        DiagnosticKind.EmptyToken or
        DiagnosticKind.FolderMissing or
        DiagnosticKind.DuplicateL1 or
        DiagnosticKind.DuplicateL2;
}

/// <summary>Whether the folder an entry names is actually there.</summary>
public enum ExistenceStatus
{
    /// <summary>Not yet checked. The grid renders before validation touches disk.</summary>
    Unknown,

    Exists,

    Missing,

    /// <summary>
    /// The folder is there but cannot be listed. Distinct from missing on purpose: deleting it
    /// is not obviously the right answer, and another process may read it fine.
    /// </summary>
    Inaccessible,
}
