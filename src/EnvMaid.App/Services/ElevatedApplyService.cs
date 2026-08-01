using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

/// <summary>
/// What the elevated helper actually does: re-read, verify the baseline, apply the ops, write,
/// and read back — all inside the privilege boundary, with nothing in between.
/// </summary>
/// <remarks>
/// The re-read and the write must happen in the same elevated process. Having the parent read and
/// the helper write leaves a window an installer can change the value in, which is the exact race
/// the optimistic-save design exists to close.
/// </remarks>
public class ElevatedApplyService
{
    private readonly IEnvironmentVariableStore _store;

    public ElevatedApplyService(IEnvironmentVariableStore? store = null) =>
        _store = store ?? new EnvironmentVariableStore();

    public (ElevatedExitCode Code, ElevatedResult Result) Apply(ElevatedIntent intent)
    {
        var scope = Enum.TryParse<PathScope>(intent.Scope, ignoreCase: true, out var parsed)
            ? parsed
            : PathScope.System;

        VariableValue current;
        try
        {
            current = _store.Read(scope, intent.ValueName);
        }
        catch (Exception ex)
        {
            return Fail($"Could not read the {scope} {intent.ValueName}: {ex.Message}");
        }

        if (!PathOpService.Matches(intent.Baseline, current))
        {
            // Windowless: it cannot prompt, so it writes nothing and hands the situation back to
            // the parent, which can show the user what changed.
            return (ElevatedExitCode.Conflict, new ElevatedResult
            {
                Outcome = new ElevatedOutcome(false, false, false,
                    $"The {scope} {intent.ValueName} changed since the baseline was taken; nothing was written."),
                OnDiskValue = current.Present ? current.RawData : null,
                OnDiskType = current.Present ? current.Type.ToString() : null,
            });
        }

        var entries = EnvironmentPathService.SplitEntries(current);
        var updated = PathOpService.Apply(entries, intent.Ops);
        var joined = string.Join(';', updated.Where(e => !string.IsNullOrWhiteSpace(e)));

        // The length gate runs here too, on the value the helper computed. A parent-side-only
        // check is unenforceable once the helper re-applies ops against a different baseline.
        if (joined.Length > PathLengthLimits.HardMaximum)
            return Fail(
                $"The resulting {scope} {intent.ValueName} would be {joined.Length:N0} characters, " +
                $"past the {PathLengthLimits.HardMaximum:N0} limit. Nothing was written.");

        try
        {
            var intended = VariableValue.Of(current.Type, joined);
            _store.Write(scope, intent.ValueName, intended);

            var verified = _store.Read(scope, intent.ValueName) == intended;

            // No broadcast here. The parent broadcasts once after every scope is written, so a
            // save touching both scopes does not fire WM_SETTINGCHANGE twice.
            return (ElevatedExitCode.Applied, new ElevatedResult
            {
                Outcome = new ElevatedOutcome(
                    RegistryWriteSucceeded: true,
                    ReadBackVerified: verified,
                    BroadcastSucceeded: false,
                    Notes: verified ? null : "The value was written but did not read back as written."),
            });
        }
        catch (Exception ex)
        {
            return Fail($"Writing the {scope} {intent.ValueName} failed: {ex.Message}");
        }
    }

    private static (ElevatedExitCode, ElevatedResult) Fail(string note) =>
        (ElevatedExitCode.Failed, new ElevatedResult
        {
            Outcome = new ElevatedOutcome(false, false, false, note),
        });
}
