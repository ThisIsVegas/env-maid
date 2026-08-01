using System.Text.Json.Serialization;

namespace EnvMaid.App.Models;

/// <summary>What to do to one PATH entry.</summary>
public enum PathOpKind
{
    Remove,
    Add,
    Move,
}

/// <summary>
/// One change to apply to the stored PATH, expressed against raw tokens rather than positions
/// alone, so it still means something if the value moved underneath us.
/// </summary>
public record PathOp(PathOpKind Op, string RawToken, int? At = null, int? From = null, int? To = null);

/// <summary>The baseline the ops were computed against.</summary>
public record IntentBaseline(bool Present, string RegistryType, string Hash);

/// <summary>
/// The file handed to the elevated helper. It carries <em>intent</em>, never a finished value:
/// the helper re-reads the registry itself, checks the baseline still matches, and applies these
/// ops to what it actually found.
/// </summary>
/// <remarks>
/// Passing a joined PATH instead would put parsed state across the privilege boundary (§13) and
/// leave the helper with nothing sensible to do when the baseline has moved.
/// </remarks>
public class ElevatedIntent
{
    public int SchemaVersion { get; set; } = 1;

    public string Scope { get; set; } = nameof(PathScope.System);

    public string ValueName { get; set; } = "Path";

    public IntentBaseline Baseline { get; set; } = new(false, string.Empty, string.Empty);

    public IReadOnlyList<PathOp> Ops { get; set; } = Array.Empty<PathOp>();

    /// <summary>Written by the helper, read by the parent after the process exits.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ElevatedResult? Result { get; set; }
}

/// <summary>
/// Three independent outcomes plus a note. None gates another: a write can succeed while the
/// read-back fails, and neither rolls anything back.
/// </summary>
/// <remarks>
/// <see cref="BroadcastSucceeded"/> is filled in by the <em>parent</em>, not the helper — the
/// helper never broadcasts, so one save produces one broadcast (§7.2).
/// </remarks>
public record ElevatedOutcome(
    bool RegistryWriteSucceeded,
    bool ReadBackVerified,
    bool BroadcastSucceeded,
    string? Notes = null);

/// <summary>The helper's report, written back into the intent file it was handed.</summary>
public class ElevatedResult
{
    public ElevatedOutcome Outcome { get; set; } = new(false, false, false);

    /// <summary>Populated only on the conflict path, so the parent can show what it found.</summary>
    public string? OnDiskValue { get; set; }

    /// <summary>The registry type found on disk, alongside <see cref="OnDiskValue"/>.</summary>
    public string? OnDiskType { get; set; }
}

/// <summary>
/// How the helper exited. Coarse on purpose — these route, the intent file carries the detail,
/// since exit codes cannot carry three booleans plus a note without exploding combinatorially.
/// </summary>
public enum ElevatedExitCode
{
    Applied = 0,
    Failed = 1,
    Conflict = 2,
    NotRun = 3,
}
