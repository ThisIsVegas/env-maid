using EnvMaid.App.Models;
using EnvMaid.App.Services;
using EnvMaid.App.Services.Interop;
using Microsoft.Win32;

namespace EnvMaid.App.Tests;

/// <summary>
/// Store-level tests. The pure ones run anywhere; the round-trip ones touch the real
/// HKCU\Environment key and only ever write values under <see cref="Prefix"/> — never Path.
/// </summary>
public class EnvironmentVariableStoreTests
{
    private const string Prefix = "EnvMaidTest_";

    // --- ByteCount: the byte-vs-character discipline -------------------------------------

    [Theory]
    [InlineData(44u, 22u)]
    [InlineData(2u, 1u)]
    [InlineData(0u, 0u)]
    public void ByteCount_HalvesEvenCounts(uint bytes, uint expectedChars)
    {
        Assert.Equal(expectedChars, new ByteCount(bytes).Chars);
    }

    [Theory]
    [InlineData(43u)]
    [InlineData(1u)]
    public void ByteCount_RefusesToHalveOddCounts(uint bytes)
    {
        // Returning null rather than a wrong number is the point: an odd cbData is real and
        // storable (EMP-07), and bytes / 2 would silently drop a byte.
        var count = new ByteCount(bytes);

        Assert.False(count.IsWholeChars);
        Assert.Null(count.Chars);
    }

    // --- Type selection for new values (§11.1 plus the Path policy) ----------------------

    [Theory]
    [InlineData("Path")]
    [InlineData("PATH")]
    [InlineData("path")]
    public void NewPath_IsAlwaysExpandString_EvenWithLiteralEntries(string name)
    {
        Assert.Equal(
            RegistryValueKind.ExpandString,
            EnvironmentVariableStore.TypeForNewValue(name, @"C:\a;C:\b"));
    }

    [Theory]
    [InlineData(@"C:\tools", RegistryValueKind.String)]
    [InlineData("", RegistryValueKind.String)]
    [InlineData("100%", RegistryValueKind.String)]           // one '%' is not a reference
    [InlineData(@"%JAVA_HOME%\bin", RegistryValueKind.ExpandString)]
    public void NewNonPathVariable_SniffsContentForAVariableReference(string raw, RegistryValueKind expected)
    {
        Assert.Equal(expected, EnvironmentVariableStore.TypeForNewValue("MY_TOOL", raw));
    }

    // --- Round-trips against the real registry -------------------------------------------

    [Fact]
    public void Write_Read_Delete_RoundTripsExactly()
    {
        var store = new EnvironmentVariableStore();
        var name = Prefix + nameof(Write_Read_Delete_RoundTripsExactly);

        try
        {
            var value = VariableValue.Of(RegistryValueKind.ExpandString, @"%SystemRoot%\system32;C:\a");
            store.Write(PathScope.User, name, value);

            var read = store.Read(PathScope.User, name);

            Assert.True(read.Present);
            Assert.Equal(value.Type, read.Type);
            Assert.Equal(value.RawData, read.RawData);   // unexpanded, byte for byte
        }
        finally
        {
            store.Delete(PathScope.User, name);
        }

        Assert.False(store.Read(PathScope.User, name).Present);
    }

    [Fact]
    public void Read_NeverExpandsVariableReferences()
    {
        // Regression: RegGetValueW expands REG_EXPAND_SZ by default AND then reports the type
        // as REG_SZ. Both halves are destructive — the save path would write C:\WINDOWS where
        // the user wrote %SystemRoot%, and downgrade the value type while doing it.
        var store = new EnvironmentVariableStore();
        var name = Prefix + nameof(Read_NeverExpandsVariableReferences);

        try
        {
            store.Write(PathScope.User, name, VariableValue.Of(RegistryValueKind.ExpandString, @"%SystemRoot%\x"));

            var read = store.Read(PathScope.User, name);

            Assert.Equal(@"%SystemRoot%\x", read.RawData);
            Assert.DoesNotContain(@"C:\", read.RawData, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(RegistryValueKind.ExpandString, read.Type);
        }
        finally
        {
            store.Delete(PathScope.User, name);
        }
    }

    [Fact]
    public void AbsentAndPresentButEmpty_AreDistinguishable()
    {
        var store = new EnvironmentVariableStore();
        var name = Prefix + nameof(AbsentAndPresentButEmpty_AreDistinguishable);

        Assert.False(store.Read(PathScope.User, name).Present);

        try
        {
            store.Write(PathScope.User, name, VariableValue.Of(RegistryValueKind.String, string.Empty));

            var read = store.Read(PathScope.User, name);

            Assert.True(read.Present);
            Assert.Equal(string.Empty, read.RawData);
        }
        finally
        {
            store.Delete(PathScope.User, name);
        }
    }

    [Fact]
    public void Delete_OnAnAbsentValue_IsNotAnError()
    {
        new EnvironmentVariableStore().Delete(PathScope.User, Prefix + "NeverWritten");
    }

    [Fact]
    public void Rename_KeepsTheOldValueWhenTheNewNameIsTaken()
    {
        var store = new EnvironmentVariableStore();
        var oldName = Prefix + "RenameSource";
        var newName = Prefix + "RenameTarget";

        try
        {
            store.Write(PathScope.User, oldName, VariableValue.Of(RegistryValueKind.String, "source"));
            store.Write(PathScope.User, newName, VariableValue.Of(RegistryValueKind.String, "occupied"));

            Assert.Throws<InvalidOperationException>(() => store.Rename(PathScope.User, oldName, newName));

            // Nothing was lost: the §11 rule is that a failure leaves the old value intact.
            Assert.Equal("source", store.Read(PathScope.User, oldName).RawData);
            Assert.Equal("occupied", store.Read(PathScope.User, newName).RawData);
        }
        finally
        {
            store.Delete(PathScope.User, oldName);
            store.Delete(PathScope.User, newName);
        }
    }

    [Fact]
    public void Rename_CarriesTypeAndDataAcross_ThenRemovesTheOldName()
    {
        var store = new EnvironmentVariableStore();
        var oldName = Prefix + "RenameFrom";
        var newName = Prefix + "RenameTo";

        try
        {
            store.Write(PathScope.User, oldName, VariableValue.Of(RegistryValueKind.ExpandString, @"%TEMP%\x"));

            store.Rename(PathScope.User, oldName, newName);

            var moved = store.Read(PathScope.User, newName);
            Assert.Equal(RegistryValueKind.ExpandString, moved.Type);
            Assert.Equal(@"%TEMP%\x", moved.RawData);
            Assert.False(store.Read(PathScope.User, oldName).Present);
        }
        finally
        {
            store.Delete(PathScope.User, oldName);
            store.Delete(PathScope.User, newName);
        }
    }

    [Fact]
    public void ReadingTheRealUserPath_DoesNotThrowAndDoesNotExpand()
    {
        // Read-only smoke test against the value that actually matters. Never written here.
        var value = new EnvironmentVariableStore().Read(PathScope.User, "Path");

        // A machine legitimately may have no User PATH — absence is a pass, not a skip.
        if (value.Present)
            Assert.True(value.Type is RegistryValueKind.ExpandString or RegistryValueKind.String);
    }
}
