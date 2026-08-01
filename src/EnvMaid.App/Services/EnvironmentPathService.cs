using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    /// <summary>
    /// Takes a path to an intent file, never a PATH value.
    /// </summary>
    /// <remarks>
    /// The old <c>--elevated-set-system-path "&lt;joined&gt;"</c> form put the whole value on the
    /// command line, where it was world-readable, broke on an entry containing a quote, and could
    /// be silently truncated at the command-line limit. A file path has none of those problems.
    /// </remarks>
    public const string ElevatedApplyArg = "--elevated-apply";

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
    private const uint BroadcastTimeoutMs = 5000;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relaunches this executable elevated (UAC prompt) to apply <paramref name="ops"/> to the
    /// System PATH, since the running process cannot upgrade its own token.
    /// </summary>
    /// <remarks>
    /// The helper re-reads the registry, verifies the baseline, applies the ops and writes — all
    /// inside the elevated process, so nothing can change the value between the read and the
    /// write. It does not broadcast; the caller does that once for the whole save.
    /// </remarks>
    public virtual (ElevatedExitCode Code, ElevatedResult? Result) ElevateApply(
        VariableValue baseline, IReadOnlyList<PathOp> ops)
    {
        string? intentPath = null;
        try
        {
            intentPath = ElevatedIntentFile.Create(new ElevatedIntent
            {
                Scope = nameof(PathScope.System),
                ValueName = PathValueName,
                Baseline = PathOpService.BaselineOf(baseline),
                Ops = ops,
            });

            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not resolve current executable path.");

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"{ElevatedApplyArg} \"{intentPath}\"",
                UseShellExecute = true,
                Verb = "runas",
            };

            using var process = Process.Start(startInfo);
            if (process is null)
                return (ElevatedExitCode.NotRun, null);

            process.WaitForExit();

            var code = Enum.IsDefined((ElevatedExitCode)process.ExitCode)
                ? (ElevatedExitCode)process.ExitCode
                : ElevatedExitCode.Failed;

            // The helper wrote its outcome back into the same file.
            return (code, ElevatedIntentFile.Read(intentPath).Result);
        }
        catch (Win32Exception)
        {
            // User declined the UAC prompt.
            return (ElevatedExitCode.NotRun, null);
        }
        catch (Exception ex)
        {
            return (ElevatedExitCode.Failed, new ElevatedResult
            {
                Outcome = new ElevatedOutcome(false, false, false, ex.Message),
            });
        }
        finally
        {
            if (intentPath is not null)
                TryDelete(intentPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover intent file in the user's temp folder is harmless; failing the save
            // over it would not be.
        }
        catch (UnauthorizedAccessException)
        {
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

    /// <summary>Writes the entries and reports whether the stored value read back as written.</summary>
    /// <remarks>
    /// A mismatch is reported, never rolled back (§14 step 6): the write already happened, and
    /// rewriting to "fix" it would be a second unverified write onto a value that is already in
    /// an unexpected state.
    /// </remarks>
    public bool SetEntries(PathScope scope, IEnumerable<string> entries)
    {
        // Empty entries are tolerated on read but dropped on write: a stray ";;" resolves to the
        // current directory for some consumers, which is never what an editor should persist.
        var joined = string.Join(';', entries.Where(e => !string.IsNullOrWhiteSpace(e)));

        var existing = _store.Read(scope, PathValueName);
        var type = existing.Present
            ? existing.Type
            : EnvironmentVariableStore.TypeForNewValue(PathValueName, joined);

        var intended = VariableValue.Of(type, joined);
        _store.Write(scope, PathValueName, intended);

        return _store.Read(scope, PathValueName) == intended;
    }

    /// <summary>
    /// Tells the shell the environment changed, and reports whether it was acknowledged.
    /// </summary>
    /// <remarks>
    /// A failed broadcast is not a failed save and must never roll anything back — the registry
    /// write already happened. It is worth reporting because a stale PATH in a fresh terminal is
    /// otherwise unexplainable to the user.
    /// </remarks>
    public virtual BroadcastResult BroadcastEnvironmentChange()
    {
        var hwndBroadcast = new IntPtr(HWND_BROADCAST_VALUE);
        var returned = SendMessageTimeout(hwndBroadcast, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment",
            SMTO_ABORTIFHUNG, BroadcastTimeoutMs, out _);

        if (returned != IntPtr.Zero)
            return new BroadcastResult(true, null);

        // SendMessageTimeout returns 0 for both a timeout and a failure; GetLastError tells them
        // apart, with 0 meaning the timeout.
        var error = Marshal.GetLastWin32Error();
        return new BroadcastResult(false, error == 0
            ? $"SendMessageTimeout timed out after {BroadcastTimeoutMs}ms"
            : $"SendMessageTimeout failed: {error}");
    }
}
