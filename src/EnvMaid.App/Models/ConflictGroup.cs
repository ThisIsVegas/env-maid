namespace EnvMaid.App.Models;

/// <summary>One folder on the PATH that provides a given executable.</summary>
public record ConflictLocation(PathEntry Entry, string ExpandedFolder)
{
    public string DisplayPath => Entry.Path;
    public PathScope Scope => Entry.Scope;
}

/// <summary>
/// All PATH folders that provide the same executable name, in resolution order.
/// The first is the winner; the rest are shadowed.
/// </summary>
public record ConflictGroup(
    string ExeName,
    ConflictConfidence Confidence,
    ConflictLocation Winner,
    IReadOnlyList<ConflictLocation> Losers);
