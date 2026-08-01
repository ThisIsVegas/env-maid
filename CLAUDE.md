# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

EnvMaid is a Windows WPF (.NET 10, MVVM) desktop tool for cleaning up the Windows PATH environment variable. It scans User and System PATH separately, reports per-entry diagnostics (broken/duplicate/ambiguous/orphaned), detects command shadow conflicts across folders, and writes changes back safely (backup first, optimistic concurrency check, then broadcast `WM_SETTINGCHANGE` so the shell refreshes its stored environment and *subsequently launched* processes inherit the new value — already-running processes keep their old environment permanently and must be restarted).

## Commands

Build (targets `net10.0-windows`, x64 only, WPF — Windows required):
```
dotnet build EnvMaid.slnx --configuration Release
```

Run the app:
```
dotnet run --project src/EnvMaid.App
```

Run all tests (xUnit):
```
dotnet test src/EnvMaid.App.Tests/EnvMaid.App.Tests.csproj
```

Run a single test class or method (xUnit via `--filter`):
```
dotnet test src/EnvMaid.App.Tests/EnvMaid.App.Tests.csproj --filter "FullyQualifiedName~ConflictAnalysisServiceTests"
dotnet test src/EnvMaid.App.Tests/EnvMaid.App.Tests.csproj --filter "DisplayName~SpecificTestName"
```

CI (`.github/workflows/ci.yml`) runs restore → Release build → test on `windows-latest` for pushes and PRs to `main`, `develop`, `release/**`, `hotfix/**`. Release is CI-driven: pushing a `v*` tag (e.g. `git tag v1.0.0 && git push origin v1.0.0`) builds a self-contained single-file `EnvMaid.exe` (win-x64) attached to a GitHub Release behind manual approval.

## The spec behind the code

[docs/knowledge/](docs/knowledge/) is not background reading — it is the specification this code implements. `windows-environment-variables-reference.md` is a numbered spec, and **the `§N` references throughout the source point into it** (e.g. "§14 step 5", "§11.1", "§9.3"). When a comment cites a section, read that section before changing the behavior it guards; many of those rules are counterintuitive and were established by measurement, not documentation. Findings are tagged by evidence class (`[DOC]` documented, `[EMP]` empirically measured, `[DOC-HIST]` historical docs), and several `[EMP]` entries record where Microsoft's own documentation is **wrong** — notably that PATHEXT, not a fixed `.exe`-first order, decides extension precedence. `docs/prototypes/` holds the throwaway probes that produced those measurements.

Check there before re-deriving Windows behavior (length limits, expansion rules, registry types, `WM_SETTINGCHANGE`).

## Architecture

Services are plain classes wired up by hand in [App.xaml.cs](src/EnvMaid.App/App.xaml.cs) `OnStartup` (no DI container). The dependency chain is: `CliToolListService` → `ConflictRanker`, plus one shared `PathExtService` → (`OrphanDetectionService`, `ConflictAnalysisService`) → `MainViewModel`. `PathExtService` is deliberately constructed **once and shared**, so the two analyses cannot disagree about what counts as a command.

### The layered storage seam

Three layers, each fakeable from above — keep the boundaries:

- **[EnvironmentVariableStore](src/EnvMaid.App/Services/EnvironmentVariableStore.cs)** (`IEnvironmentVariableStore`) is the **only** class that touches the registry. Raw P/Invoke lives under [Services/Interop/](src/EnvMaid.App/Services/Interop/). It owns storage — presence, registry type, exact bytes — not PATH semantics. Any P/Invoke leaking upward makes the layer above untestable.
- **[EnvironmentPathService](src/EnvMaid.App/Services/EnvironmentPathService.cs)** adds PATH semantics over that: the `;` split/join and the elevation relaunch.
- **ViewModels** consume only the latter.

[VariableValue](src/EnvMaid.App/Models/VariableValue.cs) is the currency between them: `Present` + `RegistryValueKind` + unexpanded `RawData`. **Absent is not a flavor of empty** — a machine with no User PATH is a different state from one with an empty string, and collapsing them makes a restore write an empty PATH. Reads use `RRF_NOEXPAND` so `REG_EXPAND_SZ` never silently downgrades to `REG_SZ` on round-trip. A PATH stored under an unsupported registry type throws `UnsupportedPathValueTypeException`, which sets `HasPathReadError` and **blocks Save**, so an unreadable value is never overwritten by the empty working copy.

