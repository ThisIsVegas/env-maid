// PROBE for issue #22:
//   EMP-15: does the environment builder split PATH *after* expansion, so a
//           token like %TOOLCHAIN% (=C:\a;C:\b) really contributes TWO search
//           directories -- or one malformed one?
//   §9.7:   does long-path I/O work from THIS process (no longPathAware in the
//           manifest) even though the machine has LongPathsEnabled=1?
// SAFETY: process-scope env vars + a temp scratch dir. Never touches PATH.

using System.Diagnostics;
using System.Runtime.InteropServices;

internal static partial class Probe
{
    [LibraryImport("kernel32.dll", EntryPoint = "SearchPathW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint SearchPath(string? path, string fileName, string? ext, uint bufLen, [Out] char[] buf, nint filePart);

    private static string Run(string exe, string args, string? pathOverride = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        if (pathOverride is not null) psi.Environment["PATH"] = pathOverride;
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return o.Trim();
    }

    internal static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "envmaid-amb-" + Guid.NewGuid().ToString("N")[..8]);
        var a = Path.Combine(root, "a");
        var b = Path.Combine(root, "b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);

        // Two distinct marker "executables", one per directory.
        File.WriteAllText(Path.Combine(a, "markerA.cmd"), "@echo FROM_A");
        File.WriteAllText(Path.Combine(b, "markerB.cmd"), "@echo FROM_B");

        Console.WriteLine("=== EMP-15: does the builder split AFTER expansion? ===");
        Console.WriteLine($"  TOOLCHAIN = {a};{b}");
        Console.WriteLine("  PATH      = %TOOLCHAIN%   (one token, two directories after expansion)");
        Console.WriteLine();

        // System32 must stay on PATH or `where`/`cmd` builtins are unreachable --
        // replacing PATH wholesale was a harness bug in the first run, not a finding.
        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

        // Child process: TOOLCHAIN defined, PATH = System32 + the literal %TOOLCHAIN%.
        var psi = new ProcessStartInfo("cmd.exe", "/c markerA & markerB")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["TOOLCHAIN"] = $"{a};{b}";
        psi.Environment["PATH"] = $"{sys32};%TOOLCHAIN%";
        using (var p = Process.Start(psi)!)
        {
            var o = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
            p.WaitForExit(15000);
            Console.WriteLine("  PATH = <System32>;%TOOLCHAIN%  (unexpanded token), run markerA & markerB:");
            foreach (var line in o.Split('\n')) Console.WriteLine($"    {line.Trim()}");
            Console.WriteLine("    >> both FROM_A and FROM_B  => token contributed TWO directories");
            Console.WriteLine("    >> neither found           => the unexpanded token resolves to nothing");
        }

        // Control: PATH set to the already-expanded two directories.
        Console.WriteLine();
        Console.WriteLine("  control, PATH = <System32>;<a>;<b> expanded directly:");
        foreach (var line in Run("cmd.exe", "/c markerA & markerB", $"{sys32};{a};{b}").Split('\n'))
            Console.WriteLine($"    {line.Trim()}");

        // Does a REG_EXPAND_SZ-style nested reference work at all when the value
        // reaches the block already expanded? Simulate the real-world case: the
        // builder expands %TOOLCHAIN% while building PATH.
        Console.WriteLine();
        Console.WriteLine("  realistic case, builder-expanded PATH = <System32>;<a>;<b>:");
        Console.WriteLine("    (this is what the environment builder produces for a");
        Console.WriteLine("     REG_EXPAND_SZ PATH containing %TOOLCHAIN%)");

        // SearchPathW view (the API a resolver would use).
        Console.WriteLine();
        Console.WriteLine("=== SearchPathW with an explicit lpPath ===");
        foreach (var (label, searchPath) in new[]
        {
            ("expanded  <a>;<b>", $"{a};{b}"),
            ("unexpanded %TOOLCHAIN%", "%TOOLCHAIN%"),
        })
        {
            Environment.SetEnvironmentVariable("TOOLCHAIN", $"{a};{b}");
            var buf = new char[1024];
            var len = SearchPath(searchPath, "markerA.cmd", null, (uint)buf.Length, buf, 0);
            Console.WriteLine($"  {label,-24} -> {(len > 0 ? new string(buf, 0, (int)len) : $"NOT FOUND ({Marshal.GetLastWin32Error()})")}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Round-trip test: split->join reproduces the original? ===");
        foreach (var token in new[] { @"C:\simple", @"%TOOLCHAIN%", @"C:\a;C:\b", @"%REAL%\bin" })
        {
            Environment.SetEnvironmentVariable("REAL", @"C:\real");
            var expanded = Environment.ExpandEnvironmentVariables(token);
            var parts = expanded.Split(';');
            var ambiguous = parts.Length > 1;
            Console.WriteLine($"  {token,-18} expands to {parts.Length} dir(s)  ambiguous={ambiguous}");
        }

        Console.WriteLine();
        Console.WriteLine("=== §9.7 long path: does THIS process handle >260 chars? ===");
        Console.WriteLine($"  machine LongPathsEnabled = 1 (checked separately)");
        Console.WriteLine($"  manifest longPathAware   = NOT declared in app.manifest");
        Console.WriteLine();

        // Build a directory path longer than MAX_PATH.
        var deep = root;
        while (deep.Length < 300) deep = Path.Combine(deep, "segment-of-some-length");
        Console.WriteLine($"  target length = {deep.Length} chars");
        try
        {
            Directory.CreateDirectory(deep);
            Console.WriteLine($"    CreateDirectory : OK");
            Console.WriteLine($"    Directory.Exists: {Directory.Exists(deep)}");
            var files = Directory.EnumerateFiles(deep).Count();
            Console.WriteLine($"    EnumerateFiles  : OK ({files} files)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    THREW {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }

        // Same via the \\?\ prefix, which bypasses MAX_PATH regardless of manifest.
        try
        {
            var prefixed = @"\\?\" + deep;
            Console.WriteLine($"    Exists via \\\\?\\ prefix: {Directory.Exists(prefixed)}");
        }
        catch (Exception ex) { Console.WriteLine($"    \\\\?\\ THREW {ex.GetType().Name}"); }

        try { Directory.Delete(root, recursive: true); } catch { }
        Console.WriteLine();
        Console.WriteLine($"cleanup: root exists = {Directory.Exists(root)}");
    }
}
