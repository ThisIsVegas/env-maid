namespace EnvMaid.App.Services;

/// <summary>
/// The extensions the shell tries when resolving a bare command, in the order it tries them.
/// </summary>
/// <remarks>
/// <para>
/// Read from the <b>process</b> scope, not the registry. The process value is what a newly
/// launched shell inherits, and it can differ from both persisted scopes — on the machine this
/// was measured, <c>.CPL</c> appears in the process value and in neither User nor Machine.
/// Reading the Machine value would model a resolution that differs from what the user's shell
/// actually does.
/// </para>
/// <para>
/// This is authoritative. The <c>path</c> command's documentation claims a fixed
/// <c>.exe .com .bat .cmd</c> precedence, but that was measured to be wrong: with the shipped
/// default <c>PATHEXT</c> a <c>.com</c> beats a <c>.exe</c>, and reversing <c>PATHEXT</c>
/// reverses the winner. The doc appears to carry MS-DOS-era text.
/// </para>
/// </remarks>
public class PathExtService
{
    /// <summary>
    /// The documented fallback, used only when <c>PATHEXT</c> is missing from the environment.
    /// </summary>
    /// <remarks>
    /// Real machines routinely extend this — the one measured had <c>.PY;.PYW</c> appended — so
    /// this is a last resort, never a substitute for reading the environment.
    /// </remarks>
    public const string DocumentedDefault =
        ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC";

    private readonly Func<string?> _readPathExt;

    public PathExtService()
        : this(() => Environment.GetEnvironmentVariable("PATHEXT"))
    {
    }

    /// <param name="readPathExt">Test seam: supplies the raw PATHEXT value.</param>
    public PathExtService(Func<string?> readPathExt) => _readPathExt = readPathExt;

    /// <summary>Extensions in resolution order, lowercased and dot-prefixed.</summary>
    public IReadOnlyList<string> Extensions => Parse(_readPathExt() is { Length: > 0 } value
        ? value
        : DocumentedDefault);

    /// <summary>Where <paramref name="extension"/> sits in the order, or -1 if it is not a command.</summary>
    public int PrecedenceOf(string extension)
    {
        var normalized = Normalize(extension);
        for (var i = 0; i < Extensions.Count; i++)
            if (Extensions[i] == normalized)
                return i;
        return -1;
    }

    /// <summary>
    /// True when a file with this extension can be run by typing its bare name.
    /// </summary>
    /// <remarks>
    /// A file whose extension is not in <c>PATHEXT</c> is not a command and does not participate
    /// in shadowing — which is why the old hardcoded set was wrong in both directions: it checked
    /// <c>.bat</c> but never <c>.com</c>, and <c>.COM</c> sorts first in the shipped default.
    /// </remarks>
    public bool IsCommandExtension(string extension) => PrecedenceOf(extension) >= 0;

    private static IReadOnlyList<string> Parse(string raw) => raw
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Normalize)
        .Where(e => e.Length > 1)
        .Distinct()
        .ToList();

    private static string Normalize(string extension)
    {
        var trimmed = extension.Trim().ToLowerInvariant();
        return trimmed.StartsWith('.') ? trimmed : '.' + trimmed;
    }
}