### The entry model

[PathEntry](src/EnvMaid.App/Models/PathEntry.cs) stores exactly one thing — `RawToken` — and derives everything else, so no caller can leave the forms inconsistent and an untouched entry round-trips to the registry byte for byte:

- `ParsedValue` — trimmed, unquoted (display)
- `ExpandedValue` — `%VAR%` expanded (disk access)
- `ComparisonKey` — case-folded, separator-trimmed, built from the **unexpanded** token on purpose: `%JAVA_HOME%\bin` and `C:\jdk\bin` are separately maintained references that merely agree today, and folding the expanded value would make them indistinguishable from a true duplicate
- `EffectiveDirectories` — one token can contribute **several** directories (a variable holding `C:\a;C:\b`). This is supported by Windows, not malformed. Analysis expands such tokens because those directories really can shadow something; maintenance commands refuse to rewrite them (`IsStructurallyAmbiguous`), because canonicalizing `a;b` as a path is meaningless.

### Diagnostics, not flags

An entry carries a **list** of [Diagnostic](src/EnvMaid.App/Models/Diagnostic.cs) records (`DiagnosticKind` + `Severity` + message), not one flag and one reason — an entry that is both missing and duplicated says both. Two rules that are easy to get wrong:

- **Auto-selection is a property of the finding, not of confidence.** `Diagnostic.SafeToAutoSelect` allows only `EmptyToken`, `FolderMissing`, `DuplicateL1`, `DuplicateL2`. Deleting is the wrong fix for the rest: an unresolved variable needs the variable defined; an inaccessible folder may work for another process; a quoted entry is legitimate. One unsafe diagnostic vetoes the whole entry (`PathEntry.IsAutoSelectable`).
- **Four duplicate levels**, assigned most-specific-first so each entry gets exactly one (§9.3). L1 identical token → L2 same token written differently (compared *before* expansion) → L3 expands to the same folder today but maintained separately → L4 the filesystem says it is the same directory (junction, 8.3 short name, `subst`), resolved by [DirectoryIdentityService](src/EnvMaid.App/Services/DirectoryIdentityService.cs) via volume serial + file ID. Only L1/L2 auto-remove; L3 is offered unchecked; L4 is advisory and never offered for bulk removal. The three textual levels are free; only L4 touches disk.

### Two analysis paths, one resolution model

The app hinges on modeling real Windows PATH resolution: **System entries resolve before User entries**, and resolution is **directory-major** — for each folder in PATH order, every PATHEXT extension is tried before moving to the next folder. So an earlier folder wins regardless of extension, and within one folder PATHEXT order decides. The ordering (`systemEntries.Concat(userEntries)`) recurs in [OrphanDetectionService](src/EnvMaid.App/Services/OrphanDetectionService.cs), [ConflictAnalysisService](src/EnvMaid.App/Services/ConflictAnalysisService.cs), and `RecalculateGlobalRank` in [MainViewModel](src/EnvMaid.App/ViewModels/MainViewModel.cs) — keep them consistent.

- **Per-entry diagnostics** (`OrphanDetectionService.Analyze`): decorates each `PathEntry` in place with diagnostics, an `ExistenceStatus`, and its shadow conflicts. One pass across **both** scopes, never one per scope — walking them separately meant a folder listed in both was never marked duplicate at all.
- **Grouped conflicts** (`ConflictAnalysisService.Analyze`): the Conflicts tab — one `ConflictGroup` per **command name** (extension stripped) provided by 2+ folders, winner + shadowed losers, banded by the most-severe loser. `CoverageAfterRemoving` powers the "what commands would I lose?" delete prompt, matched per command rather than per filename.

The unit of conflict is the command, not the file: `foo.bat` and `foo.exe` compete for `foo`, and only one ever runs. `ConflictAnalysisService.ResolverProfile` states in the UI which launch mechanism is being simulated, so the answer is not presented as *the* answer.

Both banding paths go through **`ConflictRanker.Rank`** (the single source of confidence truth): denylisted installer names or byte-size-identical files → `LikelyFalsePositive`; name in the CLI-tool allowlist → `LikelyReal`; else `Possibly`.

