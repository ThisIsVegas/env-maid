using System.IO;

namespace EnvMaid.App.Services;

/// <summary>
/// Canonicalizes a single PATH entry's text so cosmetically-different spellings of the
/// same folder (trailing slash, redundant <c>..</c>/<c>.</c> segments) stop reading as
/// distinct entries. Pure string transform; never touches disk.
///
/// Literal paths go through <see cref="Path.GetFullPath(string)"/>, matching how Windows
/// itself canonicalizes. Entries containing an environment-variable reference (<c>%VAR%</c>)
/// are left structurally intact — only a trailing slash is trimmed — because expanding the
/// variable to canonicalize it would destroy the reference the user chose to keep.
/// </summary>
public class PathNormalizer
{
    public bool AreEquivalent(string left, string right)
    {
        var normalizedLeft = Normalize(Environment.ExpandEnvironmentVariables(left));
        var normalizedRight = Normalize(Environment.ExpandEnvironmentVariables(right));
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the canonical form of <paramref name="entry"/>. Empty/whitespace
    /// entries are returned unchanged so existing "empty entry" flagging still applies.</summary>
    public string Normalize(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return entry;

        var trimmed = entry.Trim();

        if (ContainsVariableReference(trimmed))
            return TrimTrailingSeparators(trimmed);

        try
        {
            // GetFullPath collapses ..\ and .\ and normalizes separators; it does not
            // require the folder to exist, so broken entries are canonicalized too.
            var full = Path.GetFullPath(trimmed);
            return TrimTrailingSeparators(full);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // Not a canonicalizable path (illegal characters, etc.) — leave it be.
            return trimmed;
        }
    }

    private static bool ContainsVariableReference(string path)
    {
        var first = path.IndexOf('%');
        return first >= 0 && path.IndexOf('%', first + 1) > first;
    }

    // Trim trailing '\' and '/', but never the slash of a drive root ("C:\").
    private static string TrimTrailingSeparators(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + '\\' : trimmed;
    }
}
