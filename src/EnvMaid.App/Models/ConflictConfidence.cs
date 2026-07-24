namespace EnvMaid.App.Models;

/// <summary>
/// How likely a detected shadow conflict is a real problem the user cares about,
/// versus incidental noise (e.g. an uninstaller sharing a name). Separate from
/// <see cref="FlagConfidence"/>, which ranks per-entry cleanup severity.
/// </summary>
public enum ConflictConfidence
{
    LikelyFalsePositive,
    Possibly,
    LikelyReal
}
