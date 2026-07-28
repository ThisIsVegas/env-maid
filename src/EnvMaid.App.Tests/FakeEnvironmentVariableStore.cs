using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

/// <summary>
/// In-memory <see cref="IEnvironmentVariableStore"/>. This is the seam that keeps every tier
/// above the store off a real registry — path-service and ViewModel tests use this, and only
/// store tests touch HKCU.
/// </summary>
public sealed class FakeEnvironmentVariableStore : IEnvironmentVariableStore
{
    private readonly Dictionary<(PathScope, string), VariableValue> _values = new();

    /// <summary>Set when the next read of this scope/name should fail, e.g. an unsupported type.</summary>
    public Dictionary<(PathScope, string), Exception> ReadFailures { get; } = new();

    public List<(PathScope Scope, string Name, VariableValue Value)> Writes { get; } = new();

    public List<(PathScope Scope, string Name)> Deletes { get; } = new();

    public void Seed(PathScope scope, string name, VariableValue value) => _values[(scope, name)] = value;

    public VariableValue Read(PathScope scope, string name)
    {
        if (ReadFailures.TryGetValue((scope, name), out var failure))
            throw failure;

        return _values.TryGetValue((scope, name), out var value) ? value : VariableValue.Absent;
    }

    public void Write(PathScope scope, string name, VariableValue value)
    {
        Writes.Add((scope, name, value));
        _values[(scope, name)] = value;
    }

    public void Delete(PathScope scope, string name)
    {
        Deletes.Add((scope, name));
        _values.Remove((scope, name));
    }

    public void Rename(PathScope scope, string oldName, string newName)
    {
        var existing = Read(scope, oldName);
        if (!existing.Present)
            throw new InvalidOperationException($"'{oldName}' does not exist.");

        Write(scope, newName, existing);
        Delete(scope, oldName);
    }
}
