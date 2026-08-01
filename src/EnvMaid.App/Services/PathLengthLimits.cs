namespace EnvMaid.App.Services;

/// <summary>Which length band a stored PATH falls into.</summary>
public enum PathLengthBand
{
    Ok,

    /// <summary>Long enough that some other PATH-writing tools mishandle it. EnvMaid is fine.</summary>
    Caution,

    /// <summary>Past what a single environment variable can hold. A save is refused.</summary>
    TooLong,
}

/// <summary>
/// The two real boundaries on a stored PATH, measured in <b>characters</b> of the joined value
/// (entries plus the <c>;</c> separators).
/// </summary>
/// <remarks>
/// <para>
/// Characters, not bytes, and said out loud because byte-vs-character confusion is the classic
/// registry bug: <c>cbData / 2</c> is not a safe character count when the byte count is odd.
/// </para>
/// <para>
/// Both limits are per <em>scope value</em>. User Path and Machine Path are separate registry
/// values with separate budgets; neither constrains the merged environment block.
/// </para>
/// </remarks>
public static class PathLengthLimits
{
    /// <summary>
    /// Beyond this, some tools that modify PATH may mishandle the value. Not a write limit and
    /// not a load limit — nothing is dropped here, and editing in EnvMaid stays safe.
    /// </summary>
    public const int CautionThreshold = 2047;

    /// <summary>
    /// The documented single-variable ceiling. Nothing below EnvMaid enforces it — a probe wrote
    /// 40,000 characters to the registry and read them back exactly — so this gate is entirely
    /// EnvMaid's to apply, on both sides of the privilege boundary.
    /// </summary>
    public const int HardMaximum = 32767;

    public static PathLengthBand BandFor(int characters) => characters switch
    {
        > HardMaximum => PathLengthBand.TooLong,
        > CautionThreshold => PathLengthBand.Caution,
        _ => PathLengthBand.Ok,
    };

    public static bool IsWritable(int characters) => characters <= HardMaximum;
}
