using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

public class PathNormalizerTests
{
    private readonly PathNormalizer _sut = new();

    [Theory]
    [InlineData(@"C:\bin\", @"C:\bin")]           // trailing backslash trimmed
    [InlineData(@"C:\bin", @"C:\bin")]            // already canonical
    [InlineData(@"C:\a\..\bin", @"C:\bin")]       // .. collapsed
    [InlineData(@"C:\a\.\bin", @"C:\a\bin")]      // . collapsed
    [InlineData(@"C:\bin/", @"C:\bin")]           // forward slash trailing trimmed
    public void Literal_IsCanonicalized(string input, string expected)
    {
        Assert.Equal(expected, _sut.Normalize(input));
    }

    [Fact]
    public void DriveRoot_KeepsItsSlash()
    {
        Assert.Equal(@"C:\", _sut.Normalize(@"C:\"));
    }

    [Theory]
    [InlineData(@"%JAVA_HOME%\bin", @"%JAVA_HOME%\bin")]      // variable reference untouched
    [InlineData(@"%JAVA_HOME%\bin\", @"%JAVA_HOME%\bin")]     // only the trailing slash trimmed
    [InlineData(@"%LOCALAPPDATA%\a\..\b", @"%LOCALAPPDATA%\a\..\b")] // .. NOT collapsed (would need expansion)
    public void VariableReference_IsPreservedApartFromTrailingSlash(string input, string expected)
    {
        Assert.Equal(expected, _sut.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_ReturnedUnchanged(string input)
    {
        Assert.Equal(input, _sut.Normalize(input));
    }
}
