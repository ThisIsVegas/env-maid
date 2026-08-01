using System.IO;
using System.Text.Json;

namespace EnvMaid.App.Services;

public record PathBackup(string Timestamp, string UserPath, string SystemPath);

public class BackupService
{
    private readonly string _backupDir;

    /// <param name="backupDirectory">
    /// Where backups live. Defaults to %APPDATA%\EnvMaid\backups; tests pass a temp folder so
    /// they never write into the real one.
    /// </param>
    public BackupService(string? backupDirectory = null)
    {
        _backupDir = backupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EnvMaid", "backups");
        Directory.CreateDirectory(_backupDir);
    }

    public string CreateBackup(IEnumerable<string> userEntries, IEnumerable<string> systemEntries)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var file = Path.Combine(_backupDir, $"backup-{timestamp}.json");

        var backup = new PathBackup(timestamp, string.Join(';', userEntries), string.Join(';', systemEntries));
        File.WriteAllText(file, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));

        return file;
    }

    /// <summary>Write the current PATH to a user-chosen file (Export). Same JSON shape as a
    /// backup, so an exported profile can be re-imported or restored interchangeably. %VAR%
    /// entries are written verbatim, keeping the profile portable across machines.</summary>
    public void ExportTo(string filePath, IEnumerable<string> userEntries, IEnumerable<string> systemEntries)
    {
        var backup = new PathBackup(
            DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            string.Join(';', userEntries),
            string.Join(';', systemEntries));
        File.WriteAllText(filePath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Read a PATH profile from a user-chosen file (Import). Same parser as a
    /// backup restore.</summary>
    public PathBackup ImportFrom(string filePath) => LoadBackup(filePath);

    public IReadOnlyList<FileInfo> ListBackups() =>
        new DirectoryInfo(_backupDir)
            .GetFiles("backup-*.json")
            .OrderByDescending(f => f.Name)
            .ToList();

    public PathBackup LoadBackup(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<PathBackup>(json)
            ?? throw new InvalidDataException("Backup file could not be parsed.");
    }
}
