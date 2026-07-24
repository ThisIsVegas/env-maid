using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

public class PathCompressorTests
{
    // Deterministic, machine-independent variable values for the tests.
    private static readonly Dictionary<string, string?> Vars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = @"C:\Users\me",
        ["LOCALAPPDATA"] = @"C:\Users\me\AppData\Local",
        ["APPDATA"] = @"C:\Users\me\AppData\Roaming",
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
        ["ProgramData"] = @"C:\ProgramData",
        ["SystemRoot"] = @"C:\Windows",
    };

    private readonly PathCompressor _sut = new(name => Vars.GetValueOrDefault(name));

    [Fact]
    public void FoldsInMatchingVariable()
    {
        Assert.Equal(@"%LOCALAPPDATA%\Programs\x", _sut.Compress(@"C:\Users\me\AppData\Local\Programs\x"));
    }

    [Fact]
    public void LongestMatchWins()
    {
        // The folder is inside both %USERPROFILE% and %LOCALAPPDATA%; the longer expansion
        // (%LOCALAPPDATA%) must win because it saves the most characters.
        Assert.Equal(@"%LOCALAPPDATA%", _sut.Compress(@"C:\Users\me\AppData\Local"));
    }

    [Fact]
    public void MatchesCaseInsensitively()
    {
        Assert.Equal(@"%PROGRAMFILES%\Git".Replace("PROGRAMFILES", "ProgramFiles"),
            _sut.Compress(@"c:\program files\Git"));
    }

    [Fact]
    public void OnlyMatchesWholeSegment()
    {
        // "C:\ProgramData" must not swallow the "X" in "C:\ProgramDataX".
        Assert.Equal(@"C:\ProgramDataX\tool", _sut.Compress(@"C:\ProgramDataX\tool"));
    }

    [Fact]
    public void ExactFolderCompressesToBareToken()
    {
        Assert.Equal(@"%SystemRoot%", _sut.Compress(@"C:\Windows"));
    }

    [Theory]
    [InlineData(@"D:\Tools\x")]                    // matches no known variable
    [InlineData(@"%JAVA_HOME%\bin")]               // already uses a variable
    [InlineData("")]
    public void LeavesUnchangedWhenNoSafeMatch(string input)
    {
        Assert.Equal(input, _sut.Compress(input));
    }

    [Fact]
    public void SkipsVariablesUndefinedOnThisMachine()
    {
        var compressor = new PathCompressor(_ => null); // nothing defined
        Assert.Equal(@"C:\Users\me\AppData\Local\x", compressor.Compress(@"C:\Users\me\AppData\Local\x"));
    }
}
