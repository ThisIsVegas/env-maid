# Windows PATH Reference — Moved

> **This file is a compatibility redirect. Do not add guidance here.**

The canonical PATH and Windows environment-variable reference is now:

[`windows-environment-variables-reference.md`](windows-environment-variables-reference.md)

Relevant sections:

| Was here | Now |
|---|---|
| PATH syntax and parsing | §9 PATH syntax, §9.1 Entry data model |
| Raw vs effective directories | §9.2 |
| Duplicate detection | §9.3 Duplicate confidence levels |
| Machine/User composition | §9.4 |
| Executable search order | §9.5 Resolver profiles |
| App Paths | §9.6 |
| Path length / `MAX_PATH` | §9.7 |
| `PATHEXT` | §10 |
| Registry storage and value types | §4, §4.3 Safe registry string handling |
| Size limits and the `setx` hazard | §6, §6.1 |
| Propagation / `WM_SETTINGCHANGE` | §7 |
| Read/write API matrix | §8 |
| Implementer's checklist | §16 |
| Test matrix | §17 |
| Unsourced claims | Appendix A — Empirical claims backlog |

Remove this file once nothing links to it.
