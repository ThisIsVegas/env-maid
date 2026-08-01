using System.IO;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

/// <summary>
/// Bands a shadow conflict by how likely it is a real problem. Shared by
/// <see cref="OrphanDetectionService"/> (per-entry flags) and
/// <see cref="ConflictAnalysisService"/> (the Conflicts tab).
/// </summary>
public class ConflictRanker
{
    // Installer/uninstaller name patterns that make a shadow almost certainly noise.
    private static readonly string[] DenylistPrefixes =
    {
        "unins", "setup", "install", "uninstall", "update", "updater",
        "vcredist", "vc_redist", "dotnetfx", "wix", "msiexec"
    };

    private static readonly string[] DenylistContains =
    {
        "redist", "setup", "installer"
    };

    private readonly CliToolListService _cliTools;

    public ConflictRanker(CliToolListService cliTools) => _cliTools = cliTools;

    /// <summary>
    /// First match wins: denylist name or byte-identical files -> false positive;
    /// known CLI tool -> likely real; otherwise -> possibly.
    /// </summary>
    public ConflictConfidence Rank(string exeName, string winnerFile, string loserFile)
    {
        if (IsDenylisted(exeName) || SameFileSize(winnerFile, loserFile))
            return ConflictConfidence.LikelyFalsePositive;

        if (_cliTools.IsKnownCliTool(exeName))
            return ConflictConfidence.LikelyReal;

        return ConflictConfidence.Possibly;
    }

    private static bool IsDenylisted(string exeName)
    {
        var stem = Path.GetFileNameWithoutExtension(exeName);
        return DenylistPrefixes.Any(p => stem.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || DenylistContains.Any(c => stem.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameFileSize(string a, string b)
    {
        try
        {
            return new FileInfo(a).Length == new FileInfo(b).Length;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