**Two extension sets exist and are not interchangeable.** [PathExtService](src/EnvMaid.App/Services/PathExtService.cs) reads PATHEXT from the **process** scope (what a newly launched shell inherits, which can differ from both persisted scopes) and answers "is this runnable as a bare command" for shadowing. `OrphanDetectionService.ExecutableExtensions` answers the separate question "does this folder look useful", and deliberately includes `.dll` and `.ps1` — a DLL-only folder on PATH is legitimate because the loader searches PATH last, so deriving this set from PATHEXT would wrongly flag it as having no executables.

**The CLI-tool allowlist** ([CliToolListService](src/EnvMaid.App/Services/CliToolListService.cs)) = embedded [cli-tools-builtin.txt](src/EnvMaid.App/Resources/cli-tools-builtin.txt) (baseline, read-only) merged with a user file at `%APPDATA%\EnvMaid\cli-tools.txt`. User lines add names; a `!name` line suppresses a built-in. This is a knowledge base for scoring conflicts, not a security boundary.

### Saving: staged, optimistic, per-scope

Nothing touches the environment until Save. ViewModel `ObservableCollection<PathEntry>`s are the working copy; changing them re-runs analysis via `MainViewModel.OnEntriesChanged` (analyze → length → rank → conflicts → dashboard).

`Save` computes a diff (`PathDiffService`), shows it through the `ConfirmSave` gate, writes a JSON backup of **what is on disk right now** (not the scan baseline — if something changed underneath, that change is what an undo must bring back), then saves each scope independently and broadcasts **once** at the end.

`SaveScope` implements optimistic concurrency (§14): re-read the scope, compare against the stored baseline `VariableValue`, and if it moved, ask `ResolveConflict`. **`ResolveConflict` inverts the delegate convention** — a null delegate means *cancel*, not proceed, because it guards a change the user has not seen. Scopes are independent: a conflict in one never blocks the other. `PathLengthLimits.HardMaximum` (32767 chars) is enforced here **and** again inside the elevated helper, since a parent-only check is unenforceable once the helper re-applies ops against a different baseline.

`Restore` writes immediately, exactly like Save, and goes through the same `SaveScope` conflict gate — restoring a stale backup over an installer's change is the same race.

### The privilege boundary

The process cannot upgrade its own token, so System PATH writes relaunch the same exe elevated. What crosses the boundary is **intent, never a finished value**:

`EnvironmentPathService.ElevateApply` writes an [ElevatedIntent](src/EnvMaid.App/Models/ElevatedIntent.cs) file — baseline (presence + registry type + SHA-256 hash) plus an ordered list of `PathOp`s (Remove/Add/Move, keyed on raw tokens, removals first) — and relaunches with `--elevated-apply "<intent-path>"`. The helper (`App.TryRunElevatedHelper` → [ElevatedApplyService](src/EnvMaid.App/Services/ElevatedApplyService.cs)) re-reads the registry, verifies the baseline still matches, applies the ops to what it actually found, writes, and reads back — the whole cycle inside the privilege boundary, so nothing can change the value between the read and the write.

Rules this design encodes, all of which have bitten before:

- **Never put the joined PATH on the command line.** The old `--elevated-set-system-path "<joined>"` form was world-readable, broke on an entry containing a quote, and could be silently truncated at the command-line limit.
- **The intent file is a privilege-escalation vector.** Written unelevated, read elevated. [ElevatedIntentFile](src/EnvMaid.App/Services/ElevatedIntentFile.cs) therefore uses the per-user temp directory, a random name created with `FileMode.CreateNew`, an explicit ACL with inheritance off granting only the creating user and Administrators, and an owner check plus reparse-point check before the helper trusts a byte.
- **The helper never broadcasts.** The parent broadcasts once after every scope, so a save touching both scopes does not fire `WM_SETTINGCHANGE` twice.
- **The outcome travels back through the file, not the exit code.** `ElevatedExitCode` is coarse (Applied/Failed/Conflict/NotRun) and only routes; three independent booleans plus a note do not fit in an exit status.
- **A conflict comes back up to be resolved.** The helper is windowless and cannot prompt, so the parent prompts and retries **once** with a fresh baseline, rather than looping UAC prompts.
- **A failed read-back or broadcast is reported, never rolled back.** The write already happened; rewriting to "fix" it is a second unverified write onto an unexpected state.

`PathOpService` is pure and disk-free, which is what makes the helper's logic testable without a UAC prompt: write an intent, apply it to a fake value, assert.

