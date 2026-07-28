// PROBE for issue #26 — validating the interop DESIGN before committing to it.
//   Q7  can the two-call design actually DETECT a malformed value? #7 cited
//       EMP-07 reachability as a deciding factor; if detection does not work,
//       that argument collapses.
//   Q2  does SafeRegistryHandle work with LibraryImport / RegOpenKeyExW?
//   Q3  what does ERROR_MORE_DATA actually do in the two-call dance?
//   Q4  byte-vs-character: prove the wrapper catches an odd cbData.
// SAFETY: only EnvMaidIop_* values under HKCU\Environment. Never `Path`.

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static partial class Iop
{
    // --- Handle lifetime (Q2): SafeRegistryHandle owns the key handle -------
    internal sealed class SafeRegHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeRegHandle() : base(true) { }
        protected override bool ReleaseHandle() => RegCloseKey(handle) == 0;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegOpenKeyEx(nint hKey, string subKey, uint options, uint sam, out SafeRegHandle result);

    [LibraryImport("advapi32.dll", EntryPoint = "RegCloseKey")]
    private static partial int RegCloseKey(nint hKey);

    [LibraryImport("advapi32.dll", EntryPoint = "RegGetValueW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegGetValue(SafeRegHandle hKey, string? sub, string value, uint flags, out uint type, byte[]? data, ref uint cbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegQueryValueEx(SafeRegHandle hKey, string value, nint reserved, out uint type, byte[]? data, ref uint cbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegSetValueEx(SafeRegHandle hKey, string value, uint reserved, uint type, byte[] data, uint cbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteValueW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegDeleteValue(SafeRegHandle hKey, string value);

    private static readonly nint HKCU = unchecked((nint)(int)0x80000001);
    private const uint KEY_QUERY_VALUE = 0x0001, KEY_SET_VALUE = 0x0002;
    private const uint REG_SZ = 1, REG_EXPAND_SZ = 2;
    private const uint RRF_RT_ANY = 0x0000ffff;
    private const int ERROR_MORE_DATA = 234, ERROR_FILE_NOT_FOUND = 2;
    private const string Key = @"Environment";
    private const string Prefix = "EnvMaidIop_";

    // --- Q4: byte-vs-character discipline as a TYPE, not loose ints ---------
    private readonly record struct ByteCount(uint Bytes)
    {
        public bool IsWholeChars => Bytes % 2 == 0;
        /// <summary>Chars only when the count is even; odd means malformed.</summary>
        public uint? Chars => IsWholeChars ? Bytes / 2 : null;
        public override string ToString() => $"{Bytes}B" + (IsWholeChars ? $"/{Bytes / 2}ch" : " (ODD!)");
    }

    private static SafeRegHandle Open(uint rights)
    {
        var rc = RegOpenKeyEx(HKCU, Key, 0, rights, out var h);
        if (rc != 0) throw new InvalidOperationException($"RegOpenKeyEx failed {rc}");
        return h;
    }

    /// <summary>Reads raw stored bytes -- the ONLY way to see malformation.</summary>
    private static (int Rc, uint Type, byte[] Data) ReadRaw(SafeRegHandle h, string name)
    {
        uint cb = 0;
        var rc = RegQueryValueEx(h, name, 0, out _, null, ref cb);
        if (rc == ERROR_FILE_NOT_FOUND) return (rc, 0, Array.Empty<byte>());
        var buf = new byte[cb];
        rc = RegQueryValueEx(h, name, 0, out var t, buf, ref cb);
        return (rc, t, buf[..(int)Math.Min(cb, (uint)buf.Length)]);
    }

    /// <summary>Reads via RegGetValueW -- guarantees termination, hides malformation.</summary>
    private static (int Rc, uint Type, byte[] Data, int Retries) ReadSafe(SafeRegHandle h, string name)
    {
        uint cb = 0;
        var retries = 0;
        var rc = RegGetValue(h, null, name, RRF_RT_ANY, out var t, null, ref cb);
        if (rc == ERROR_FILE_NOT_FOUND) return (rc, 0, Array.Empty<byte>(), 0);

        // Q3: bounded retry -- the value can grow between the two calls.
        while (retries < 5)
        {
            var buf = new byte[cb];
            rc = RegGetValue(h, null, name, RRF_RT_ANY, out t, buf, ref cb);
            if (rc != ERROR_MORE_DATA) return (rc, t, buf[..(int)Math.Min(cb, (uint)buf.Length)], retries);
            retries++;   // cb was updated to the new required size; loop
        }
        return (ERROR_MORE_DATA, t, Array.Empty<byte>(), retries);
    }

    private static string Show(byte[] b) => Encoding.Unicode.GetString(b).Replace("\0", "\\0");

    internal static void Main()
    {
        Console.WriteLine("=== Q2: SafeRegistryHandle with LibraryImport ===");
        using (var h = Open(KEY_QUERY_VALUE))
            Console.WriteLine($"  opened HKCU\\Environment, IsInvalid={h.IsInvalid}, IsClosed={h.IsClosed}");
        Console.WriteLine("  disposed via using -> ReleaseHandle called automatically");

        Console.WriteLine();
        Console.WriteLine("=== Q7: does the two-call design DETECT malformation? ===");
        Console.WriteLine("(#7 justified the migration partly on EMP-07 reachability)");
        Console.WriteLine();

        var payload = @"C:\probe\value";
        var cases = new (string Suffix, byte[] Data, uint Type, string Note)[]
        {
            ("Good",   Encoding.Unicode.GetBytes(payload + "\0"), REG_EXPAND_SZ, "terminated, even"),
            ("NoTerm", Encoding.Unicode.GetBytes(payload),        REG_EXPAND_SZ, "unterminated, even"),
            ("Odd",    Encoding.Unicode.GetBytes(payload)[..27],  REG_SZ,        "odd byte count"),
            ("Empty",  Array.Empty<byte>(),                        REG_EXPAND_SZ, "zero length"),
        };

        using (var hw = Open(KEY_SET_VALUE | KEY_QUERY_VALUE))
        {
            foreach (var (suffix, data, type, note) in cases)
            {
                var name = Prefix + suffix;
                RegSetValueEx(hw, name, 0, type, data, (uint)data.Length);

                var (_, rawType, raw) = ReadRaw(hw, name);
                var (_, safeType, safe, retries) = ReadSafe(hw, name);

                var rawCb = new ByteCount((uint)raw.Length);
                var safeCb = new ByteCount((uint)safe.Length);
                var terminated = raw.Length >= 2 && raw[^1] == 0 && raw[^2] == 0;
                var malformed = !rawCb.IsWholeChars || !terminated;

                Console.WriteLine($"  {name}  ({note})");
                Console.WriteLine($"    RegQueryValueExW raw : {rawCb,-16} type={rawType} terminated={terminated}");
                Console.WriteLine($"    RegGetValueW safe    : {safeCb,-16} type={safeType} retries={retries}");
                Console.WriteLine($"    delta                : {(int)safe.Length - raw.Length:+#;-#;0} bytes");
                Console.WriteLine($"    >> MALFORMED DETECTED: {malformed}   (raw=[{Show(raw)}])");
                Console.WriteLine();
            }

            Console.WriteLine("=== Q4: does the ByteCount wrapper catch odd counts? ===");
            foreach (var n in new uint[] { 44, 43, 0, 1 })
            {
                var bc = new ByteCount(n);
                Console.WriteLine($"    ByteCount({n,2}) -> {bc,-18} Chars={(bc.Chars?.ToString() ?? "null (refuses to halve)")}");
            }

            Console.WriteLine();
            Console.WriteLine("=== Absent vs present-and-empty ===");
            var (rcAbsent, _, _) = ReadRaw(hw, Prefix + "DoesNotExist");
            var (rcEmpty, _, emptyData) = ReadRaw(hw, Prefix + "Empty");
            Console.WriteLine($"    absent  -> rc={rcAbsent} (ERROR_FILE_NOT_FOUND={ERROR_FILE_NOT_FOUND})");
            Console.WriteLine($"    empty   -> rc={rcEmpty}, {emptyData.Length} bytes  => Present=true, RawData=\"\"");

            foreach (var (suffix, _, _, _) in cases) RegDeleteValue(hw, Prefix + suffix);
        }

        using (var h = Open(KEY_QUERY_VALUE))
        {
            var leftover = cases.Count(c => ReadRaw(h, Prefix + c.Suffix).Rc != ERROR_FILE_NOT_FOUND);
            Console.WriteLine();
            Console.WriteLine($"cleanup: leftover probe values = {leftover}");
            var (rc, t, d) = ReadRaw(h, "Path");
            Console.WriteLine($"HKCU Path intact: rc={rc} type={t} bytes={d.Length}");
        }
    }
}
