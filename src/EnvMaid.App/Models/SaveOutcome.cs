namespace EnvMaid.App.Models;

/// <summary>What a caller chose to do about a scope changed underneath it.</summary>
public enum ConflictResolution
{
    /// <summary>Discard the pending edits and reload what is on disk.</summary>
    Reload,

    /// <summary>Write the pending edits over the external change (recoverable via the backup).</summary>
    Overwrite,

    /// <summary>Leave this scope alone.</summary>
    Cancel,
}

/// <summary>
/// A scope changed outside EnvMaid between the scan and the save, described well enough for
/// the user to choose between their edits and the external ones.
/// </summary>
public record ConflictPrompt(
    PathScope Scope,
    IReadOnlyList<string> ExternalChangeSummary,
    IReadOnlyList<string> PendingChangeSummary);

/// <summary>How one scope fared in a save.</summary>
public enum ScopeSaveStatus
{
    /// <summary>Nothing to write — the staged value already matches what is stored.</summary>
    Unchanged,

    /// <summary>Written and confirmed to read back exactly as written.</summary>
    Written,

    /// <summary>
    /// Written, but the read-back did not match. The write is <em>not</em> rolled back:
    /// rewriting would be a second unverified write onto a value already in an unexpected state.
    /// </summary>
    WrittenButUnverified,

    /// <summary>Skipped because the scope changed underneath us and the user cancelled.</summary>
    SkippedConflict,

    /// <summary>The System scope needed elevation, and it was declined or failed.</summary>
    ElevationFailed,

    /// <summary>The write itself failed.</summary>
    Failed,
}

/// <summary>Per-scope save result. Scopes succeed and fail independently (§13 partial success).</summary>
public record ScopeSaveResult(PathScope Scope, ScopeSaveStatus Status, string? Detail = null)
{
    public bool Succeeded => Status is ScopeSaveStatus.Unchanged or ScopeSaveStatus.Written;
}
