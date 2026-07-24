namespace EnvMaid.App.Models;

/// <summary>
/// An executable in this entry's folder that is shadowed by an earlier PATH folder
/// (the earlier folder's copy wins). <see cref="Confidence"/> ranks how likely this
/// is a real problem versus incidental noise.
/// </summary>
public record ShadowConflict(
    string ExeName,
    string ShadowedFolderPath,
    ConflictConfidence Confidence);
