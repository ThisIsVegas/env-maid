// PROBE for issue #13:
//   Q1  is "a surviving %..% pair means unresolved" a sound detection rule?
//       (EMP-04: no documented escape for a literal %, so a path legitimately
//        containing % could false-positive)
//   Q4  EMP-08: is expansion single-pass? do self-reference and cycles terminate?
// SAFETY: sets PROCESS-scope variables only. Never touches persisted scopes.

using System.Diagnostics;

static string Ex(string s) => Environment.ExpandEnvironmentVariables(s);

static void Set(string n, string v) => Environment.SetEnvironmentVariable(n, v);   // process scope

Console.WriteLine("=== Q1: detection rule — what survives expansion? ===");
Console.WriteLine($"{"input",-34} {"expanded",-40} unresolved?");
Console.WriteLine(new string('-', 92));

Set("REAL", @"C:\real");

var q1 = new (string Input, string Note)[]
{
    (@"%REAL%\bin",            "defined variable"),
    (@"%NOPE%\bin",            "undefined variable"),
    (@"C:\literal",            "no variable at all"),
    (@"C:\100%",               "single % - literal, not a pair"),
    (@"C:\100%%",              "doubled %% at end"),
    (@"C:\a%b%c",              "%b% undefined, mid-path"),
    (@"C:\50%-off\bin",        "single % mid-path"),
    (@"C:\a%%b",               "%% mid-path"),
    (@"%REAL%\x%NOPE%\y",      "one defined, one not"),
    (@"100%%REAL%%",           "ambiguous %% around a real name"),
    (@"%",                     "lone %"),
    (@"%%",                    "just %%"),
};

foreach (var (input, note) in q1)
{
    var outp = Ex(input);
    // candidate rule: a %...% pair survives in the OUTPUT
    var i = outp.IndexOf('%');
    var j = i >= 0 ? outp.IndexOf('%', i + 1) : -1;
    var flagged = j > i && i >= 0;
    Console.WriteLine($"{input,-34} {outp,-40} {(flagged ? "FLAG" : "ok"),-6} {note}");
}

Console.WriteLine();
Console.WriteLine("=== Q1b: refined rule — %NAME% where NAME is a plausible var name ===");
Console.WriteLine("Naive 'any two % survive' misflags %%-doubling and real dirs containing %.");
Console.WriteLine("Refined: a %...% pair whose interior is non-empty and contains no % and no");
Console.WriteLine("path separator, i.e. it looks like a variable NAME that failed to resolve.");
Console.WriteLine();

static bool LooksUnresolved(string expanded)
{
    for (var i = 0; i < expanded.Length; i++)
    {
        if (expanded[i] != '%') continue;
        var close = expanded.IndexOf('%', i + 1);
        if (close < 0) return false;                 // no closing % at all
        var inner = expanded[(i + 1)..close];
        if (inner.Length > 0
            && !inner.Contains('\\') && !inner.Contains('/')
            && !inner.Contains(':'))
            return true;                             // plausible variable name
        i = close - 1;                               // resume scanning after this pair
    }
    return false;
}

Console.WriteLine($"{"expanded value",-40} {"naive",-7} {"refined",-9} expected");
Console.WriteLine(new string('-', 84));
var q1b = new (string Expanded, bool Expected, string Note)[]
{
    (@"C:\real\bin",                       false, "resolved"),
    (@"%NOPE%\bin",                        true,  "genuine unresolved"),
    (@"C:\literal",                        false, "no %"),
    (@"C:\100%",                           false, "single %"),
    (@"C:\100%%",                          false, "%% literal, empty interior"),
    (@"C:\a%b%c",                          true,  "genuine unresolved %b%"),
    (@"C:\50%-off\bin",                    false, "single % mid-path"),
    (@"C:\a%%b",                           false, "%% literal"),
    (@"C:\real\x%NOPE%\y",                 true,  "genuine, one unresolved"),
    (@"%%",                                false, "just %%"),
    (@"C:\Users\thisi\%HOMEDRIVE%%HOMEPATH%", true, "REAL DIR on this machine (see below)"),
};

foreach (var (exp, expected, note) in q1b)
{
    var i = exp.IndexOf('%');
    var j = i >= 0 ? exp.IndexOf('%', i + 1) : -1;
    var naive = j > i && i >= 0;
    var refined = LooksUnresolved(exp);
    var mark = refined == expected ? "" : "  <-- MISMATCH";
    Console.WriteLine($"{exp,-40} {(naive ? "FLAG" : "ok"),-7} {(refined ? "FLAG" : "ok"),-9} {expected}{mark}   {note}");
}

Console.WriteLine();
Console.WriteLine("=== Q4 / EMP-08: single-pass? cycles terminate? ===");

// nesting: A contains a reference to B
Set("INNER", @"C:\inner");
Set("OUTER", "%INNER%");
Console.WriteLine($"  OUTER=%INNER%, INNER=C:\\inner");
Console.WriteLine($"    Expand(\"%OUTER%\") = \"{Ex("%OUTER%")}\"");
Console.WriteLine($"    -> {(Ex("%OUTER%") == @"C:\inner" ? "RECURSIVE (multi-pass)" : "SINGLE-PASS (stops after one substitution)")}");

Console.WriteLine();
Console.WriteLine("  self-reference A=%A%");
Set("SELFREF", "%SELFREF%");
var sw = Stopwatch.StartNew();
var self = Ex("%SELFREF%");
sw.Stop();
Console.WriteLine($"    Expand(\"%SELFREF%\") = \"{self}\"  ({sw.ElapsedMilliseconds} ms) -> terminated");

Console.WriteLine();
Console.WriteLine("  cycle A=%B%, B=%A%");
Set("CYCA", "%CYCB%");
Set("CYCB", "%CYCA%");
sw.Restart();
var cyc = Ex("%CYCA%");
sw.Stop();
Console.WriteLine($"    Expand(\"%CYCA%\") = \"{cyc}\"  ({sw.ElapsedMilliseconds} ms) -> terminated");

Console.WriteLine();
Console.WriteLine("  deep chain A->B->C->D");
Set("D4", @"C:\deep");
Set("C3", "%D4%");
Set("B2", "%C3%");
Set("A1", "%B2%");
Console.WriteLine($"    Expand(\"%A1%\") = \"{Ex("%A1%")}\"   (single-pass => \"%B2%\")");

Console.WriteLine();
Console.WriteLine("=== Case sensitivity of lookup (§5.1 [DOC]) ===");
foreach (var v in new[] { "%real%", "%REAL%", "%ReAl%" })
    Console.WriteLine($"    {v,-10} -> {Ex(v)}");

Console.WriteLine();
Console.WriteLine("=== Would a REAL path with % be misflagged? ===");
foreach (var d in Directory.GetDirectories(@"C:\", "*", SearchOption.TopDirectoryOnly).Take(0)) { }
var pctDirs = new List<string>();
try
{
    foreach (var root in new[] { @"C:\", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
        foreach (var d in Directory.EnumerateDirectories(root))
            if (Path.GetFileName(d).Contains('%')) pctDirs.Add(d);
}
catch { }
Console.WriteLine($"    directories containing '%' found: {(pctDirs.Count == 0 ? "none" : string.Join(", ", pctDirs))}");
