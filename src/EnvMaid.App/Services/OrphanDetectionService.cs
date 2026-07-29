using System.IO;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

public class OrphanDetectionService
{
    /// <summary>
    /// Answers "does this folder look useful?" — deliberately <em>not</em> derived from PATHEXT.
    /// </summary>
    /// <remarks>
    /// <c>.dll</c> is the clearest reason the two sets differ: it will never be in PATHEXT, but a
    /// DLL-only folder on PATH is legitimate because the loader searches PATH as its last stage.
    /// Deriving this from PATHEXT would flag such a folder as having no executables, which is
    /// wrong. <c>.ps1</c> is similar — PowerShell runs it, but it is not a bare command.
    /// </remarks>
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".dll"
    };

    private readonly ConflictRanker _ranker;
    private readonly PathExtService _pathExt;

    public OrphanDetectionService(ConflictRanker ranker, PathExtService? pathExt = null)
    {
        _ranker = ranker;
        _pathExt = pathExt ?? new PathExtService();
    }

    public void Analyze(IReadOnlyList<PathEntry> userEntries, IReadOnlyList<PathEntry> systemEntries)
    {
        // System first, matching real PATH resolution order. Grouping does not depend on the
        // order, but every composition site in the app uses the same one so that none of them
        // has to be re-derived when someone reads it.
        var allEntries = systemEntries.Concat(userEntries).ToList();
        var byNormalized = allEntries
            .GroupBy(e => Normalize(e.RawToken))
            .ToDictionary(g => g.Key, g => g.ToList());

        // One pass in resolution order, not one per scope: the copy Windows actually resolves is
        // the first across both scopes, and the later one is the redundant entry. Walking each
        // scope separately meant a folder listed in both was never marked duplicate at all.
        ApplyEntryDiagnostics(allEntries, byNormalized);

        ApplyShadowFlags(allEntries);
    }

    private void ApplyShadowFlags(IReadOnlyList<PathEntry> resolutionOrderEntries)
    {
        // command name (no extension) -> the winning folder + the file that actually runs.
        var seenCommands = new Dictionary<string, (string Normalized, string Display, string WinnerFile)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in resolutionOrderEntries)
        {
            var expanded = entry.ExpandedValue;
            if (!Directory.Exists(expanded))
                continue;

            var files = TryEnumerateFiles(expanded);
            if (files is null)
                continue;

            var normalizedFolder = Normalize(entry.RawToken);

            entry.ShadowConflicts.Clear();
            foreach (var (command, file) in WinnersPerCommand(files))
            {
                var name = Path.GetFileName(file);
                if (seenCommands.TryGetValue(command, out var first))
                {
                    if (!string.Equals(first.Normalized, normalizedFolder, StringComparison.OrdinalIgnoreCase)
                        && !entry.ShadowConflicts.Any(c => c.ExeName == name))
                    {
                        var confidence = _ranker.Rank(name, first.WinnerFile, file);
                        entry.ShadowConflicts.Add(new ShadowConflict(name, first.Display, confidence));
                    }
                }
                else
                {
                    seenCommands[command] = (normalizedFolder, expanded, file);
                }
            }

            // A shadow conflict is not a diagnostic: it says something about the relationship
            // between two folders, not about this entry being wrong. HasAttention already
            // surfaces it, and it must never make an entry auto-selectable for deletion.
        }
    }

    private void ApplyEntryDiagnostics(IReadOnlyList<PathEntry> entriesInResolutionOrder, Dictionary<string, List<PathEntry>> byNormalized)
    {
        var seenNormalized = new HashSet<string>();

        foreach (var entry in entriesInResolutionOrder)
        {
            entry.Diagnostics.Clear();

            foreach (var diagnostic in ParseDiagnostics(entry))
                entry.Diagnostics.Add(diagnostic);

            entry.ExistenceStatus = Validate(entry);

            var normalized = Normalize(entry.RawToken);
            if (!seenNormalized.Add(normalized))
                entry.Diagnostics.Add(DuplicateDiagnostic(entry, byNormalized[normalized]));

            // Auto-selection is decided by what is wrong, not by how sure we are. One unsafe
            // finding vetoes the entry, so a duplicate whose variable also failed to expand is
            // never silently pre-checked for deletion.
            entry.IsChecked = entry.IsAutoSelectable;
        }
    }

    /// <summary>Findings that come from the token alone, without touching disk.</summary>
    private static IEnumerable<Diagnostic> ParseDiagnostics(PathEntry entry)
    {
        var raw = entry.RawToken;

        if (string.IsNullOrWhiteSpace(entry.ParsedValue))
        {
            yield return new Diagnostic(DiagnosticKind.EmptyToken, Severity.Warning, "Empty entry");
            yield break;
        }

        if (raw != raw.Trim())
            yield return new Diagnostic(DiagnosticKind.SurroundingWhitespace, Severity.Info,
                "Stored with surrounding spaces. Windows ignores them; EnvMaid keeps the value as stored.");

        var trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            yield return new Diagnostic(DiagnosticKind.SurroundingQuotes, Severity.Info,
                "Stored with surrounding quotes.");
    }

    /// <summary>Findings that need the filesystem, and the resulting existence status.</summary>
    private ExistenceStatus Validate(PathEntry entry)
    {
        if (entry.Has(DiagnosticKind.EmptyToken))
            return ExistenceStatus.Unknown;

        var expanded = entry.ExpandedValue;

        // An unexpanded %VAR% means the variable is not defined — which is a different problem
        // from a deleted folder, and has a different fix. Reporting it as missing would point the
        // user at deleting an entry when what they need is to define the variable.
        if (expanded.Contains('%') && entry.ParsedValue.Contains('%'))
        {
            entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.UnresolvedVariable, Severity.Warning,
                "This entry refers to a variable that is not defined, so Windows cannot resolve it."));
            return ExistenceStatus.Unknown;
        }

        if (!Directory.Exists(expanded))
        {
            entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.FolderMissing, Severity.Error,
                "Folder does not exist"));
            return ExistenceStatus.Missing;
        }

        var files = TryEnumerateFiles(expanded);
        if (files is null)
        {
            // The folder is there but we are not allowed to list it. Reporting "no executables"
            // would invite the user to delete a folder that may be full of them.
            entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.FolderInaccessible, Severity.Info,
                "Folder exists but could not be read"));
            return ExistenceStatus.Inaccessible;
        }

        if (!files.Any(f => ExecutableExtensions.Contains(Path.GetExtension(f))))
            entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.NoExecutables, Severity.Info,
                "No executable-type files found"));

        return ExistenceStatus.Exists;
    }

    /// <summary>
    /// Classifies a repeat of an already-seen folder.
    /// </summary>
    /// <remarks>
    /// L1 is the same token twice and L2 differs only by case or a trailing separator — both are
    /// safe to remove automatically. Anything subtler is left for the duplicate-levels work;
    /// treating it as L2 here would auto-check entries that are not certainly redundant.
    /// </remarks>
    private static Diagnostic DuplicateDiagnostic(PathEntry entry, List<PathEntry> group)
    {
        var earlier = group.FirstOrDefault(g => !ReferenceEquals(g, entry));
        var crossScope = group.Any(g => g.Scope != entry.Scope);

        var where = crossScope
            ? $" (also in {group.First(g => g.Scope != entry.Scope).Scope} PATH)"
            : string.Empty;

        var exact = earlier is not null
            && string.Equals(earlier.RawToken, entry.RawToken, StringComparison.Ordinal);

        return exact
            ? new Diagnostic(DiagnosticKind.DuplicateL1, Severity.Warning, $"Duplicate entry{where}")
            : new Diagnostic(DiagnosticKind.DuplicateL2, Severity.Warning,
                $"Duplicate — same folder, written differently{where}");
    }

    /// <summary>
    /// Lists a folder's files, or returns <c>null</c> when it exists but cannot be read.
    /// </summary>
    /// <remarks>
    /// <c>Directory.Exists</c> answers "is there a folder here", not "may I list it" — a folder
    /// on PATH under another user's profile answers yes to the first and throws on the second.
    /// Returning null keeps that distinct from a genuinely empty folder.
    /// </remarks>
    /// <summary>
    /// For one folder's files, the file that would actually run for each command it provides.
    /// </summary>
    /// <remarks>
    /// Within a folder PATHEXT order decides, so a <c>foo.com</c> beside a <c>foo.exe</c> means
    /// the <c>.exe</c> never runs by that name and does not represent the folder.
    /// </remarks>
    private IEnumerable<(string Command, string File)> WinnersPerCommand(IReadOnlyList<string> files)
    {
        var best = new Dictionary<string, (string File, int Precedence)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var precedence = _pathExt.PrecedenceOf(Path.GetExtension(file));
            if (precedence < 0)
                continue; // not runnable as a bare command

            var command = Path.GetFileNameWithoutExtension(file);
            if (command.Length == 0)
                continue;

            if (!best.TryGetValue(command, out var current) || precedence < current.Precedence)
                best[command] = (file, precedence);
        }

        return best.Select(kv => (kv.Key, kv.Value.File));
    }

    // Not directly unit-tested: reproducing it needs a folder with a deny ACL, which means
    // mutating machine state from a test. The behaviour is one try/catch; the risk it removes
    // (mislabelling an unreadable folder "no executables") is what earns the branch.
    private static IReadOnlyList<string>? TryEnumerateFiles(string expandedFolder)
    {
        try
        {
            return Directory.GetFiles(expandedFolder);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string Normalize(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return expanded.TrimEnd('\\').ToLowerInvariant();
    }
}
