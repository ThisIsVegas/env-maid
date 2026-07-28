// PROBE: is L4 (filesystem-equivalent) duplicate detection actually workable?
// §9.3 names junction, symlink, subst, 8.3 short name, UNC-vs-drive aliasing.
// Testing GetFinalPathNameByHandle + volume/file-id on each, measuring cost,
// and checking failure modes on inaccessible / missing paths.
// SAFETY: creates only a temp scratch dir; deletes it; touches no PATH.

using System.Diagnostics;
using System.Runtime.InteropServices;

internal static partial class L4
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateFile(string path, uint access, uint share, nint sa, uint disp, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(nint handle, [Out] char[] buf, uint len, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(nint handle, out BY_HANDLE_FILE_INFORMATION info);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint h);

    // NOTE: the three timestamps are FILETIME = two uints each, NOT long.
    // Declaring them as long adds 4 bytes of alignment padding after
    // FileAttributes and shifts every following field, so VolumeSerialNumber
    // reads as 0 and every volume looks identical. Verified: C:/D:/E:/F: all
    // returned vol=00000000 fileId=...0500000000 with the wrong layout.
    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public uint CreationTimeLow, CreationTimeHigh;
        public uint LastAccessTimeLow, LastAccessTimeHigh;
        public uint LastWriteTimeLow, LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh, FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh, FileIndexLow;
    }

    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_ALL = 0x07;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;   // required for directories
    private const uint VOLUME_NAME_DOS = 0x0;

    private record Identity(string? FinalPath, uint Volume, ulong FileId, string? Error);

    private static Identity Resolve(string path)
    {
        var h = CreateFile(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (h == -1)
            return new Identity(null, 0, 0, $"CreateFile failed ({Marshal.GetLastWin32Error()})");

        try
        {
            var buf = new char[1024];
            var len = GetFinalPathNameByHandle(h, buf, (uint)buf.Length, VOLUME_NAME_DOS);
            var final = len > 0 && len < buf.Length ? new string(buf, 0, (int)len) : null;

            if (!GetFileInformationByHandle(h, out var info))
                return new Identity(final, 0, 0, $"GetFileInformationByHandle failed ({Marshal.GetLastWin32Error()})");

            var id = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
            return new Identity(final, info.VolumeSerialNumber, id, null);
        }
        finally { CloseHandle(h); }
    }

    private static void Show(string label, string path)
    {
        var r = Resolve(path);
        if (r.Error is not null)
        {
            Console.WriteLine($"  {label,-26} {path,-34} ERROR: {r.Error}");
            return;
        }
        Console.WriteLine($"  {label,-26} {path,-34}");
        Console.WriteLine($"  {"",-26} final = {r.FinalPath}");
        Console.WriteLine($"  {"",-26} vol={r.Volume:X8} fileId={r.FileId:X16}");
    }

    private static bool SameObject(string a, string b)
    {
        var ra = Resolve(a);
        var rb = Resolve(b);
        if (ra.Error is not null || rb.Error is not null) return false;
        return ra.Volume == rb.Volume && ra.FileId == rb.FileId;
    }

    private static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        return o.Trim();
    }

    internal static void Main()
    {
        // Round 2: vol=00000000 in round 1 is suspicious -- if VolumeSerialNumber is
        // always zero, SameObject() is matching on file ID alone, which WILL collide
        // across volumes. Check against several real directories first.
        Console.WriteLine("=== Volume serial sanity check ===");
        foreach (var p in new[] { @"C:\Windows", @"C:\Users", Path.GetTempPath(), @"C:\Windows\System32" })
        {
            var r = Resolve(p);
            Console.WriteLine($"  {p,-34} vol={r.Volume:X8} fileId={r.FileId:X16} err={r.Error}");
        }
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady).Take(4))
        {
            var r = Resolve(d.RootDirectory.FullName);
            Console.WriteLine($"  {d.Name,-34} vol={r.Volume:X8} fileId={r.FileId:X16} err={r.Error}");
        }
        Console.WriteLine();

        var root = Path.Combine(Path.GetTempPath(), "envmaid-l4-" + Guid.NewGuid().ToString("N")[..8]);
        var real = Path.Combine(root, "real");
        var junction = Path.Combine(root, "junction");
        var withSpaces = Path.Combine(root, "dir with spaces");
        Directory.CreateDirectory(real);
        Directory.CreateDirectory(withSpaces);
        File.WriteAllText(Path.Combine(real, "marker.exe"), "x");

        Console.WriteLine("=== Identity of a plain directory ===");
        Show("real", real);

        Console.WriteLine();
        Console.WriteLine("=== Junction (mklink /J) — no elevation needed ===");
        var mk = Run("cmd.exe", $"/c mklink /J \"{junction}\" \"{real}\"");
        Console.WriteLine($"  mklink: {mk}");
        if (Directory.Exists(junction))
        {
            Show("junction", junction);
            Console.WriteLine($"  >> SameObject(real, junction) = {SameObject(real, junction)}");
            Console.WriteLine($"  >> textual compare would say  = {string.Equals(real, junction, StringComparison.OrdinalIgnoreCase)}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 8.3 short name ===");
        var shortName = Run("cmd.exe", $"/c for %A in (\"{withSpaces}\") do @echo %~sA");
        Console.WriteLine($"  short = {shortName}");
        if (!string.IsNullOrWhiteSpace(shortName) && Directory.Exists(shortName))
        {
            Console.WriteLine($"  >> SameObject(long, short) = {SameObject(withSpaces, shortName)}");
            Console.WriteLine($"  >> textual compare         = {string.Equals(withSpaces, shortName, StringComparison.OrdinalIgnoreCase)}");
        }
        else Console.WriteLine("  (8.3 generation appears disabled on this volume)");

        Console.WriteLine();
        Console.WriteLine("=== subst drive ===");
        Console.WriteLine($"  subst: {Run("cmd.exe", $"/c subst X: \"{real}\"")}");
        if (Directory.Exists(@"X:\"))
        {
            Console.WriteLine($"  >> SameObject(real, X:\\) = {SameObject(real, @"X:\")}");
            Show("X:\\", @"X:\");
        }
        else Console.WriteLine("  (subst failed or X: in use)");

        Console.WriteLine();
        Console.WriteLine("=== Failure modes ===");
        Show("missing dir", Path.Combine(root, "does-not-exist"));
        Show("system-protected", @"C:\System Volume Information");
        Show("unexpanded var", @"%NOPE%\bin");

        Console.WriteLine();
        Console.WriteLine("=== Cost: resolve 131 entries (System PATH size) ===");
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 131; i++) Resolve(real);
        sw.Stop();
        Console.WriteLine($"  131 resolves: {sw.ElapsedMilliseconds} ms  ({sw.Elapsed.TotalMilliseconds / 131:F3} ms each)");

        sw.Restart();
        for (var i = 0; i < 131; i++) Resolve(Path.Combine(root, "does-not-exist"));
        sw.Stop();
        Console.WriteLine($"  131 failures: {sw.ElapsedMilliseconds} ms  (failure path cost)");

        // cleanup
        Run("cmd.exe", "/c subst X: /D");
        if (Directory.Exists(junction)) Directory.Delete(junction);   // removes the link, not the target
        Directory.Delete(root, recursive: true);
        Console.WriteLine();
        Console.WriteLine($"cleanup: root exists = {Directory.Exists(root)}, X: exists = {Directory.Exists(@"X:\")}");
    }
}
