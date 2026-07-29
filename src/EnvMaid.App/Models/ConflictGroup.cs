namespace EnvMaid.App.Models;

/// <summary>One folder on the PATH that provides a given executable.</summary>
public record ConflictLocation(PathEntry Entry, string ExpandedFolder)
{
    public string DisplayPath => Entry.RawToken;
    public PathScope Scope => Entry.Scope;
}

/// <summary>
/// All PATH folders that provide the same command, in resolution order. The first is what
/// actually runs; the rest are shadowed.
/// </summary>
/// <remarks>
/// <see cref="ExeName"/> is the command as typed — no extension — because that is the unit that
/// competes. The filenames are carried alongside so a report can say which file wins, which
/// matters when they differ: "foo.bat in C:\a shadows foo.exe in C:\b" is invisible if the group
/// is keyed on the filename.
/// </remarks>
public record ConflictGroup(
    string ExeName,
    ConflictConfidence Confidence,
    ConflictLocation Winner,
    IReadOnlyList<ConflictLocation> Losers,
    string WinnerFileName = "",
    IReadOnlyList<string>? LoserFileNames = null)
{
    /// <summary>True when the shadowed files differ in extension from the winner.</summary>
    public bool ShadowsAcrossExtensions =>
        LoserFileNames is not null
        && LoserFileNames.Any(name => !string.Equals(name, WinnerFileName, StringComparison.OrdinalIgnoreCase));
}
