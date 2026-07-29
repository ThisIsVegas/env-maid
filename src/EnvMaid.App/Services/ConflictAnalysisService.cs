using System.IO;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

/// <summary>
/// Builds <see cref="ConflictGroup"/>s for the Conflicts tab: executables provided
/// by more than one PATH folder, grouped by exe name in resolution order (System
/// before User), with the winner and shadowed losers identified and banded.
/// Also answers coverage questions for the delete prompt.
/// </summary>
public class ConflictAnalysisService
{
    private readonly ConflictRanker _ranker;
    private readonly PathExtService _pathExt;

    /// <summary>
    /// What this analysis simulates, stated in the UI so the answer is not presented as
    /// <em>the</em> answer. Resolution differs by launch mechanism, and this models one of them.
    /// </summary>
    public const string ResolverProfile =
        "Assumes a command typed at a shell prompt (cmd.exe or PowerShell), using the PATHEXT " +
        "from your environment. Does not model App Paths or the current directory.";

    public ConflictAnalysisService(ConflictRanker ranker, PathExtService? pathExt = null)
    {
        _ranker = ranker;
        _pathExt = pathExt ?? new PathExtService();
    }

    /// <summary>
    /// Groups shadowed commands across both scopes, System entries before User.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The unit is the <b>command name</b> — the filename with its extension stripped — not the
    /// filename. <c>foo.bat</c> and <c>foo.exe</c> are not unrelated files; they compete for the
    /// command <c>foo</c>, and only one of them ever runs.
    /// </para>
    /// <para>
    /// Resolution is directory-major: for each folder in PATH order, every PATHEXT extension is
    /// tried before moving to the next folder. So a folder earlier on PATH wins regardless of
    /// extension, and within one folder the PATHEXT order decides.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ConflictGroup> Analyze(
        IReadOnlyList<PathEntry> userEntries,
        IReadOnlyList<PathEntry> systemEntries)
    {
        var resolutionOrder = systemEntries.Concat(userEntries).ToList();

        // command name -> providers in resolution order, winner first.
        var byCommand = new Dictionary<string, List<CommandProvider>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in resolutionOrder)
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry.Path);
            if (!Directory.Exists(expanded))
                continue;

            var location = new ConflictLocation(entry, expanded);

            foreach (var (command, file) in WinnersPerCommand(expanded))
            {
                if (!byCommand.TryGetValue(command, out var providers))
                    byCommand[command] = providers = new List<CommandProvider>();

                // A folder can appear twice on PATH; it only provides the command once.
                if (!providers.Any(p => PathsEqual(p.Location.ExpandedFolder, expanded)))
                    providers.Add(new CommandProvider(location, file));
            }
        }

        var groups = new List<ConflictGroup>();
        foreach (var (command, providers) in byCommand)
        {
            if (providers.Count < 2)
                continue; // no conflict

            var winner = providers[0];
            var losers = providers.Skip(1).ToList();

            // Band the group by its most-severe loser (the worst real problem present).
            var confidence = losers
                .Select(l => _ranker.Rank(Path.GetFileName(winner.File), winner.File, l.File))
                .Max();

            groups.Add(new ConflictGroup(
                command,
                confidence,
                winner.Location,
                losers.Select(l => l.Location).ToList(),
                Path.GetFileName(winner.File),
                losers.Select(l => Path.GetFileName(l.File)).ToList()));
        }

        // Most-serious conflicts first, then alphabetically.
        return groups
            .OrderByDescending(g => g.Confidence)
            .ThenBy(g => g.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// For one folder, the file that would actually run for each command it provides.
    /// </summary>
    /// <remarks>
    /// Within a folder, PATHEXT order decides: if both <c>foo.com</c> and <c>foo.exe</c> are
    /// present and <c>.COM</c> precedes <c>.EXE</c>, typing <c>foo</c> runs the <c>.com</c> and
    /// the <c>.exe</c> is unreachable by that name. Only the winner represents the folder.
    /// </remarks>
    private IEnumerable<(string Command, string File)> WinnersPerCommand(string folder)
    {
        var best = new Dictionary<string, (string File, int Precedence)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateFiles(folder))
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

    private readonly record struct CommandProvider(ConflictLocation Location, string File);

    /// <summary>
    /// For the delete prompt: which commands in <paramref name="folderToRemove"/> would still be
    /// reachable through another PATH folder afterwards. Each command is paired with the folder
    /// that would take over, or null when nothing else provides it.
    /// </summary>
    /// <remarks>
    /// Coverage is per <em>command</em>, not per filename: a folder providing <c>foo.exe</c>
    /// still covers <c>foo</c> when the folder being removed had <c>foo.cmd</c>. Matching on the
    /// filename would report a false loss and talk the user out of a safe removal.
    /// </remarks>
    public IReadOnlyList<(string ExeName, string? CoveredBy)> CoverageAfterRemoving(
        ConflictLocation folderToRemove,
        IReadOnlyList<PathEntry> userEntries,
        IReadOnlyList<PathEntry> systemEntries)
    {
        var others = systemEntries.Concat(userEntries)
            .Where(e => !ReferenceEquals(e, folderToRemove.Entry))
            .Select(e => (Entry: e, Expanded: Environment.ExpandEnvironmentVariables(e.Path)))
            .Where(x => Directory.Exists(x.Expanded)
                        && !PathsEqual(x.Expanded, folderToRemove.ExpandedFolder))
            .ToList();

        var result = new List<(string, string?)>();
        foreach (var (command, file) in WinnersPerCommand(folderToRemove.ExpandedFolder))
        {
            var coveringFolder = others.FirstOrDefault(o => ProvidesCommand(o.Expanded, command));
            result.Add((Path.GetFileName(file), coveringFolder.Entry?.Path));
        }
        return result;
    }

    private bool ProvidesCommand(string folder, string command) =>
        _pathExt.Extensions.Any(extension => File.Exists(Path.Combine(folder, command + extension)));

    private static IEnumerable<string> EnumerateFiles(string folder)
    {
        try
        {
            return Directory.GetFiles(folder);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            // A folder on PATH we cannot list is not the same as one with no executables, but
            // nothing downstream can express that yet. Skipping keeps analysis running; the
            // entry model's Inaccessible status is where this becomes visible.
            return Array.Empty<string>();
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
