namespace EnvMaid.App.Services;

/// <summary>
/// Shortens a literal PATH entry by folding a known Windows environment variable back in
/// (e.g. <c>C:\Users\me\AppData\Local\x</c> → <c>%LOCALAPPDATA%\x</c>) to claw back room
/// under the 2047-character PATH limit. Only a curated set of variables that Windows always
/// defines is used, so a compressed entry can never break because a variable was later
/// removed. When more than one variable matches, the longest expansion wins (maximum
/// characters saved). Entries that already use a <c>%VAR%</c> reference, or that match no
/// known variable, are returned unchanged.
/// </summary>
public class PathCompressor
{
    // Curated, always-defined Windows variables, resolved once per instance. Order does not
    // matter: match selection is by expanded length, longest first.
    private static readonly string[] CandidateVariables =
    {
        "LOCALAPPDATA", "APPDATA", "USERPROFILE",
        "ProgramFiles(x86)", "ProgramFiles", "ProgramData",
        "SystemRoot",
    };

    private readonly IReadOnlyList<(string Token, string Expanded)> _substitutions;

    public PathCompressor()
        : this(name => Environment.GetEnvironmentVariable(name))
    {
    }

    // Test seam: inject variable values instead of reading the machine environment.
    public PathCompressor(Func<string, string?> resolve)
    {
        _substitutions = CandidateVariables
            .Select(name => (Token: $"%{name}%", Expanded: resolve(name)))
            .Where(x => !string.IsNullOrEmpty(x.Expanded))
            .Select(x => (x.Token, Expanded: x.Expanded!.TrimEnd('\\')))
            // Longest expansion first so the most space-saving match is tried first.
            .OrderByDescending(x => x.Expanded.Length)
            .ToList();
    }

    /// <summary>Returns a compressed form of <paramref name="entry"/>, or the entry
    /// unchanged when it already uses a variable or matches none.</summary>
    public string Compress(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry) || entry.Contains('%'))
            return entry;

        foreach (var (token, expanded) in _substitutions)
        {
            // Match a whole leading path segment: the expansion, followed by end-of-string
            // or a separator — so "C:\ProgramData" never matches "C:\ProgramDataX".
            if (entry.Length >= expanded.Length
                && entry.AsSpan(0, expanded.Length).Equals(expanded, StringComparison.OrdinalIgnoreCase)
                && (entry.Length == expanded.Length || entry[expanded.Length] == '\\'))
            {
                return token + entry[expanded.Length..];
            }
        }

        return entry;
    }
}
