using System.Runtime.InteropServices;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

public class EnvironmentPathService
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint timeout, out UIntPtr result);

    private const int HWND_BROADCAST_VALUE = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x1A;
    private const uint SMTO_ABORTIFHUNG = 0x2;

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
