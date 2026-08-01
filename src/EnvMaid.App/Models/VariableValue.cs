using Microsoft.Win32;

namespace EnvMaid.App.Models;

/// <summary>
/// One environment variable as it is stored in the registry: present or not, its registry
/// type, and its exact unexpanded data.
/// </summary>
/// <remarks>
/// <para>
/// Absence is <em>not</em> a flavour of emptiness. <c>Present == false</c> is the only
/// representation of "the registry value does not exist"; <c>Present == true</c> with
/// <c>RawData == ""</c> is a real and different state. Collapsing the two is how a restore
/// ends up writing an empty PATH onto a machine that simply had no User PATH.
/// </para>
/// <para>
/// <see cref="Type"/> is meaningless when <see cref="Present"/> is false, and is captured on
/// read so a write can round-trip it rather than downgrading REG_EXPAND_SZ to REG_SZ.
/// </para>
/// </remarks>
public readonly record struct VariableValue(bool Present, RegistryValueKind Type, string RawData)
{
    public static VariableValue Absent { get; } = new(false, RegistryValueKind.Unknown, string.Empty);

    public static VariableValue Of(RegistryValueKind type, string rawData) => new(true, type, rawData);

    /// <summary>True when the value exists but holds no characters (§11's "Set empty").</summary>
    public bool IsPresentAndEmpty => Present && RawData.Length == 0;
}
