using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using EnvMaid.App.Models;
using Microsoft.Win32;

namespace EnvMaid.App.Services;

public class EnvironmentPathService
{
    public const string ElevatedSetSystemPathArg = "--elevated-set-system-path";

    // Registry locations backing the User and System PATH. We read/write here rather
    // than via Environment.GetEnvironmentVariable so that entries like "%JAVA_HOME%\bin"
    // survive a round-trip: the Environment API expands %VARS% on read, which would make
    // Save rewrite them into hardcoded literals and destroy the variable reference.
    private const string UserEnvKey = @"Environment";
    private const string SystemEnvKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

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

    public IReadOnlyList<string> GetEntries(PathScope scope)
    {
        using var key = OpenEnvKey(scope, writable: false);
        // DoNotExpandEnvironmentNames keeps "%VAR%\bin" literal instead of expanding it.
        var raw = key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames);

        return ParseStoredValue(raw, scope, () => key!.GetValueKind("Path"));
    }

    /// <summary>
    /// Turns the raw object the registry handed back into PATH entries.
    /// Split out from <see cref="GetEntries"/> so the type guard is testable without a registry.
    /// </summary>
    /// <param name="raw">The value as returned by <c>RegistryKey.GetValue</c>; null when absent.</param>
    /// <param name="scope">Scope, for the error message.</param>
    /// <param name="readKind">Reads the value's registry type. Only called for the error path.</param>
    public static IReadOnlyList<string> ParseStoredValue(
        object? raw, PathScope scope, Func<RegistryValueKind> readKind)
    {
        // A Path stored as REG_MULTI_SZ/REG_DWORD/REG_BINARY comes back as string[]/int/byte[].
        // Casting that with "as string" yields null, which would read as an EMPTY path — and
        // saving that back would wipe the real value. That is the destructive round-trip §4.2
        // of the environment-variable reference calls out. Refuse rather than report empty.
        if (raw is not null and not string)
            throw new UnsupportedPathValueTypeException(scope, readKind());

        var text = raw as string;
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        return text.Split(';');
    }

    public void SetEntries(PathScope scope, IEnumerable<string> entries)
    {
        var joined = string.Join(';', entries);
        using var key = OpenEnvKey(scope, writable: true)
            ?? throw new InvalidOperationException($"Could not open the {scope} environment registry key for writing.");
        // ExpandString (REG_EXPAND_SZ) is the type Windows expects for PATH, so any
        // %VAR% references we preserved on read stay expandable at use time.
        key.SetValue("Path", joined, RegistryValueKind.ExpandString);
    }

    private static RegistryKey? OpenEnvKey(PathScope scope, bool writable) =>
        scope == PathScope.User
            ? Registry.CurrentUser.OpenSubKey(UserEnvKey, writable)
            : Registry.LocalMachine.OpenSubKey(SystemEnvKey, writable);

    public void BroadcastEnvironmentChange()
    {
        var hwndBroadcast = new IntPtr(HWND_BROADCAST_VALUE);
        SendMessageTimeout(hwndBroadcast, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment",
            SMTO_ABORTIFHUNG, 5000, out _);
    }
}
