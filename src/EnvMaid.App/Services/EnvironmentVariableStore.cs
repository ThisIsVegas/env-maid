using System.ComponentModel;
using System.IO;
using System.Text;
using EnvMaid.App.Models;
using EnvMaid.App.Services.Interop;
using Microsoft.Win32;
using static EnvMaid.App.Services.Interop.RegistryNative;

namespace EnvMaid.App.Services;

/// <summary>
/// The §11 storage contract: read, write, delete and rename one named environment variable
/// in one scope. This is the <em>only</em> class that touches the registry — everything above
/// it fakes this interface in tests, so any P/Invoke leaking upward becomes untestable.
/// </summary>
/// <remarks>
/// It owns storage, not PATH semantics. Splitting on <c>;</c>, entry ordering and the
/// elevation relaunch belong to <see cref="EnvironmentPathService"/> one level up.
/// </remarks>
public interface IEnvironmentVariableStore
{
    VariableValue Read(PathScope scope, string name);

    void Write(PathScope scope, string name, VariableValue value);

    void Delete(PathScope scope, string name);

    void Rename(PathScope scope, string oldName, string newName);
}

/// <inheritdoc cref="IEnvironmentVariableStore"/>
public sealed class EnvironmentVariableStore : IEnvironmentVariableStore
{
    private const string UserEnvKey = @"Environment";
    private const string SystemEnvKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    /// <summary>
    /// The value name whose registry type is fixed by policy rather than sniffed from content.
    /// </summary>
    private const string PathValueName = "Path";

    /// <summary>Bound on the two-call retry, so an installer rewriting PATH in a loop cannot spin us forever.</summary>
    private const int MaxSizeRetries = 5;

    /// <summary>
    /// Accept any type — the guard below reports an unsupported one — but never let the API
    /// expand REG_EXPAND_SZ data. Expansion would hardcode <c>%VAR%</c> references on save and
    /// make the value report itself as REG_SZ, losing the type on every round-trip.
    /// </summary>
    private const uint ReadFlags = RRF_RT_ANY | RRF_NOEXPAND;

    public VariableValue Read(PathScope scope, string name)
    {
        using var key = Open(scope, KEY_QUERY_VALUE);

        uint cb = 0;
        var rc = RegGetValue(key, null, name, ReadFlags, out var type, null, ref cb);
        if (rc == ERROR_FILE_NOT_FOUND)
            return VariableValue.Absent;
        ThrowIfFailed(rc, scope, name);

        byte[] data;
        var retries = 0;
        while (true)
        {
            // On ERROR_MORE_DATA the buffer contents are undefined (§4.3), so it is discarded
            // and reallocated at the size the API just reported — never partially trusted.
            var buffer = new byte[cb];
            rc = RegGetValue(key, null, name, ReadFlags, out type, buffer, ref cb);
            if (rc != ERROR_MORE_DATA)
            {
                ThrowIfFailed(rc, scope, name);
                data = buffer[..(int)Math.Min(cb, (uint)buffer.Length)];
                break;
            }

            if (++retries > MaxSizeRetries)
                throw new IOException(
                    $"The {scope} {name} value kept growing while being read ({MaxSizeRetries} attempts). " +
                    "Another process is rewriting it; try again.");
        }

        var kind = ToValueKind(type);
        if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            throw new UnsupportedPathValueTypeException(scope, kind, name);

        return VariableValue.Of(kind, Decode(new ByteCount((uint)data.Length), data));
    }

    public void Write(PathScope scope, string name, VariableValue value)
    {
        if (!value.Present)
            throw new ArgumentException(
                "Writing an absent value is meaningless; call Delete to remove it.", nameof(value));

        var kind = value.Type is RegistryValueKind.String or RegistryValueKind.ExpandString
            ? value.Type
            : TypeForNewValue(name, value.RawData);

        // The terminating null is part of the value: RegSetValueExW stores exactly cbData bytes,
        // and a string written without it is the malformed state EMP-07 covers.
        var data = Encoding.Unicode.GetBytes(value.RawData + '\0');

        using var key = Open(scope, KEY_SET_VALUE);
        var rc = RegSetValueEx(key, name, 0, (uint)kind, data, (uint)data.Length);
        ThrowIfFailed(rc, scope, name);
    }

