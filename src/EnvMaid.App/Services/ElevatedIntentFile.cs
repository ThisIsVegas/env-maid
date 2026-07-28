using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

/// <summary>
/// Reads and writes the file that carries intent across the privilege boundary.
/// </summary>
/// <remarks>
/// <para>
/// This file is written by an <b>unelevated</b> process and read by an <b>elevated</b> one, which
/// makes it a privilege-escalation vector if handled casually: another user who can plant or swap
/// it gets the elevated helper to write a System PATH of their choosing, which is persistent code
/// execution on the machine.
/// </para>
/// <para>
/// So: per-user temp directory, a random name created with <see cref="FileMode.CreateNew"/>, an
/// explicit ACL granting only the creating user and Administrators with inheritance switched off,
/// and an owner check plus a reparse-point check before the helper trusts a byte of it.
/// </para>
/// </remarks>
public static class ElevatedIntentFile
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Creates the intent file in the per-user temp directory with a restrictive ACL.
    /// </summary>
    /// <returns>The full path; the caller must delete it in a <c>finally</c>.</returns>
    public static string Create(ElevatedIntent intent)
    {
        // Per-user temp, never a world-writable directory like C:\Windows\Temp.
        var path = Path.Combine(Path.GetTempPath(), $"envmaid-{Guid.NewGuid():N}.json");

        var security = new FileSecurity();
        // Inheritance off: the file's permissions must not depend on whatever the temp folder
        // happens to grant, which can include groups we never intended.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Could not resolve the current user's SID.");
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(administrators, FileSystemRights.FullControl, AccessControlType.Allow));

        // CreateNew: an existing file is an error, not something to overwrite. FileShare.None so
        // nothing can read or swap it while it is being written.
        using (var stream = FileSystemAclExtensions.Create(
                   new FileInfo(path), FileMode.CreateNew, FileSystemRights.WriteData,
                   FileShare.None, bufferSize: 4096, FileOptions.None, security))
        {
            JsonSerializer.Serialize(stream, intent, Json);
        }

        return path;
    }

    /// <summary>
    /// Reads an intent file, refusing anything whose provenance cannot be established.
    /// </summary>
    /// <remarks>
    /// Called from the elevated helper, so every check here runs with the machine's full trust
    /// behind it. A file owned by someone other than the invoking user or an administrator is
    /// rejected outright rather than merely warned about.
    /// </remarks>
    public static ElevatedIntent Read(string path)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
            throw new FileNotFoundException("The intent file does not exist.", path);

        // A reparse point could redirect this read somewhere else entirely.
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new UnauthorizedAccessException("The intent file is a reparse point; refusing to read it.");

        var owner = info.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new UnauthorizedAccessException("The intent file has no resolvable owner.");

        if (!IsTrustedOwner(owner))
            throw new UnauthorizedAccessException(
                $"The intent file is owned by {owner.Value}, which is neither the current user nor an administrator.");

        using var stream = info.Open(new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.None,
        });

        return JsonSerializer.Deserialize<ElevatedIntent>(stream, Json)
            ?? throw new InvalidDataException("The intent file is empty or malformed.");
    }

    /// <summary>Writes the helper's outcome back into the file the parent is waiting on.</summary>
    public static void WriteResult(string path, ElevatedIntent intent, ElevatedResult result)
    {
        intent.Result = result;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, intent, Json);
    }

    private static bool IsTrustedOwner(SecurityIdentifier owner)
    {
        using var identity = WindowsIdentity.GetCurrent();

        // An elevated helper's SID matches the unelevated parent's, so this covers the normal
        // case. Administrators is allowed too, because an admin-owned file is not somewhere an
        // unprivileged attacker could have planted anything.
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        return owner.Equals(administrators)
            || (identity.User is not null && owner.Equals(identity.User))
            || (identity.Owner is not null && owner.Equals(identity.Owner));
    }
}
