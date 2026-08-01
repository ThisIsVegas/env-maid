using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

/// <summary>
/// The file that crosses the privilege boundary. It is written unelevated and read elevated, so
/// its ACL and its provenance checks are the thing standing between a temp file and an
/// attacker-chosen System PATH.
/// </summary>
public class ElevatedIntentFileTests
{
    private static ElevatedIntent SampleIntent() => new()
    {
        Scope = nameof(PathScope.System),
        ValueName = "Path",
        Baseline = new IntentBaseline(true, "ExpandString", PathOpService.Hash(@"C:\a;C:\b")),
        Ops = new[]
        {
            new PathOp(PathOpKind.Remove, @"C:\b"),
            new PathOp(PathOpKind.Add, @"C:\new", At: 1),
        },
    };

    [Fact]
    public void RoundTrips_IncludingEntriesThatWouldBreakACommandLine()
    {
        var intent = SampleIntent();
        // A quote in an entry is what broke the old command-line transport. JSON does not care.
        intent.Ops = new[] { new PathOp(PathOpKind.Add, @"C:\weird ""quoted"" path", At: 0) };

        var path = ElevatedIntentFile.Create(intent);
        try
        {
            var read = ElevatedIntentFile.Read(path);

            Assert.Equal(intent.ValueName, read.ValueName);
            Assert.Equal(intent.Baseline.Hash, read.Baseline.Hash);
            Assert.Equal(@"C:\weird ""quoted"" path", Assert.Single(read.Ops).RawToken);
            Assert.Equal(PathOpKind.Add, read.Ops[0].Op);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CarriesNoPathValue_OnlyIntent()
    {
        var path = ElevatedIntentFile.Create(SampleIntent());
        try
        {
            var text = File.ReadAllText(path);

            // The baseline travels as a hash. The value itself never crosses the boundary, so a
            // conflicting on-disk value cannot be silently overwritten with a stale one.
            Assert.DoesNotContain(@"C:\a;C:\b", text);
            Assert.Contains("sha256:", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GrantsOnlyTheCreatingUserAndAdministrators()
    {
        var path = ElevatedIntentFile.Create(SampleIntent());
        try
        {
            var rules = new FileInfo(path).GetAccessControl()
                .GetAccessRules(true, false, typeof(SecurityIdentifier))   // explicit only, no inherited
                .Cast<FileSystemAccessRule>()
                .ToList();

            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var me = WindowsIdentity.GetCurrent().User!;

            Assert.All(rules, rule =>
                Assert.True(rule.IdentityReference.Equals(me) || rule.IdentityReference.Equals(administrators),
                    $"Unexpected ACE for {rule.IdentityReference.Value}"));

            // Inheritance is off, so nothing the temp folder grants can widen this.
            Assert.True(new FileInfo(path).GetAccessControl().AreAccessRulesProtected);
            Assert.NotEmpty(rules);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RefusesToOverwriteAnExistingFile()
    {
        // CreateNew means a name collision is an error, not something to write through.
        var first = ElevatedIntentFile.Create(SampleIntent());
        try
        {
            Assert.StartsWith("envmaid-", System.IO.Path.GetFileName(first));
            var second = ElevatedIntentFile.Create(SampleIntent());
            File.Delete(second);

            Assert.NotEqual(first, second);
        }
        finally
        {
            File.Delete(first);
        }
    }

    [Fact]
    public void ResultIsWrittenBackIntoTheSameFile()
    {
        var path = ElevatedIntentFile.Create(SampleIntent());
        try
        {
            var intent = ElevatedIntentFile.Read(path);
            ElevatedIntentFile.WriteResult(path, intent, new ElevatedResult
            {
                Outcome = new ElevatedOutcome(true, false, false, "did not read back"),
            });

            var result = ElevatedIntentFile.Read(path).Result;

            Assert.NotNull(result);
            Assert.True(result!.Outcome.RegistryWriteSucceeded);
            Assert.False(result.Outcome.ReadBackVerified);
            Assert.Equal("did not read back", result.Outcome.Notes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ElevatedIntentFile.Read(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"envmaid-{Guid.NewGuid():N}.json")));
    }
}
