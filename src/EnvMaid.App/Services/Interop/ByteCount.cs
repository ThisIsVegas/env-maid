namespace EnvMaid.App.Services.Interop;

/// <summary>
/// A registry data length. Always a <em>byte</em> count, never a character count.
/// </summary>
/// <remarks>
/// Byte-vs-character confusion is the classic registry bug (§4.3 of the environment-variable
/// reference), so the halving is behind a type rather than written inline. <see cref="Chars"/>
/// returns <c>null</c> for an odd count instead of truncating: an odd <c>cbData</c> is a real,
/// storable state (EMP-07), and <c>cbData / 2</c> would silently lose the trailing byte.
/// </remarks>
public readonly record struct ByteCount(uint Bytes)
{
    public bool IsWholeChars => Bytes % 2 == 0;

    /// <summary>UTF-16 character count, or <c>null</c> when the byte count is odd.</summary>
    public uint? Chars => IsWholeChars ? Bytes / 2 : null;

    public override string ToString() =>
        IsWholeChars ? $"{Bytes}B/{Bytes / 2}ch" : $"{Bytes}B (odd)";
}
