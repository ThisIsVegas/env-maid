using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

/// <summary>
/// The two length boundaries. Nothing below EnvMaid enforces the hard one — a probe wrote 40,000
/// characters to the registry and read them back exactly — so these are the only gate there is.
/// </summary>
public class PathLengthLimitsTests
{
    [Theory]
    [InlineData(0, PathLengthBand.Ok)]
    [InlineData(2047, PathLengthBand.Ok)]
    [InlineData(2048, PathLengthBand.Caution)]
    [InlineData(32767, PathLengthBand.Caution)]
    [InlineData(32768, PathLengthBand.TooLong)]
    public void BandFor_SwitchesExactlyAtTheBoundaries(int characters, PathLengthBand expected)
    {
        Assert.Equal(expected, PathLengthLimits.BandFor(characters));
    }

    [Fact]
    public void ExactlyAtTheWriteLimit_IsStillWritable()
    {
        Assert.True(PathLengthLimits.IsWritable(PathLengthLimits.HardMaximum));
        Assert.False(PathLengthLimits.IsWritable(PathLengthLimits.HardMaximum + 1));
    }
}
