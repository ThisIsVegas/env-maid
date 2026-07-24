namespace EnvMaid.App.Models;

/// <summary>
/// Machine-readable classification of why an entry is flagged, so dashboard counts
/// don't have to parse the human-readable <see cref="PathEntry.Reason"/> string.
/// An entry can carry several (e.g. missing and duplicate).
/// </summary>
[Flags]
public enum PathFlag
{
    None = 0,
    Missing = 1 << 0,    // folder does not exist
    Empty = 1 << 1,      // empty entry
    NoExecutable = 1 << 2,
    Duplicate = 1 << 3,
}
