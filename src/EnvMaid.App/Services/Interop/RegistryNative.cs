using System.Runtime.InteropServices;

namespace EnvMaid.App.Services.Interop;

/// <summary>
/// The raw advapi32 surface. Nothing above <see cref="EnvironmentVariableStore"/> may call these.
/// </summary>
/// <remarks>
/// Every entry point is the <c>W</c> variant (§4.3). Both read functions are declared on purpose:
/// <c>RegGetValueW</c> guarantees a terminated string but silently repairs malformation, and
/// <c>RegQueryValueExW</c> shows what is actually stored but hands back an untrusted buffer.
/// Using each for what it is good at is the design; see <see cref="RegistryValueReader"/>.
/// </remarks>
internal static partial class RegistryNative
{
    private const string Advapi32 = "advapi32.dll";

    internal static readonly nint HKEY_CURRENT_USER = unchecked((nint)(int)0x80000001);
    internal static readonly nint HKEY_LOCAL_MACHINE = unchecked((nint)(int)0x80000002);

    internal const uint KEY_QUERY_VALUE = 0x0001;
    internal const uint KEY_SET_VALUE = 0x0002;

    internal const uint RRF_RT_ANY = 0x0000ffff;

    /// <summary>
    /// Suppresses <c>RegGetValueW</c>'s automatic expansion of REG_EXPAND_SZ data.
    /// </summary>
    /// <remarks>
    /// Without this the API expands <c>%VAR%</c> <em>and</em> reports the type as REG_SZ, so a
    /// round-trip would both hardcode the variable references and downgrade the value type —
    /// the destructive rewrite §4.2 of the environment-variable reference describes.
    /// </remarks>
    internal const uint RRF_NOEXPAND = 0x10000000;

    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_MORE_DATA = 234;

    [LibraryImport(Advapi32, EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegOpenKeyEx(
        nint hKey, string subKey, uint options, uint samDesired, out SafeRegistryKeyHandle result);

    [LibraryImport(Advapi32, EntryPoint = "RegCloseKey")]
    internal static partial int RegCloseKey(nint hKey);

    /// <summary>Primary string read. Guarantees the returned data is null-terminated.</summary>
    [LibraryImport(Advapi32, EntryPoint = "RegGetValueW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegGetValue(
        SafeRegistryKeyHandle hKey, string? subKey, string valueName,
        uint flags, out uint type, byte[]? data, ref uint cbData);

    /// <summary>Raw read. Used only to see malformation that <c>RegGetValueW</c> would hide.</summary>
    [LibraryImport(Advapi32, EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegQueryValueEx(
        SafeRegistryKeyHandle hKey, string valueName, nint reserved,
        out uint type, byte[]? data, ref uint cbData);

    [LibraryImport(Advapi32, EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegSetValueEx(
        SafeRegistryKeyHandle hKey, string valueName, uint reserved,
        uint type, byte[] data, uint cbData);

    [LibraryImport(Advapi32, EntryPoint = "RegDeleteValueW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegDeleteValue(SafeRegistryKeyHandle hKey, string valueName);
}
