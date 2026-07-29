using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

/// <summary>
/// PATHEXT is authoritative and user-configurable, which is why it is read from the environment
/// rather than hardcoded — the previously hardcoded set omitted .COM entirely, and .COM sorts
/// first in the shipped default.
/// </summary>
public class PathExtServiceTests
{
    private static PathExtService With(string? value) => new(() => value);

    [Fact]
    public void ReadsTheOrderFromTheEnvironment()
    {
        var sut = With(".COM;.EXE;.BAT");

        Assert.Equal(new[] { ".com", ".exe", ".bat" }, sut.Extensions);
        Assert.True(sut.PrecedenceOf(".com") < sut.PrecedenceOf(".exe"));
    }

    [Fact]
    public void ACustomisedOrderChangesTheWinner()
    {
        // Measured on a real machine: reversing PATHEXT reverses which file runs.
        Assert.True(With(".EXE;.COM").PrecedenceOf(".exe") < With(".EXE;.COM").PrecedenceOf(".com"));
    }

    [Fact]
    public void AnExtensionNotListed_IsNotACommand()
    {
        var sut = With(".COM;.EXE;.BAT;.CMD");

        // A DLL-only folder on PATH is legitimate — the loader searches PATH — but nothing runs
        // by typing its bare name.
        Assert.False(sut.IsCommandExtension(".dll"));
        Assert.False(sut.IsCommandExtension(".ps1"));
        Assert.Equal(-1, sut.PrecedenceOf(".txt"));
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData("exe")]
    [InlineData(".EXE")]
    [InlineData("  .Exe  ")]
    public void ExtensionLookupIsForgivingAboutFormatting(string extension)
    {
        Assert.True(With(".COM;.EXE").IsCommandExtension(extension));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingPathExt_FallsBackToTheDocumentedDefault(string? value)
    {
        var sut = With(value);

        Assert.Equal(".com", sut.Extensions[0]);
        Assert.True(sut.IsCommandExtension(".msc"));
    }

    [Fact]
    public void DocumentedDefault_LeadsWithComNotExe()
    {
        // The `path` command's own documentation claims .exe precedes .com. That was measured to
        // be wrong; it appears to carry MS-DOS-era text.
        Assert.StartsWith(".COM;.EXE", PathExtService.DocumentedDefault);
    }

    [Fact]
    public void ToleratesRaggedValues()
    {
        var sut = With(".COM;;.EXE ; .BAT;.COM");

        Assert.Equal(new[] { ".com", ".exe", ".bat" }, sut.Extensions);
    }
}