### Bulk maintenance is preview-first

`PathListViewModel`'s `Normalize` / `RemoveDuplicates` / `RemoveBroken` / `Compress` each build a `MaintenancePreview` (a list of `MaintenanceChange` rows, individually checkable, pre-checked per the auto-selection rules above), show it through the `ConfirmMaintenance` gate, then apply only the still-selected rows inside `RunChangeBatch` so analysis re-runs once instead of per-row. Adding a maintenance command means following that shape, not mutating `Entries` directly. `RemoveDuplicates` is driven by the diagnostics analysis already assigned rather than re-deriving a "same folder" bucket, which could not tell an exact repeat from two separately-maintained references.

The two transforms behind them are pure and disk-free: [PathNormalizer](src/EnvMaid.App/Services/PathNormalizer.cs) (canonicalize via `Path.GetFullPath`, but leave `%VAR%` entries structurally intact) and [PathCompressor](src/EnvMaid.App/Services/PathCompressor.cs) (fold a curated always-defined Windows variable back in, longest expansion wins).

**Two length boundaries, not one** ([PathLengthLimits](src/EnvMaid.App/Services/PathLengthLimits.cs)), measured in characters of the joined value, per scope: `CautionThreshold` (2047) is where *other* PATH-writing tools start mishandling the value — nothing is truncated and editing in EnvMaid stays safe — while `HardMaximum` (32767) blocks a save. `PathListViewModel.RecalculateLength` draws a marker row at each boundary; color is reserved for the band that actually blocks a save, since a permanent always-on warning gets tuned out.

### Import/Export vs Backup/Restore

All four go through `BackupService`, but Export/Import are staging operations (`MainViewModel.Import` replaces the working copy and leaves it unsaved for review), while Restore writes to the environment immediately. `HasStagedChanges` is stored-state equality against the scan baseline — normalize/compress deliberately count as changes even though the folders resolve the same.

### View ↔ ViewModel seams

Plain delegates the view sets, not services: `MainViewModel.ConfirmSave` (save-diff dialog), `MainViewModel.ResolveConflict` (external-change prompt), `MainViewModel.PickImportFile`/`PickExportFile` (file dialogs), `PathListViewModel.ConfirmMaintenance` (preview dialog), and `ConflictsViewModel.ConfirmDelete` (coverage dialog). If null the action proceeds unconfirmed — **except `ResolveConflict`, which cancels** (see above).

**UI theming lives in [App.xaml](src/EnvMaid.App/App.xaml)** — a dark palette of named `SolidColorBrush`es (`CanvasBrush`, `SurfaceBrush`, `AccentBrush`, `AttentionBrush`, …) plus the shared control styles (`PrimaryButton`, `SurfaceBorder`, `PageTitle`, `NavTab`, …). Views reference these by key; don't hardcode colors in a view. Chrome (menu bar, About dialog, backup/restore) hangs off [MainWindow.xaml](src/EnvMaid.App/MainWindow.xaml) with click handlers in its code-behind.

## Conventions

- MVVM via `CommunityToolkit.Mvvm` source generators — `[ObservableProperty]` on a `_camelCase` field generates the `PascalCase` property; `[RelayCommand]` on a method generates `MethodNameCommand`. Don't hand-write these.
- Path comparison is normalize-then-compare. Reuse `PathEntry.Parse`/`PathEntry.Fold`, `ComparisonKey`, and the existing `PathsEqual` helpers rather than comparing raw strings — and pick the expanded or unexpanded form deliberately, since that choice is what separates duplicate levels L2 and L3.
- Tests are pure service-level xUnit against the logic classes (no UI). Test seams are constructor injection: `IEnvironmentVariableStore` (see `FakeEnvironmentVariableStore`), `PathExtService`'s `Func<string?>` PATHEXT reader, `PathCompressor`'s variable resolver, injected paths for `CliToolListService`/`BackupService`, and `EnvironmentPathService`'s `virtual` `ElevateApply`/`BroadcastEnvironmentChange`. ViewModel tests drive commands with the confirmation delegates stubbed. Use `EntryFactory` to build `PathEntry` fixtures.
- Comments here carry load — they record *why* a non-obvious rule exists, usually citing a `§` section or an `EMP-nn` measurement. Preserve that reasoning when editing nearby code.
