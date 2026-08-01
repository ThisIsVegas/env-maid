using System.IO;
using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

public class CliToolListServiceTests
{
    [Fact]
    public void MissingUserFile_UsesBuiltInOnly()
    {
        var sut = new CliToolListService(NonexistentFile());

        Assert.True(sut.IsKnownCliTool("node"));      // built-in
        Assert.False(sut.IsKnownCliTool("acmecli"));  // not anywhere
    }

    [Fact]
    public void IsKnownCliTool_IgnoresExtensionAndCase()
    {
        var sut = new CliToolListService(NonexistentFile());

        Assert.True(sut.IsKnownCliTool("Node.EXE"));
        Assert.True(sut.IsKnownCliTool("GIT"));
    }

    [Fact]
    public void UserFile_AddsCustomTool()
    {
        var file = TempFileWith("mycustomtool\n");
        var sut = new CliToolListService(file);

        Assert.True(sut.IsKnownCliTool("mycustomtool"));
    }

    [Fact]
    public void UserFile_BangPrefix_SuppressesBuiltIn()
    {
        var file = TempFileWith("!node\n");
        var sut = new CliToolListService(file);

        Assert.False(sut.IsKnownCliTool("node")); // built-in suppressed
        Assert.True(sut.IsKnownCliTool("git"));   // other built-ins intact
    }

    [Fact]
    public void UserFile_DuplicateOfBuiltIn_IsNoOp()
    {
        var file = TempFileWith("node\nNODE\n");
        var sut = new CliToolListService(file);

        Assert.True(sut.IsKnownCliTool("node"));
    }

    [Fact]
    public void UserFile_CommentsAndBlankLines_Ignored()
    {
        var file = TempFileWith("# a comment\n\n   \nmytool\n");
        var sut = new CliToolListService(file);

        Assert.True(sut.IsKnownCliTool("mytool"));
        Assert.False(sut.IsKnownCliTool("#"));
    }

    [Fact]
    public void Reload_PicksUpFileChanges()
    {
        var file = NonexistentFile();
        var sut = new CliToolListService(file);
        Assert.False(sut.IsKnownCliTool("laterTool"));

        File.WriteAllText(file, "laterTool\n");
        sut.Reload();

        Assert.True(sut.IsKnownCliTool("laterTool"));
    }

    private static string NonexistentFile() =>
        Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");

    private static string TempFileWith(string content)
    {
        var file = NonexistentFile();
        File.WriteAllText(file, content);
        return file;
    }
}
