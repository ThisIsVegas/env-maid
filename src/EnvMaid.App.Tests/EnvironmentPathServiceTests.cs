using EnvMaid.App.Models;
using EnvMaid.App.Services;
using Microsoft.Win32;

namespace EnvMaid.App.Tests;

/// <summary>
/// Covers PATH semantics over the store: the ';' split and join, absent-vs-empty, and the
/// stored-value guard — a Path held as a non-string type must never read as an empty PATH,
/// because saving that back would wipe the real value.
/// See docs/knowledge/windows-environment-variables-reference.md §4.2 and §11.
/// </summary>
public class EnvironmentPathServiceTests
{
    private const string Path = EnvironmentPathService.PathValueName;

    private static (EnvironmentPathService Service, FakeEnvironmentVariableStore Store) Build()
    {
        var store = new FakeEnvironmentVariableStore();
        return (new EnvironmentPathService(store), store);
    }

    private static VariableValue Expand(string raw) =>
        VariableValue.Of(RegistryValueKind.ExpandString, raw);

    [Fact]
    public void StringValue_IsSplitOnSemicolons()
    {
        var (service, store) = Build();
        store.Seed(PathScope.User, Path, Expand(@"C:\a;C:\b;%SystemRoot%\system32"));

        Assert.Equal(
            new[] { @"C:\a", @"C:\b", @"%SystemRoot%\system32" },
            service.GetEntries(PathScope.User));
    }

    [Fact]
    public void AbsentValue_ReadsAsNoEntries()
    {
        var (service, _) = Build();

        Assert.Empty(service.GetEntries(PathScope.User));
        Assert.False(service.GetStoredValue(PathScope.User).Present);
    }

    [Fact]
    public void PresentButEmpty_ReadsAsNoEntries_ButStaysDistinctFromAbsent()
    {
        var (service, store) = Build();
        store.Seed(PathScope.User, Path, Expand(string.Empty));

        Assert.Empty(service.GetEntries(PathScope.User));

        // The distinction survives at the value level, which is what restore needs: writing an
        // empty PATH onto a machine that had none is a different act from leaving it absent.
        var stored = service.GetStoredValue(PathScope.User);
        Assert.True(stored.Present);
        Assert.True(stored.IsPresentAndEmpty);
    }

    [Fact]
    public void UnsupportedType_PropagatesRatherThanReadingAsEmpty()
    {
        var (service, store) = Build();
        store.ReadFailures[(PathScope.User, Path)] =
            new UnsupportedPathValueTypeException(PathScope.User, RegistryValueKind.MultiString);

        Assert.Throws<UnsupportedPathValueTypeException>(() => service.GetEntries(PathScope.User));
    }

    [Fact]
    public void SetEntries_JoinsOnSemicolonsAndDropsEmptyEntries()
    {
        var (service, store) = Build();
        store.Seed(PathScope.User, Path, Expand(@"C:\old"));

        service.SetEntries(PathScope.User, new[] { @"C:\a", "", "   ", @"C:\b" });

        var write = Assert.Single(store.Writes);
        Assert.Equal(@"C:\a;C:\b", write.Value.RawData);
    }

    [Fact]
    public void SetEntries_PreservesTheExistingRegistryType()
    {
        var (service, store) = Build();
        // A PATH someone downgraded to REG_SZ round-trips as REG_SZ; repairing it is an
        // explicit, separate act, not a side effect of saving.
        store.Seed(PathScope.User, Path, VariableValue.Of(RegistryValueKind.String, @"C:\old"));

        service.SetEntries(PathScope.User, new[] { @"C:\a" });

        Assert.Equal(RegistryValueKind.String, Assert.Single(store.Writes).Value.Type);
    }

    [Fact]
    public void SetEntries_OnAnAbsentPath_CreatesItAsExpandString()
    {
        var (service, store) = Build();

        // Literal entries, no %VAR% — content-sniffing would pick REG_SZ here. Path is keyed
        // on the name instead, so a newly created PATH is never born broken.
        service.SetEntries(PathScope.User, new[] { @"C:\a" });

        Assert.Equal(RegistryValueKind.ExpandString, Assert.Single(store.Writes).Value.Type);
    }

    [Fact]
    public void UnsupportedTypeMessage_NamesScopeAndTypeAndRefusesToOverwrite()
    {
        var ex = new UnsupportedPathValueTypeException(PathScope.System, RegistryValueKind.MultiString);

        Assert.Contains("System", ex.Message);
        Assert.Contains("MultiString", ex.Message);
        Assert.Contains("will not overwrite", ex.Message);
    }
}
