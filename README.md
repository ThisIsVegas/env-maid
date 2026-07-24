# EnvMaid

Windows PATH environment variable cleanup tool. Scans User and System PATH entries, flags orphaned/duplicate/empty entries, lets you review and remove them, with automatic backup before any change.

## Features

- Scans User and System PATH separately
- Flags entries that:
  - point to a folder that no longer exists
  - are empty
  - contain no executable-type files (`.exe`, `.bat`, `.cmd`, `.ps1`, `.dll`)
  - duplicate another entry (within or across scopes)
- Confidence-ranked flags (High/Low) with reasons shown per entry
- Backup created automatically before every save, restorable from the app
- Broadcasts `WM_SETTINGCHANGE` after saving so running apps pick up the new PATH without a reboot

## Requirements

- Windows
- .NET 10 desktop runtime (only if using the framework-dependent build; release builds are self-contained)

## Build

```
dotnet build EnvMaid.slnx --configuration Release
```

## Test

```
dotnet test src/EnvMaid.App.Tests/EnvMaid.App.Tests.csproj
```

## Run

```
dotnet run --project src/EnvMaid.App
```

## Release

Releases are built and published automatically by CI when a `v*` tag is pushed:

```
git tag v1.0.0
git push origin v1.0.0
```

This produces a self-contained single-file `EnvMaid.exe` (win-x64) attached to a GitHub Release, gated behind manual approval.

## Project structure

```
src/
  EnvMaid.App/         WPF application (MVVM)
  EnvMaid.App.Tests/   xUnit tests
```
