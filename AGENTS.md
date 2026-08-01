# AGENTS.md

Guidance for coding agents (Codex, Claude Code, and others) working in this repository.

The full architecture, conventions, and the reasoning behind the non-obvious rules live in
**[CLAUDE.md](CLAUDE.md)** — read it first. It is the single source of truth for this project and
is kept current; this file only adds what an agent needs on top of it.

Previously this file held its own copy of that guidance, which silently drifted out of date. Add
project knowledge to CLAUDE.md, not here.

## Quick reference

Windows on x64 and the .NET 10 SDK are required — the project targets `net10.0-windows` and uses WPF.

```
dotnet build EnvMaid.slnx --configuration Release
dotnet run --project src/EnvMaid.App
dotnet test src/EnvMaid.App.Tests/EnvMaid.App.Tests.csproj
dotnet test src/EnvMaid.App.Tests/EnvMaid.App.Tests.csproj --filter "FullyQualifiedName~ConflictAnalysisServiceTests"
```

## Working rules

- **[docs/knowledge/](docs/knowledge/) is the specification, not background reading.** The `§N`
  references in source comments point into
  [windows-environment-variables-reference.md](docs/knowledge/windows-environment-variables-reference.md).
  Read the cited section before changing the behavior it guards. Several rules there were
  established by measurement and contradict Microsoft's own documentation.
- **Do not weaken a safety rule to make a change simpler.** The backup-before-write ordering, the
  optimistic-concurrency baseline check, the intent-file ACL and owner checks, the write-verify-then-delete
  order in `Rename`, and the length gate on both sides of the privilege boundary each exist because
  the alternative loses data or opens a privilege-escalation path. CLAUDE.md explains each one.
- **Nothing writes to the environment outside the existing Save/Restore flow**, which confirms,
  backs up, checks the baseline, writes, verifies the read-back, and broadcasts once.
- **Preserve the explanatory comments.** They record why a rule exists and are load-bearing for the
  next reader.
- Add or update tests for behavior changes, then run the Release build and the full suite before
  reporting done.
- Tests are service-level xUnit with no UI. Everything is reachable through constructor-injected
  seams — see the Conventions section of CLAUDE.md.
