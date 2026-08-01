// PROBE for issue #9 — EMP-18, EMP-19, and EMP-17 while we are here.
//   EMP-18  same-named .com and .exe in ONE directory: which wins?
//           (the `path` doc claims .exe first; shipped PATHEXT starts .COM)
//   EMP-19  a.bat in dir1, a.exe in dir2: directory-major or extension-major?
//   EMP-17  do Machine PATH entries really precede User entries?
// SAFETY: temp scratch dirs + child-process PATH overrides only.
//         Never writes PATH, never touches the registry.

using System.Diagnostics;

internal static class PathExt
{
    // A real .exe is needed (a text file named .exe will not run), so copy a
    // known-good one and identify it by its OUTPUT rather than by name.
    // `where.exe` prints its argument's location -- distinct, harmless output.
    private static string Run(string args, string pathOverride, string? pathextOverride = null)
    {
        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var psi = new ProcessStartInfo("cmd.exe", "/c " + args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // System32 must stay reachable or cmd's own helpers vanish.
        psi.Environment["PATH"] = $"{sys32};{pathOverride}";
        if (pathextOverride is not null) psi.Environment["PATHEXT"] = pathextOverride;
        using var p = Process.Start(psi)!;
        var o = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
        p.WaitForExit(15000);
        return o.Replace("\r\n", " | ");
    }

    private static string RunPwsh(string command, string pathOverride)
    {
        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{command}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["PATH"] = $"{sys32};{pathOverride}";
        using var p = Process.Start(psi)!;
        var o = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
        p.WaitForExit(15000);
        return o.Replace("\r\n", " | ");
    }

    internal static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "envmaid-pathext-" + Guid.NewGuid().ToString("N")[..8]);
        var dir1 = Path.Combine(root, "dir1");
        var dir2 = Path.Combine(root, "dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        // Batch-family markers are easy: they echo their own identity.
        void Bat(string dir, string name, string id) =>
            File.WriteAllText(Path.Combine(dir, name), $"@echo {id}");

        Console.WriteLine($"PATHEXT on this machine: {Environment.GetEnvironmentVariable("PATHEXT")}");
        Console.WriteLine();

        // ---------------------------------------------------------------- EMP-18
        // .com vs .exe in ONE directory. A .com must be a real executable, so
        // copy a real one; identify by output. Compare against a .bat too, which
        // sorts AFTER both in PATHEXT.
        Console.WriteLine("=== EMP-18: same name, different extensions, ONE directory ===");

        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var realExe = Path.Combine(sys32, "hostname.exe");   // prints the machine name
        var comTarget = Path.Combine(dir1, "marker.com");
        var exeTarget = Path.Combine(dir1, "marker.exe");

        // .com = a copy of a real exe (Windows runs PE files regardless of extension)
        File.Copy(realExe, comTarget, true);
        File.Copy(realExe, exeTarget, true);
        Bat(dir1, "marker.bat", "RAN_BAT");

        Console.WriteLine("  dir1 contains marker.com, marker.exe, marker.bat");
        Console.WriteLine("  .com and .exe are both copies of hostname.exe (print the machine name),");
        Console.WriteLine("  so a machine-name result means com-or-exe won; RAN_BAT means .bat won.");
        Console.WriteLine();
        Console.WriteLine($"    `marker`                -> {Run("marker", dir1)}");
        Console.WriteLine($"    (expected: machine name, NOT RAN_BAT -> .bat loses to .com/.exe)");
        Console.WriteLine();

        // Distinguishing .com from .exe needs two DIFFERENT real binaries, since
        // copies of the same one produce identical output. hostname.exe prints the
        // machine name; whoami.exe prints the user -- unmistakably different.
        var realExe2 = Path.Combine(sys32, "whoami.exe");
        File.Copy(realExe2, comTarget, true);    // .com = whoami  (prints user)
        File.Copy(realExe, exeTarget, true);     // .exe = hostname (prints machine)
        Console.WriteLine();
        Console.WriteLine("  Distinguishing .com from .exe (different binaries):");
        Console.WriteLine("    marker.com = whoami.exe   (prints the USER)");
        Console.WriteLine("    marker.exe = hostname.exe (prints the MACHINE)");
        Console.WriteLine($"    `marker` with default PATHEXT -> {Run("marker", dir1)}");
        Console.WriteLine("    a USER-looking result => .com won, matching PATHEXT order");
        Console.WriteLine("    a MACHINE name        => .exe won, matching the `path` doc");

        // Reversed PATHEXT should flip it, proving the order is what decides.
        Console.WriteLine($"    PATHEXT=.EXE;.COM             -> {Run("marker", dir1, ".EXE;.COM")}");
        Console.WriteLine($"    PATHEXT=.COM;.EXE             -> {Run("marker", dir1, ".COM;.EXE")}");
        File.Delete(comTarget);
        File.Copy(realExe, comTarget, true);

        // The decisive test: reorder PATHEXT and see if the winner follows it.
        Console.WriteLine();
        Console.WriteLine("  Decisive test — does the winner follow PATHEXT order?");
        Console.WriteLine("  Put .BAT FIRST in PATHEXT; if .bat now wins, PATHEXT is authoritative");
        Console.WriteLine("  and the `path` doc's fixed .exe-first claim is wrong.");
        Console.WriteLine($"    PATHEXT=.BAT;.COM;.EXE  -> {Run("marker", dir1, ".BAT;.COM;.EXE")}");
        Console.WriteLine($"    PATHEXT=.COM;.EXE;.BAT  -> {Run("marker", dir1, ".COM;.EXE;.BAT")}");

        // ---------------------------------------------------------------- EMP-19
        Console.WriteLine();
        Console.WriteLine("=== EMP-19: directory-major or extension-major? ===");
        Directory.Delete(dir1, true);
        Directory.CreateDirectory(dir1);
        Bat(dir1, "cross.bat", "DIR1_BAT");            // early dir, LATE extension
        File.Copy(realExe, Path.Combine(dir2, "cross.exe"), true);  // late dir, EARLY extension

        Console.WriteLine("  dir1\\cross.bat  (earlier directory, later extension)");
        Console.WriteLine("  dir2\\cross.exe  (later directory, earlier extension)");
        Console.WriteLine("  PATH = dir1;dir2   PATHEXT = .COM;.EXE;.BAT;...");
        Console.WriteLine();
        var crossResult = Run("cross", $"{dir1};{dir2}");
        Console.WriteLine($"    `cross` -> {crossResult}");
        Console.WriteLine("    DIR1_BAT     => DIRECTORY-major (dir order wins; EMP-19 holds)");
        Console.WriteLine("    machine name => EXTENSION-major (PATHEXT order wins across dirs)");

        // ---------------------------------------------------------------- EMP-17
        Console.WriteLine();
        Console.WriteLine("=== EMP-17: does earlier-on-PATH win? (composition-order proxy) ===");
        var dir3 = Path.Combine(root, "dir3");
        var dir4 = Path.Combine(root, "dir4");
        Directory.CreateDirectory(dir3);
        Directory.CreateDirectory(dir4);
        Bat(dir3, "order.bat", "FROM_FIRST");
        Bat(dir4, "order.bat", "FROM_SECOND");
        Console.WriteLine($"    PATH = dir3;dir4 -> {Run("order", $"{dir3};{dir4}")}");
        Console.WriteLine($"    PATH = dir4;dir3 -> {Run("order", $"{dir4};{dir3}")}");
        Console.WriteLine("    (first-listed should win in both -- confirms order significance,");
        Console.WriteLine("     which is what the Machine-before-User model rests on)");

        // ---------------------------------------------------------- PowerShell
        Console.WriteLine();
        Console.WriteLine("=== PowerShell profile (§9.5) — same layout, different resolver ===");
        Console.WriteLine($"    pwsh `cross`            -> {RunPwsh("cross", $"{dir1};{dir2}")}");
        Console.WriteLine($"    pwsh Get-Command cross  -> {RunPwsh("(Get-Command cross).Source", $"{dir1};{dir2}")}");

        try { Directory.Delete(root, true); } catch { }
        Console.WriteLine();
        Console.WriteLine($"cleanup: root exists = {Directory.Exists(root)}");
    }
}
