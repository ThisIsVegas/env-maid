<p align="center">
  <img src="assets/app_icon.png" alt="EnvMaid application icon" width="160">
</p>

<h1 align="center">EnvMaid</h1>

<p align="center">
  A focused Windows app for reviewing, cleaning, and safely editing your PATH environment variable.
</p>

<p align="center">
  <a href="https://github.com/ThisIsVegas/env-maid/actions/workflows/ci.yml"><img src="https://github.com/ThisIsVegas/env-maid/actions/workflows/ci.yml/badge.svg?branch=main" alt="Build status"></a>
  <a href="https://github.com/ThisIsVegas/env-maid/releases"><img src="https://img.shields.io/badge/download-releases-2f81f7?logo=github" alt="Download from GitHub Releases"></a>
  <img src="https://img.shields.io/badge/platform-Windows%20x64-0078d4?logo=windows11" alt="Windows x64">
  <img src="https://img.shields.io/badge/.NET-10.0-512bd4?logo=dotnet" alt=".NET 10">
</p>

<p align="center">
  <a href="https://github.com/ThisIsVegas/env-maid/releases">Download</a>
  ·
  <a href="https://github.com/ThisIsVegas/env-maid/issues/new">Report a problem</a>
  ·
  <a href="#for-developers">Developer guide</a>
</p>

---

## For users

EnvMaid gives you a simple health summary first and keeps the complete PATH editor available when you need more control. It scans both User and System PATH entries, explains what needs attention, and stages every edit until you choose to save.

![EnvMaid overview showing PATH findings grouped by type](assets/ui-overview.png)

### What it finds

| Finding | What it means |
| --- | --- |
| Missing locations | The folder no longer exists or the entry is empty |
| Duplicate entries | The same location appears more than once |
| No executables | A folder does not contain a supported executable file |
| Command priority | More than one folder provides the same command, so Windows uses the first match |

Findings include a reason and confidence level so you can decide what deserves action.

### A safe workflow

1. Open EnvMaid to scan your current User and System PATH.
2. Start on **Overview** for a short, grouped summary.
3. Select **Review** to inspect a finding in the full PATH editor.
4. Add, edit, remove, or reorder entries. Maintenance actions show a preview before changing the staged list.
5. Select **Review and save** to inspect the exact diff before anything is written to Windows.

Changes remain staged inside EnvMaid until you confirm the final save. Before writing, EnvMaid creates a JSON backup and then notifies Windows of the updated environment settings.

> [!IMPORTANT]
> Saving System PATH changes requires administrator approval. User PATH changes do not.

### More tools

The **More** menu keeps occasional actions out of the main workflow:

- Restore an automatic backup
- Import a PATH profile
- Export the current PATH as a profile

<details>
<summary><strong>How command priority works</strong></summary>

Windows searches System PATH before User PATH. Within each scope, entries are searched from top to bottom. If several folders contain a command with the same name, the first copy wins and later copies are shadowed.

EnvMaid models this order when identifying conflicts and shows what command coverage would be lost before a conflicting location is removed.
</details>

### Install and requirements

Download `EnvMaid.exe` from [GitHub Releases](https://github.com/ThisIsVegas/env-maid/releases). Release builds are self-contained, so a separate .NET installation is not required.

EnvMaid requires:

- 64-bit Windows
- Administrator approval only when saving System PATH changes

If no release is published yet, developers can run the project from source using the instructions below.

### Get help

Found incorrect detection, unexpected behavior, or a usability problem? [Open a GitHub issue](https://github.com/ThisIsVegas/env-maid/issues/new) and include:

- What you expected and what happened
- The steps needed to reproduce it
- Your Windows version
- A screenshot when it helps, with private paths removed

---

## For developers

EnvMaid is a Windows WPF application targeting .NET 10. It uses MVVM with `CommunityToolkit.Mvvm`; services are plain classes composed in `App.xaml.cs`.

### Development requirements

- Windows on x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

Clone and verify the project:

```powershell
git clone https://github.com/ThisIsVegas/env-maid.git
cd env-maid
dotnet restore EnvMaid.slnx
dotnet build EnvMaid.slnx --configuration Release
dotnet test src/EnvMaid.App.Tests/EnvMaid.App.Tests.csproj --configuration Release
```

Run the desktop app:

```powershell
dotnet run --project src/EnvMaid.App
```

Run a focused test:

```powershell
dotnet test src/EnvMaid.App.Tests/EnvMaid.App.Tests.csproj `
  --filter "FullyQualifiedName~ConflictAnalysisServiceTests"
```

### Architecture at a glance

| Area | Responsibility |
| --- | --- |
| `Models` | PATH entries, flags, conflicts, and maintenance previews |
| `Services` | Environment access, normalization, analysis, ranking, diffs, and backups |
| `ViewModels` | Staged editing, commands, dashboard state, and confirmation seams |
| `Views` | WPF screens and dialogs |
| `Resources` | Built-in CLI-tool knowledge used to rank command conflicts |
| `EnvMaid.App.Tests` | Pure service and view-model tests |

Two analysis paths share the same Windows resolution model:

- Per-entry analysis flags missing, empty, executable-free, and duplicate entries.
- Grouped conflict analysis identifies the winning and shadowed providers for each command.

Both must preserve `System entries → User entries` ordering and use `ConflictRanker` as the single source of confidence ranking.

### Important implementation behavior

- Editing is staged in observable collections; the environment is untouched until save.
- Save computes a diff, asks for confirmation, creates a backup, writes the changes, and broadcasts `WM_SETTINGCHANGE`.
- System PATH writes run directly when already elevated or relaunch a small elevated helper.
- Path equality is case-insensitive and based on expanded, normalized paths.
- The CLI-tool list combines an embedded baseline with user overrides from `%APPDATA%\EnvMaid\cli-tools.txt`.

See [AGENTS.md](AGENTS.md) for the detailed project architecture and coding conventions used by contributors and coding agents.

### Contributing

1. Create a focused branch from `main`.
2. Keep environment writes behind the existing confirmation and backup flow.
3. Add or update tests for behavior changes.
4. Run the Release build and complete test suite.
5. Open a pull request with the motivation, behavior change, and verification notes.

GitHub Actions builds and tests pull requests targeting `main`. For bugs and proposed features, start with a [GitHub issue](https://github.com/ThisIsVegas/env-maid/issues/new) so the behavior and scope can be discussed.

### Release process

Releases are CI-driven. Pushing a `v*` tag runs the Release workflow, builds and tests the solution, publishes a self-contained single-file `EnvMaid.exe` for `win-x64`, and attaches it to a GitHub Release after production approval.

```powershell
git tag v1.0.0
git push origin v1.0.0
```
