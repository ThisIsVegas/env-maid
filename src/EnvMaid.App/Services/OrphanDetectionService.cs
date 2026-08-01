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
    private readonly DirectoryIdentityService _identities;
    private readonly LongPathSupport _longPaths;

    public OrphanDetectionService(
        ConflictRanker ranker,
        PathExtService? pathExt = null,
        DirectoryIdentityService? identities = null,
        LongPathSupport? longPaths = null)
    {
        _ranker = ranker;
        _pathExt = pathExt ?? new PathExtService();
        _identities = identities ?? new DirectoryIdentityService();
        _longPaths = longPaths ?? new LongPathSupport();
    }

    public void Analyze(IReadOnlyList<PathEntry> userEntries, IReadOnlyList<PathEntry> systemEntries)
    {
        // System first, matching real PATH resolution order — the first copy of a folder is the
        // one Windows resolves, which is what makes any later copy the redundant one.
        var allEntries = systemEntries.Concat(userEntries).ToList();

        // One pass across both scopes, not one per scope: walking each separately meant a folder
        // listed in both was never marked duplicate at all.
        ApplyEntryDiagnostics(allEntries);

        ApplyShadowFlags(allEntries);
    }

    private void ApplyShadowFlags(IReadOnlyList<PathEntry> resolutionOrderEntries)
    {
        // command name (no extension) -> the winning folder + the file that actually runs.
        var seenCommands = new Dictionary<string, (string Normalized, string Display, string WinnerFile)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in resolutionOrderEntries)
        {
            entry.ShadowConflicts.Clear();

            // Analysis expands an ambiguous token rather than refusing it: those directories are
            // genuinely on the search path and can genuinely shadow something. Only the
            // maintenance commands refuse, because rewriting the token is what has no meaning.
            foreach (var directory in entry.EffectiveDirectories)
            {
                if (!Directory.Exists(directory))
                    continue;

                var files = TryEnumerateFiles(directory);
                if (files is null)
                    continue;

                var normalizedFolder = directory.TrimEnd('\\').ToLowerInvariant();

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
                        seenCommands[command] = (normalizedFolder, directory, file);
                    }
                }
            }

            // A shadow conflict is not a diagnostic: it says something about the relationship
            // between two folders, not about this entry being wrong. HasAttention already
            // surfaces it, and it must never make an entry auto-selectable for deletion.
        }
    }

    private void ApplyEntryDiagnostics(IReadOnlyList<PathEntry> entriesInResolutionOrder)
    {
        _identities.Clear();

        // Entries already seen, in resolution order. The first copy of a folder is the one
        // Windows resolves; anything later that reaches the same place is the redundant one.
        var earlier = new List<PathEntry>();

        foreach (var entry in entriesInResolutionOrder)
        {
            entry.Diagnostics.Clear();

            foreach (var diagnostic in ParseDiagnostics(entry))
                entry.Diagnostics.Add(diagnostic);

            entry.ExistenceStatus = Validate(entry);

            if (FindDuplicate(entry, earlier) is { } duplicate)
                entry.Diagnostics.Add(duplicate);

            earlier.Add(entry);

            // Auto-selection is decided by what is wrong, not by how sure we are. One unsafe
            // finding vetoes the entry, so a duplicate whose variable also failed to expand is
            // never silently pre-checked for deletion.
            entry.IsChecked = entry.IsAutoSelectable;
        }
    }

    /// <summary>
    /// Finds the earlier entry this one duplicates, and how certain that duplication is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Levels are assigned most-specific-first, so each entry gets exactly one: an exact textual
    /// match is L1 and is never also reported as L2, L3 or L4.
    /// </para>
    /// <para>
    /// The three textual levels are free — they compare fields the entry already derives. Only
    /// L4 touches disk, and only for entries no textual level matched, so the common case costs
    /// nothing extra.
    /// </para>
    /// </remarks>
    private Diagnostic? FindDuplicate(PathEntry entry, IReadOnlyList<PathEntry> earlier)
    {
        // An empty token names nothing, so there is nothing for it to duplicate.
        if (entry.Has(DiagnosticKind.EmptyToken))
            return null;

        // A token contributing several directories has no single identity to compare, and the
        // exclusion is total rather than just from auto-removal: L4 would open the joined string
        // as a path, fail, and read as "not a duplicate" for entirely the wrong reason.
        if (entry.IsStructurallyAmbiguous)
            return null;

        // L1 — the identical token, character for character. This holds even when the token does
        // not resolve: listing the same broken entry twice is still listing it twice.
        var match = earlier.FirstOrDefault(e =>
            string.Equals(e.RawToken, entry.RawToken, StringComparison.Ordinal));
        if (match is not null)
            return new Diagnostic(DiagnosticKind.DuplicateL1, Severity.Warning,
                $"Duplicate entry{Where(entry, match)}");

        // Past L1, every level claims two tokens reach the same folder — which cannot be judged
        // when the token does not resolve to one. An undefined variable might expand to anything.
        if (entry.Has(DiagnosticKind.UnresolvedVariable))
            return null;

        // L2 — the same token written differently: case, or a trailing separator. Compared
        // BEFORE expansion, because %JAVA_HOME%\bin and C:\jdk\bin fold to the same key once
        // expanded, and calling that L2 would auto-check a pair that belongs to L3.
        match = earlier.FirstOrDefault(e =>
            string.Equals(e.ComparisonKey, entry.ComparisonKey, StringComparison.Ordinal));
        if (match is not null)
            return new Diagnostic(DiagnosticKind.DuplicateL2, Severity.Warning,
                $"Duplicate — same folder, written differently{Where(entry, match)}");

        // L3 — two tokens that expand to the same folder today but are maintained separately,
        // such as %JAVA_HOME%\bin beside C:\jdk\bin. Removing one is legitimate, but doing it
        // silently is not: whichever survives may stop tracking the variable the other followed.
        match = earlier.FirstOrDefault(e =>
            !e.Has(DiagnosticKind.UnresolvedVariable)
            && string.Equals(e.ExpandedValue.TrimEnd('\\', '/'), entry.ExpandedValue.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return new Diagnostic(DiagnosticKind.DuplicateL3, Severity.Warning,
                $"'{entry.ParsedValue}' and '{match.ParsedValue}' point to the same folder today, but are " +
                "written differently. Removing one means the other no longer follows changes to the variable.");

        // L4 — different paths that the filesystem says are the same directory: a junction, an
        // 8.3 short name, a subst drive. Advisory only; nothing here is auto-removed.
        return FindIdentityDuplicate(entry, earlier);
    }

    private Diagnostic? FindIdentityDuplicate(PathEntry entry, IReadOnlyList<PathEntry> earlier)
    {
        if (entry.ExistenceStatus != ExistenceStatus.Exists)
            return null;

        var identity = _identities.Resolve(entry.ExpandedValue);
        if (identity is null)
            return null;

        foreach (var candidate in earlier)
        {
            if (candidate.ExistenceStatus != ExistenceStatus.Exists || candidate.IsStructurallyAmbiguous)
                continue;

            var other = _identities.Resolve(candidate.ExpandedValue);
            if (other is null || !identity.SameObjectAs(other))
                continue;

            var mechanism = DirectoryIdentityService.DescribeAlias(entry.ExpandedValue, identity);
            var via = mechanism is null ? string.Empty : $" (via a {mechanism})";

            return new Diagnostic(DiagnosticKind.DuplicateL4, Severity.Info,
                $"This is the same folder as '{candidate.ParsedValue}'{via}, reached by a different path.");
        }

        return null;
    }

    private static string Where(PathEntry entry, PathEntry match) =>
        match.Scope == entry.Scope ? string.Empty : $" (also in {match.Scope} PATH)";

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

    /// <summary>
    /// Findings that need the filesystem, and the resulting existence status.
    /// </summary>
    /// <remarks>
    /// Runs per <em>effective directory</em>, not on the whole expanded token. A token
    /// contributing two directories used to be handed to <c>Directory.Exists</c> whole, which
    /// always failed — so a perfectly working multi-directory variable was flagged as a missing
    /// folder and arrived pre-checked for deletion.
    /// </remarks>
    private ExistenceStatus Validate(PathEntry entry)
    {
        if (entry.Has(DiagnosticKind.EmptyToken))
            return ExistenceStatus.Unknown;

        // An unexpanded %VAR% means the variable is not defined — which is a different problem
        // from a deleted folder, and has a different fix. Reporting it as missing would point the
        // user at deleting an entry when what they need is to define the variable.
        if (entry.ExpandedValue.Contains('%') && entry.ParsedValue.Contains('%'))
        {
            entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.UnresolvedVariable, Severity.Warning,
                "This entry refers to a variable that is not defined, so Windows cannot resolve it."));
            return ExistenceStatus.Unknown;
        }

        var directories = entry.EffectiveDirectories;

        if (entry.IsStructurallyAmbiguous)
            entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.StructurallyAmbiguous, Severity.Info,
                $"This single entry contributes {directories.Count} directories to the search path. " +
                "Windows supports this, but EnvMaid will not rewrite it automatically."));

        AddLongPathDiagnostics(entry, directories);

        var statuses = directories.Select(CheckDirectory).ToList();
        if (statuses.Count == 0)
            return ExistenceStatus.Unknown;

        // For an ambiguous token the status is the aggregate, so "one of the two is missing" is
        // expressible rather than collapsing to a single yes or no.
        if (statuses.All(s => s == ExistenceStatus.Exists))
        {
            AddNoExecutablesIfApplicable(entry, directories);
            return ExistenceStatus.Exists;
        }

        if (statuses.All(s => s == ExistenceStatus.Missing))
        {
            entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.FolderMissing, Severity.Error,
                directories.Count == 1 ? "Folder does not exist" : "None of these folders exist"));
            return ExistenceStatus.Missing;
        }

        if (statuses.Any(s => s == ExistenceStatus.Missing))
        {
            var missing = directories.Where(d => CheckDirectory(d) == ExistenceStatus.Missing).ToList();
            entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.FolderMissing, Severity.Warning,
                $"{missing.Count} of {directories.Count} folders in this entry do not exist: " +
                string.Join(", ", missing)));
            return ExistenceStatus.Unknown;
        }

        // Nothing missing, so what is left is at least one folder we cannot read.
        entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.FolderInaccessible, Severity.Info,
            directories.Count == 1
                ? "Folder exists but could not be read"
                : "Some of these folders exist but could not be read"));
        return ExistenceStatus.Inaccessible;
    }

    private static ExistenceStatus CheckDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return ExistenceStatus.Missing;

        return TryEnumerateFiles(directory) is null
            ? ExistenceStatus.Inaccessible
            : ExistenceStatus.Exists;
    }

    private static void AddNoExecutablesIfApplicable(PathEntry entry, IReadOnlyList<string> directories)
    {
        // "Nothing here looks useful" only holds when it holds for every directory the entry
        // contributes — one useful folder is reason enough to keep the entry.
        var anyExecutable = directories
            .Select(TryEnumerateFiles)
            .Where(files => files is not null)
            .SelectMany(files => files!)
            .Any(file => ExecutableExtensions.Contains(Path.GetExtension(file)));

        if (!anyExecutable)
            entry.Diagnostics.Add(new Diagnostic(DiagnosticKind.NoExecutables, Severity.Info,
                "No executable-type files found"));
    }

    /// <summary>
    /// Flags effective directories past <c>MAX_PATH</c>.
    /// </summary>
    /// <remarks>
    /// Measured on the expanded directory, not the raw token: a short <c>%VAR%</c> can expand
    /// well past the limit. Never auto-selected — a long path is usually a working directory.
    /// </remarks>
    private void AddLongPathDiagnostics(PathEntry entry, IReadOnlyList<string> directories)
    {
        var tooLong = directories.Where(d => d.Length > LongPathSupport.MaxPath).ToList();
        if (tooLong.Count == 0)
            return;

        entry.Diagnostics.Add(_longPaths.IsEnabled
            ? new Diagnostic(DiagnosticKind.ExceedsMaxPath, Severity.Info,
                $"Longer than {LongPathSupport.MaxPath} characters. Long paths are enabled on this " +
                "machine, though some older tools may still fail to use it.")
            : new Diagnostic(DiagnosticKind.ExceedsMaxPath, Severity.Warning,
                $"Longer than the {LongPathSupport.MaxPath}-character limit, and long path support " +
                "is not enabled on this machine. Some programs may not find files here."));
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
