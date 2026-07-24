using System.IO;
using System.Text.Json;

namespace EnvMaid.App.Services;

public record PathBackup(string Timestamp, string UserPath, string SystemPath);

public class BackupService
{
    private readonly string _backupDir;

    public BackupService()
    {
        _backupDir = Path.Combine(
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