    public void Delete(PathScope scope, string name)
    {
        using var key = Open(scope, KEY_SET_VALUE);
        var rc = RegDeleteValue(key, name);
        if (rc == ERROR_FILE_NOT_FOUND)
            return;
        ThrowIfFailed(rc, scope, name);
    }

    /// <summary>
    /// Renames a value by writing the new name, verifying it reads back, and only then removing
    /// the old one.
    /// </summary>
    /// <remarks>
    /// The ordering is a correctness rule, not a preference (§11 [POLICY]): a crash between the
    /// steps must leave the <em>old</em> value intact, never neither. Deleting first would risk
    /// losing the value entirely, however much simpler it reads.
    /// </remarks>
    public void Rename(PathScope scope, string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            return;

        var existing = Read(scope, oldName);
        if (!existing.Present)
            throw new InvalidOperationException($"The {scope} variable '{oldName}' does not exist.");

        if (Read(scope, newName).Present)
            throw new InvalidOperationException($"The {scope} variable '{newName}' already exists.");

        Write(scope, newName, existing);

        var written = Read(scope, newName);
        if (written != existing)
            throw new IOException(
                $"'{newName}' did not read back as written, so '{oldName}' was left in place.");

        Delete(scope, oldName);
    }

    /// <summary>
    /// Picks the registry type for a value that does not exist yet: REG_EXPAND_SZ when the data
    /// contains a <c>%…%</c> reference, REG_SZ otherwise (§11.1).
    /// </summary>
    /// <remarks>
    /// <c>Path</c> is an exception by policy and is keyed on the <em>name</em>: content-sniffing
    /// it would produce a REG_SZ PATH whenever the initial entries happen to be literal, which is
    /// exactly the broken state the repair flow exists to fix.
    /// </remarks>
    public static RegistryValueKind TypeForNewValue(string name, string rawData)
    {
        if (string.Equals(name, PathValueName, StringComparison.OrdinalIgnoreCase))
            return RegistryValueKind.ExpandString;

        return ContainsVariableReference(rawData)
            ? RegistryValueKind.ExpandString
            : RegistryValueKind.String;
    }

    private static bool ContainsVariableReference(string text)
    {
        var open = text.IndexOf('%');
        return open >= 0 && text.IndexOf('%', open + 1) > open;
    }

    /// <summary>
    /// Turns the bytes the registry returned into a string, dropping the terminator.
    /// </summary>
    /// <remarks>
    /// An odd byte count cannot be halved into characters, so the trailing byte is dropped rather
    /// than letting <c>ByteCount.Chars</c> silently truncate — the value is malformed either way,
    /// and this is the read path, which tolerates. <c>RegGetValueW</c> guarantees termination, so
    /// a trailing NUL here is the API's, not data.
    /// </remarks>
    private static string Decode(ByteCount count, byte[] data)
    {
        var usable = (int)(count.Chars ?? (count.Bytes - 1) / 2) * 2;
        if (usable <= 0)
            return string.Empty;

        var text = Encoding.Unicode.GetString(data, 0, usable);
        return text.TrimEnd('\0');
    }

    private static RegistryValueKind ToValueKind(uint type) => (RegistryValueKind)(int)type;

    private static SafeRegistryKeyHandle Open(PathScope scope, uint rights)
    {
        // Least privilege by construction (§4.3): the caller states read or write, never both,
        // and KEY_ALL_ACCESS is not reachable from here.
        var (root, subKey) = scope == PathScope.User
            ? (HKEY_CURRENT_USER, UserEnvKey)
            : (HKEY_LOCAL_MACHINE, SystemEnvKey);

        var rc = RegOpenKeyEx(root, subKey, 0, rights, out var handle);
        if (rc != ERROR_SUCCESS)
        {
            handle.Dispose();
            throw ToException(rc, $"Could not open the {scope} environment registry key.");
        }

        return handle;
    }

    private static void ThrowIfFailed(int rc, PathScope scope, string name)
    {
        if (rc != ERROR_SUCCESS)
            throw ToException(rc, $"Registry operation on the {scope} '{name}' value failed.");
    }

    private static Exception ToException(int rc, string context) => rc switch
    {
        ERROR_ACCESS_DENIED => new UnauthorizedAccessException($"{context} (access denied)"),
        _ => new Win32Exception(rc, $"{context} ({new Win32Exception(rc).Message})"),
    };
}
