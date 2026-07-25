using EnvMaid.App.Models;
using EnvMaid.App.Services;
using Microsoft.Win32;

namespace EnvMaid.App.Tests;

/// <summary>
/// Covers the stored-value guard: a Path held in the registry as a non-string type must
/// never read as an empty PATH, because saving that back would wipe the real value.
/// See docs/knowledge/windows-environment-variables-reference.md §4.2.
/// </summary>
public class EnvironmentPathServiceTests
{
    private static IReadOnlyList<string> Parse(object? raw, RegistryValueKind kind = RegistryValueKind.String) =>
        EnvironmentPathService.ParseStoredValue(raw, PathScope.User, () => kind);

    [Fact]
    public void StringValue_IsSplitOnSemicolons()
    {
        var entries = Parse(@"C:\a;C:\b;%SystemRoot%\system32");

        Assert.Equal(new[] { @"C:\a", @"C:\b", @"%SystemRoot%\system32" }, entries);
    }

    [Fact]
    public void AbsentValue_ReadsAsEmpty()
    {
        Assert.Empty(Parse(null));
    }

    [Fact]
    public void PresentButEmptyString_ReadsAsEmpty()
    {
        // NOTE: absent and present-and-empty are indistinguishable here today. §15 requires
        // them to be distinct so undo can restore a deletion faithfully — that is issue #23's
        // subject, not this guard's.
        Assert.Empty(Parse(string.Empty));
    }

    [Theory]
    [MemberData(nameof(NonStringValues))]
    public void NonStringValue_ThrowsRatherThanReadingAsEmpty(object raw, RegistryValueKind kind)
    {
        var ex = Assert.Throws<UnsupportedPathValueTypeException>(() => Parse(raw, kind));

        Assert.Equal(PathScope.User, ex.Scope);
        Assert.Equal(kind, ex.ValueKind);
    }

    public static TheoryData<object, RegistryValueKind> NonStringValues() => new()
    {
        // What RegistryKey.GetValue actually returns for each type — verified against a
        // real registry key, not assumed.
        { new[] { @"C:\a", @"C:\b" }, RegistryValueKind.MultiString },
        { 1234, RegistryValueKind.DWord },
        { new byte[] { 1, 2, 3 }, RegistryValueKind.Binary },
        { 1234L, RegistryValueKind.QWord },
    };

    [Fact]
    public void UnsupportedTypeMessage_NamesScopeAndTypeAndRefusesToOverwrite()
    {
        var ex = new UnsupportedPathValueTypeException(PathScope.System, RegistryValueKind.MultiString);

        Assert.Contains("System", ex.Message);
        Assert.Contains("MultiString", ex.Message);
        Assert.Contains("will not overwrite", ex.Message);
    }
}
