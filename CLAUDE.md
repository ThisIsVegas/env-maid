# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

EnvMaid is a Windows WPF (.NET 10, MVVM) desktop tool for cleaning up the Windows PATH environment variable. It scans User and System PATH separately, flags broken/duplicate/orphaned entries, detects executable shadow conflicts across folders, and writes changes back safely (backup first, then broadcast so running apps pick up the change without a reboot).

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

Release is CI-driven: pushing a `v*` tag (e.g. `git tag v1.0.0 && git push origin v1.0.0`) builds a self-contained single-file `EnvMaid.exe` (win-x64) attached to a GitHub Release behind manual approval.

## Architecture

Services are plain classes wired up by hand in [App.xaml.cs](src/EnvMaid.App/App.xaml.cs) `OnStartup` (no DI container). The dependency chain is: `CliToolListService` → `ConflictRanker` → (`OrphanDetectionService`, `ConflictAnalysisService`) → `MainViewModel`.

**Two data flows share one core model.** The whole app hinges on modeling real Windows PATH resolution: **System entries resolve before User entries**, and the first folder to provide a given `.exe`/`.bat`/`.cmd` wins; later providers are "shadowed" losers. This ordering (`systemEntries.Concat(userEntries)`) recurs in [OrphanDetectionService](src/EnvMaid.App/Services/OrphanDetectionService.cs), [ConflictAnalysisService](src/EnvMaid.App/Services/ConflictAnalysisService.cs), and `RecalculateGlobalRank` in [MainViewModel](src/EnvMaid.App/ViewModels/MainViewModel.cs) — keep them consistent if you change it.

- **Per-entry flags** (`OrphanDetectionService.ApplyFlags`): decorates each `PathEntry` in place with `PathFlag` (Missing/Empty/NoExecutable/Duplicate), a `FlagConfidence`, a human-readable `Reason`, and its shadow conflicts. Drives the grid rows and the dashboard counts.
- **Grouped conflicts** (`ConflictAnalysisService.Analyze`): the Conflicts tab — one `ConflictGroup` per exe name provided by 2+ folders, winner + shadowed losers, banded by the most-severe loser. `CoverageAfterRemoving` powers the "what commands would I lose?" delete prompt.

Both banding paths go through **`ConflictRanker.Rank`** (the single source of confidence truth): denylisted installer names or byte-size-identical files → `LikelyFalsePositive`; name in the CLI-tool allowlist → `LikelyReal`; else `Possibly`.

**The CLI-tool allowlist** ([CliToolListService](src/EnvMaid.App/Services/CliToolListService.cs)) = embedded [cli-tools-builtin.txt](src/EnvMaid.App/Resources/cli-tools-builtin.txt) (baseline, read-only) merged with a user file at `%APPDATA%\EnvMaid\cli-tools.txt`. User lines add names; a `!name` line suppresses a built-in. This is a knowledge base for scoring conflicts, not a security boundary.

**Editing is staged; nothing touches the environment until Save.** ViewModel `ObservableCollection<PathEntry>`s are the working copy. Changing them re-runs analysis via `MainViewModel.OnEntriesChanged` (rank → conflicts → dashboard). `Save` ([MainViewModel](src/EnvMaid.App/ViewModels/MainViewModel.cs)) computes a diff (`PathDiffService`), shows it through the `ConfirmSave` gate, writes a JSON backup (`BackupService`), then commits and broadcasts `WM_SETTINGCHANGE`.

**System PATH requires elevation.** The process can't upgrade its own token, so writing System PATH relaunches the same exe with `--elevated-set-system-path "<joined>"`; that instance writes and exits with no window (`App.TryRunElevatedHelper`). User PATH is always applied; System is applied in-process if already admin, else via the elevated relaunch, else reported as not-applied. See `ApplySystemPathIfChanged`.

**View ↔ ViewModel confirmation seams** are plain delegates the view sets, not services: `MainViewModel.ConfirmSave` (save-diff dialog) and `ConflictsViewModel.ConfirmDelete` (coverage dialog). If null, the action proceeds unconfirmed.

## Conventions

- MVVM via `CommunityToolkit.Mvvm` source generators — `[ObservableProperty]` on a `_camelCase` field generates the `PascalCase` property; `[RelayCommand]` on a method generates `MethodNameCommand`. Don't hand-write these.
- Path comparison is normalize-then-compare: expand `%VARS%`, `TrimEnd('\\')`, case-insensitive. Reuse the existing `Normalize`/`PathsEqual` helpers rather than comparing raw strings.
- Tests are pure service-level xUnit against the logic classes (no UI); `CliToolListService` and `BackupService` take an injected path as a test seam.
