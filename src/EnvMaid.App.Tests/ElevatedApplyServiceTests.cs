using EnvMaid.App.Models;
using EnvMaid.App.Services;
using Microsoft.Win32;

namespace EnvMaid.App.Tests;

/// <summary>
/// What the elevated helper does, tested without elevating: the intent file is a serialization
/// boundary, so the apply logic runs against a fake store with no UAC prompt in sight.
/// </summary>
public class ElevatedApplyServiceTests
{
    private const string Path = "Path";

    private static (ElevatedApplyService Service, FakeEnvironmentVariableStore Store) Build(string systemPath)
    {
        var store = new FakeEnvironmentVariableStore();
        store.Seed(PathScope.System, Path, VariableValue.Of(RegistryValueKind.ExpandString, systemPath));
        return (new ElevatedApplyService(store), store);
    }

    private static ElevatedIntent IntentFor(VariableValue baseline, params PathOp[] ops) => new()
    {
        Scope = nameof(PathScope.System),
        ValueName = Path,
        Baseline = PathOpService.BaselineOf(baseline),
        Ops = ops,
    };

    [Fact]
    public void MatchingBaseline_AppliesTheOpsAndVerifiesTheReadBack()
    {
        var (service, store) = Build(@"C:\a;C:\b");
        var intent = IntentFor(store.Read(PathScope.System, Path), new PathOp(PathOpKind.Remove, @"C:\b"));

        var (code, result) = service.Apply(intent);

        Assert.Equal(ElevatedExitCode.Applied, code);
        Assert.True(result.Outcome.RegistryWriteSucceeded);
        Assert.True(result.Outcome.ReadBackVerified);
        Assert.Equal(@"C:\a", store.Read(PathScope.System, Path).RawData);
    }

    [Fact]
    public void ChangedValue_WritesNothingAndReportsWhatItFound()
    {
        var (service, store) = Build(@"C:\a;C:\b");
        var intent = IntentFor(store.Read(PathScope.System, Path), new PathOp(PathOpKind.Remove, @"C:\b"));

        // An installer edits System PATH between the parent's read and the helper's.
        store.Seed(PathScope.System, Path, VariableValue.Of(RegistryValueKind.ExpandString, @"C:\a;C:\b;C:\installed"));

        var (code, result) = service.Apply(intent);

        Assert.Equal(ElevatedExitCode.Conflict, code);
        Assert.Empty(store.Writes);
        Assert.Equal(@"C:\a;C:\b;C:\installed", result.OnDiskValue);
        Assert.Equal(nameof(RegistryValueKind.ExpandString), result.OnDiskType);
    }

    [Fact]
    public void HelperNeverBroadcasts()
    {
        // The parent broadcasts once for the whole save, so one save cannot fire WM_SETTINGCHANGE
        // twice when both scopes change.
        var (service, store) = Build(@"C:\a");
        var intent = IntentFor(store.Read(PathScope.System, Path), new PathOp(PathOpKind.Add, @"C:\b", At: 1));

        var (_, result) = service.Apply(intent);

        Assert.False(result.Outcome.BroadcastSucceeded);
    }

    [Fact]
    public void OverLengthResult_IsRefusedHelperSide()
    {
        // The helper applies ops to a value the parent never computed, so a parent-side-only
        // length check is unenforceable. Same gate, both sides.
        var (service, store) = Build(@"C:\a");
        var huge = new string('x', PathLengthLimits.HardMaximum);
        var intent = IntentFor(store.Read(PathScope.System, Path), new PathOp(PathOpKind.Add, huge, At: 1));

        var (code, result) = service.Apply(intent);

        Assert.Equal(ElevatedExitCode.Failed, code);
        Assert.Empty(store.Writes);
        Assert.Contains("32,767", result.Outcome.Notes);
    }

    [Fact]
    public void PreservesTheRegistryTypeItFound()
    {
        var store = new FakeEnvironmentVariableStore();
        store.Seed(PathScope.System, Path, VariableValue.Of(RegistryValueKind.String, @"C:\a"));
        var service = new ElevatedApplyService(store);

        var intent = IntentFor(store.Read(PathScope.System, Path), new PathOp(PathOpKind.Add, @"C:\b", At: 1));

        service.Apply(intent);

        Assert.Equal(RegistryValueKind.String, Assert.Single(store.Writes).Value.Type);
    }
}
