using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

public class EnvironmentPathService
{
    public const string ElevatedSetSystemPathArg = "--elevated-set-system-path";

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
        var target = ToEnvironmentVariableTarget(scope);
        var raw = Environment.GetEnvironmentVariable("Path", target);
        if (string.IsNullOrEmpty(raw))
            return Array.Empty<string>();

        return raw.Split(';');
    }

    public void SetEntries(PathScope scope, IEnumerable<string> entries)
    {
        var target = ToEnvironmentVariableTarget(scope);
        var joined = string.Join(';', entries);
        Environment.SetEnvironmentVariable("Path", joined, target);
    }

    public void BroadcastEnvironmentChange()
    {
        var hwndBroadcast = new IntPtr(HWND_BROADCAST_VALUE);
        SendMessageTimeout(hwndBroadcast, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment",
            SMTO_ABORTIFHUNG, 5000, out _);
    }

    private static EnvironmentVariableTarget ToEnvironmentVariableTarget(PathScope scope) =>
        scope == PathScope.User ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Machine;
}
