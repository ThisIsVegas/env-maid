using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

/// <summary>
/// PATH semantics over <see cref="IEnvironmentVariableStore"/>: the <c>;</c> split and join,
/// and the elevation relaunch that writing the System scope needs.
/// </summary>
/// <remarks>
/// Storage — presence, registry type, the raw bytes — belongs to the store. This class never
/// touches the registry itself, which is what makes everything above it fakeable.
/// </remarks>
public class EnvironmentPathService
{
    public const string ElevatedSetSystemPathArg = "--elevated-set-system-path";

    /// <summary>The registry value name backing PATH in both scopes.</summary>
    public const string PathValueName = "Path";

    private readonly IEnvironmentVariableStore _store;

    public EnvironmentPathService(IEnvironmentVariableStore? store = null) =>
        _store = store ?? new EnvironmentVariableStore();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint timeout, out UIntPtr result);

    private const int HWND_BROADCAST_VALUE = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x1A;
    private const uint SMTO_ABORTIFHUNG = 0x2;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relaunches this executable elevated (UAC prompt) to write the System PATH,
    /// since the running process itself cannot upgrade its own token.
    /// </summary>
    public bool TryElevateSetSystemPath(string joinedPath)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not resolve current executable path.");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"{ElevatedSetSystemPathArg} \"{joinedPath}\"",
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            using var process = Process.Start(startInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // User declined the UAC prompt.
            return false;
        }
    }

    /// <summary>Reads the stored PATH for a scope, unexpanded.</summary>
    public IReadOnlyList<string> GetEntries(PathScope scope) =>
        SplitEntries(_store.Read(scope, PathValueName));

    /// <summary>The whole stored value, so callers that care about absent-vs-empty can tell.</summary>
    public VariableValue GetStoredValue(PathScope scope) => _store.Read(scope, PathValueName);

    /// <summary>
    /// Splits a stored PATH into entries.
    /// </summary>
    /// <remarks>
    /// An absent value and an empty one both yield no entries — a PATH with nothing on it is a
    /// PATH with nothing on it. The difference only matters on write, which is why it survives
    /// in <see cref="VariableValue"/> rather than being resolved here.
    /// </remarks>
    public static IReadOnlyList<string> SplitEntries(VariableValue value)
    {
        if (!value.Present || value.RawData.Length == 0)
            return Array.Empty<string>();

        return value.RawData.Split(';');
    }

    public void SetEntries(PathScope scope, IEnumerable<string> entries)
    {
        // Empty entries are tolerated on read but dropped on write: a stray ";;" resolves to the
        // current directory for some consumers, which is never what an editor should persist.
        var joined = string.Join(';', entries.Where(e => !string.IsNullOrWhiteSpace(e)));

        var existing = _store.Read(scope, PathValueName);
        var type = existing.Present
            ? existing.Type
            : EnvironmentVariableStore.TypeForNewValue(PathValueName, joined);

        _store.Write(scope, PathValueName, VariableValue.Of(type, joined));
    }

    public void BroadcastEnvironmentChange()
    {
        var hwndBroadcast = new IntPtr(HWND_BROADCAST_VALUE);
        SendMessageTimeout(hwndBroadcast, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment",
            SMTO_ABORTIFHUNG, 5000, out _);
    }
}
