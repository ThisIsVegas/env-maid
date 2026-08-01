using System.IO;
using System.Reflection;

namespace EnvMaid.App.Services;

/// <summary>
/// Effective allowlist of known CLI tool names (without extension), built from an
/// embedded built-in list plus a user-editable file. Used to raise shadow-conflict
/// confidence to "Likely real".
///
/// User file (%APPDATA%\EnvMaid\cli-tools.txt), one entry per line, '#' comments:
///   sometool    -> add to allowlist
///   !builtin    -> suppress a built-in entry (failsafe for a wrong built-in)
/// Adding a name already built-in is a silent no-op (case-insensitive dedupe).
/// </summary>
public class CliToolListService
{
    private const string EmbeddedResourceName = "EnvMaid.App.Resources.cli-tools-builtin.txt";

    private readonly string _userFilePath;
    private HashSet<string> _effective = new(StringComparer.OrdinalIgnoreCase);

    public CliToolListService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EnvMaid", "cli-tools.txt"))
    {
    }

    // Test seam: inject the user-file path.
    public CliToolListService(string userFilePath)
    {
        _userFilePath = userFilePath;
        Reload();
    }

    public string UserFilePath => _userFilePath;

    /// <summary>Names in the embedded built-in list (read-only baseline).</summary>
    public IReadOnlyCollection<string> BuiltInNames { get; private set; } = Array.Empty<string>();

    /// <summary>True if <paramref name="exeName"/> (with or without extension) is a known CLI tool.</summary>
    public bool IsKnownCliTool(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName))
            return false;
        return _effective.Contains(Path.GetFileNameWithoutExtension(exeName));
    }

    /// <summary>Re-read the built-in and user lists, rebuilding the effective allowlist.</summary>
    public void Reload()
    {
        var builtIn = ParseLines(ReadEmbedded())
            .Where(l => !l.StartsWith('!'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        BuiltInNames = builtIn;

        var effective = new HashSet<string>(builtIn, StringComparer.OrdinalIgnoreCase);

        foreach (var line in ParseLines(ReadUserFile()))
        {
            if (line.StartsWith('!'))
                effective.Remove(line[1..].Trim()); // suppress a built-in
            else
                effective.Add(line);                // add (dedupe is automatic)
        }

        _effective = effective;
    }

    private static IEnumerable<string> ParseLines(string? content)
    {
        if (content is null)
            yield break;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            yield return line;
        }
    }

    private static string ReadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {EmbeddedResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private string? ReadUserFile()
    {
        try
        {
            return File.Exists(_userFilePath) ? File.ReadAllText(_userFilePath) : null;
        }
        catch (IOException)
        {
            return null; // unreadable file -> fall back to built-in only
        }
    }
}
