using Microsoft.Win32.SafeHandles;

namespace EnvMaid.App.Services.Interop;

/// <summary>
/// Owns an <c>HKEY</c> returned by <c>RegOpenKeyExW</c>.
/// </summary>
/// <remarks>
/// A raw <c>nint</c> would leak on an error path, and a leaked key handle is invisible until
/// some later open fails. The finalizer closes it even when a <c>using</c> is forgotten.
/// </remarks>
internal sealed class SafeRegistryKeyHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeRegistryKeyHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle() => RegistryNative.RegCloseKey(handle) == 0;
}
