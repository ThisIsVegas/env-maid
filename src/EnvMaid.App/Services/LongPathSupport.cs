using Microsoft.Win32;

namespace EnvMaid.App.Services;

/// <summary>
/// Whether this machine is configured to allow paths past the classic 260-character limit.
/// </summary>
/// <remarks>
/// Only affects how loudly a long path is reported, never whether EnvMaid acts on it. A long
/// path is usually a working directory rather than junk, so it is never auto-selected either way.
/// </remarks>
public class LongPathSupport
{
    /// <summary>The classic <c>MAX_PATH</c> ceiling.</summary>
    public const int MaxPath = 260;

    private const string FileSystemKey = @"SYSTEM\CurrentControlSet\Control\FileSystem";

    private readonly Lazy<bool> _enabled;

    public LongPathSupport() : this(ReadMachineSetting) { }

    /// <param name="readSetting">Test seam: supplies the machine's setting.</param>
    public LongPathSupport(Func<bool> readSetting) => _enabled = new Lazy<bool>(readSetting);

    public bool IsEnabled => _enabled.Value;

    private static bool ReadMachineSetting()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(FileSystemKey);
            return key?.GetValue("LongPathsEnabled") is int value && value == 1;
        }
        catch (Exception)
        {
            // Absent, unreadable, or an unexpected type — all mean "assume the limit applies",
            // which produces the more cautious message rather than a crash on a read-only key.
            return false;
        }
    }
}
