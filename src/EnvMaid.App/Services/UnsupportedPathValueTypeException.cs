using EnvMaid.App.Models;
using Microsoft.Win32;

namespace EnvMaid.App.Services;

/// <summary>
/// Thrown when the registry holds an environment variable that is present but not a string
/// (for example <c>REG_MULTI_SZ</c>, <c>REG_DWORD</c>, or <c>REG_BINARY</c>).
///
/// This is deliberately an exception rather than a silent empty result: reading such a
/// value as empty and later saving would overwrite the real value with nothing. The value
/// is readable only as a non-string type, so EnvMaid refuses to interpret it rather than
/// guess. EMP-06 confirmed the environment builder skips such values outright, so the
/// variable is absent from every process's environment while it stays in this state.
/// </summary>
public class UnsupportedPathValueTypeException : Exception
{
    public PathScope Scope { get; }

    public RegistryValueKind ValueKind { get; }

    /// <summary>The registry value name, e.g. <c>Path</c>.</summary>
    public string VariableName { get; }

    public UnsupportedPathValueTypeException(PathScope scope, RegistryValueKind valueKind, string variableName = "Path")
        : base($"The {scope} {variableName} is stored as {valueKind}, which EnvMaid cannot safely read or edit. " +
               "Expected REG_EXPAND_SZ (or REG_SZ). Repair the value type before continuing; " +
               "EnvMaid will not overwrite it.")
    {
        Scope = scope;
        ValueKind = valueKind;
        VariableName = variableName;
    }
}
