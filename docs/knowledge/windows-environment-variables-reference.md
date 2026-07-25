# Windows Environment Variables — Canonical Reference & Implementation Specification

**Document version:** 2.0
**Last revised:** 2026-07-25
**Status:** Canonical. This document is the source of truth for Windows environment-variable
behavior in this repository. `windows-path-reference.md` is a compatibility redirect and must
not receive new guidance.

---

## 0. How to read this document

### 0.1 Evidence taxonomy

Every non-obvious claim carries one of these tags. Do not silently promote a claim to a
stronger tag; changing a tag requires either a new source link or a recorded test result.

| Tag | Meaning | Obligation |
|---|---|---|
| **[DOC]** | Directly supported by current Microsoft documentation. | Must carry a link. |
| **[DOC-HIST]** | Supported by Microsoft documentation from a superseded product generation. Probably still true, not currently republished. | Must carry a link **and** a regression test. |
| **[EMP]** | Observed behavior. No Microsoft statement found. | Must have an entry in [Appendix A](#appendix-a--empirical-claims-backlog) with builds tested and a verification date. |
| **[INF]** | Follows logically from a **[DOC]** claim but is not stated directly. | Must name the claim it is inferred from. |
| **[POLICY]** | A chosen behavior of this program. Not a Windows fact. | Must be justified. |

> ⚠️ **[EMP]** and **[INF]** claims are the ones that will eventually bite. They are the
> reason Appendix A exists.

### 0.2 Implementation-status labels

Sections carry one of:

- 🟢 **CURRENT** — a dependency of the PATH feature as implemented today.
- 🔵 **PATH-SPECIFIC** — current PATH behavior.
- 🟡 **PLANNED** — specification for the future general environment-variable editor.
- ⚪ **BACKGROUND** — reference only; nothing depends on it yet.

### 0.3 Implementation status

```
Program currently manages the User and Machine `Path` environment variable.

The general environment-variable sections document Windows behavior that the
current PATH implementation depends on, and define the foundation for a planned
general environment-variable management feature.

Status:
- User and Machine PATH management ......................... implemented
- PATH analysis and conflict detection ..................... implemented
- PATHEXT-aware resolution ................................. planned / partial
- General persistent environment-variable management ....... planned
- Process and volatile environment views ................... informational / planned
```

### 0.4 Scope note

Microsoft has never published a formal specification for the environment-variable system or
for `PATH`. Behavior is defined piecemeal across the Win32 process/loader APIs, the Shell
docs, the registry reference, and the command reference. This document assembles those
pieces; it is not itself normative on Windows.

---

# Part I — Environment foundations

## 1. The three-scope model 🟢 CURRENT

There is no single "the environment." There are three distinct things, and conflating them
causes most environment-manager bugs.

| Scope | Lives in | Lifetime | Who can write | Evidence |
|---|---|---|---|---|
| **Process** | The process's in-memory environment block | Until process exit | The process itself | **[DOC]** |
| **User** | `HKCU\Environment` | Persistent | The user, no elevation | **[DOC]** |
| **Machine** | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` | Persistent | Administrators | **[DOC]** |

**[DOC]** Every process has an environment block; there are two types of persisted
variables — user (per user) and system (for everyone). A child process inherits its parent's
environment by default. `SetEnvironmentVariable` affects **only the calling process** and has
no effect on system environment variables.
→ [Environment Variables (Win32)](https://learn.microsoft.com/en-us/windows/win32/procthread/environment-variables)

**[INF]** (from the above) Persisted changes never reach already-running processes, and no
API exists to make them.

### 1.1 The volatile scope ⚪ BACKGROUND

`HKCU\Volatile Environment` holds session-scoped values (`SESSIONNAME`, `LOGONSERVER`, etc.)
rebuilt at logon.

- **[EMP]** `EMP-01` — that this key is the origin of those values and is rebuilt per session.
- **[POLICY]** Never write to it. Display it read-only, clearly labeled, so users understand
  why a variable visible in `set` output does not appear in either editable scope.

---

## 2. The environment block ⚪ BACKGROUND *(needed only if you build blocks for child processes)*

### 2.1 Binary format

**[DOC]** Format is:

```
Var1=Value1\0
Var2=Value2\0
...
VarN=ValueN\0\0
```

**[DOC]** Termination differs by encoding — this is a common source of buffer bugs:

- **ANSI block:** terminated by **two** zero bytes (one for the last string, one for the block).
- **Unicode block:** terminated by **four** zero bytes (two for the last string, two for the block).

**[DOC]** `CreateEnvironmentBlock` returns an array of null-terminated **Unicode** strings
ending with `\0\0` (two wide nulls).
→ [`CreateProcessW`](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw),
[`CreateEnvironmentBlock`](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createenvironmentblock)

### 2.2 Sorting

**[DOC]** The system uses a **sorted** environment; when supplying your own block you must
sort entries alphabetically.

- **[EMP]** `EMP-02` — the exact collation (case-insensitive? ordinal? locale-aware?) is not
  documented. If you need byte-exact reproduction of what Windows produces, test it.

### 2.3 Hidden `=X:` variables

**[DOC]** There is a variable named `=C:` whose value is the current directory on drive C
(and equivalents per drive). If an application supplies its own environment block, this
information is **not** automatically propagated and must be added manually — typically at
the front, because of the sort order.

- **[POLICY]** When enumerating a block, treat entries whose name begins with `=` as hidden.
  Exclude them from the UI by default. A block *writer* must preserve them.
- Note the tension with §3: names cannot contain `=`, yet these names *begin* with it. They
  are a documented special case, not a contradiction to resolve.

### 2.4 Reading the current process's block

**[DOC]** `GetEnvironmentStrings` returns a pointer that must be treated as **read-only** —
do not modify in place. Use `SetEnvironmentVariable` to change a value; call
`FreeEnvironmentStrings` when done.

---

## 3. Naming and value rules 🟡 PLANNED

| Rule | Detail | Evidence |
|---|---|---|
| `=` in names | **Forbidden.** The equal sign is the separator. | **[DOC]** |
| Case sensitivity | Names are **case-insensitive**: "Case is ignored when looking up the environment-variable name." | **[DOC]** |
| Canonical casing | Windows displays some names canonically (`Path`, `ComSpec`). | **[EMP]** `EMP-03` — cosmetic only; no documented contract. Preserve user casing on write; compare case-insensitively. |
| Deleting | Pass `NULL` as the value to `SetEnvironmentVariable` to delete from the current process. | **[DOC]** |
| Empty vs absent | Setting a persisted variable to `""` is not the same as deleting it. | **[INF]** from the registry model — a value can exist with zero-length data. Must be representable distinctly; see §14. |
| `%` in values | Legal, but interpreted as an expansion marker in `REG_EXPAND_SZ` and by `cmd`. | **[EMP]** `EMP-04` — no documented escape mechanism at the registry layer. |

→ [Environment Variables](https://learn.microsoft.com/en-us/windows/win32/procthread/environment-variables),
[`ExpandEnvironmentStringsW`](https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-expandenvironmentstringsw)

**[POLICY]** Validation on create/rename: reject names containing `=`; warn on names
containing `%`, whitespace, or non-ASCII; warn on names colliding case-insensitively with an
existing one in the same scope.

---

## 4. Registry storage 🟢 CURRENT

### 4.1 Layout

| Scope | Key | Notes |
|---|---|---|
| Machine | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` | Requires admin |
| User | `HKCU\Environment` | No elevation. **Preferred default.** |
| Volatile | `HKCU\Volatile Environment` | Do not write |

### 4.2 Value types

**[DOC-HIST]** For the `Path` value specifically, the documented data type is
`REG_EXPAND_SZ`, with a default of `%SystemRoot%\system32;%SystemRoot%`, and Microsoft warns
that if a user or application changes the data type, the system will not substitute the
variable with its intended value.

> **Source age note:** the explicit registry documentation for this value comes from the
> Windows Server 2003 documentation set. It very likely still describes current systems, but
> Microsoft does not currently republish a comprehensive modern PATH registry specification.
> Cover this with a regression test (`EMP-05`) rather than treating the page as a live
> contract.

→ [Path Entry (WS2003 registry reference)](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc737559(v=ws.10))

> 🚨 **The single most destructive bug this program could have** is reading a
> `REG_EXPAND_SZ` value, expanding it, and writing it back as `REG_SZ`. That bakes in
> machine-specific absolute paths; on `Path` it can leave a machine where nothing resolves.
>
> **[POLICY]** `RegQueryValueExW` returns the type in `lpType`. Round-trip it unchanged
> unless the user explicitly requests conversion (§14).

- **[EMP]** `EMP-06` — `REG_MULTI_SZ` is reportedly tolerated by the environment builder for
  some values. Never write it; the reader must degrade gracefully rather than crash.

### 4.3 Safe registry string handling 🟢 CURRENT

> ⚠️ **[DOC]** For `REG_SZ`, `REG_MULTI_SZ`, and `REG_EXPAND_SZ`, the value returned by
> `RegQueryValueEx` is **NOT guaranteed to be null-terminated.** Even when the function
> returns `ERROR_SUCCESS`, the application must ensure the string is properly terminated
> before use, "otherwise, it may overwrite a buffer." `RegGetValue` adds terminating nulls
> if needed.
>
> → [`RegQueryValueExW`](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regqueryvalueexw)

This is a memory-safety issue, not a formatting nicety. Concrete rules — **[POLICY]** except
where marked:

| Rule | Rationale |
|---|---|
| Use `W` (Unicode) APIs exclusively. | **[DOC]** The ANSI variants convert the stored Unicode string to ANSI, which is lossy. |
| Prefer `RegGetValueW` for string reads. | **[DOC]** It guarantees termination. |
| If you must use `RegQueryValueExW`, allocate `cbData + 2` bytes and append the terminator yourself. | Direct consequence of the warning above. |
| Treat all registry lengths as **byte** counts, never character counts. | `cbData` is in bytes; a `WCHAR` is 2. Off-by-half truncation is the classic bug here. |
| Include the terminating null in the byte count passed to `RegSetValueExW`. | **[DOC]** The size for string types includes terminating nulls "unless the data was stored without them" — storing without them is exactly what produces the unterminated values above. |
| Open keys with `KEY_QUERY_VALUE` or `KEY_SET_VALUE`, never `KEY_ALL_ACCESS`. | Least privilege; `KEY_ALL_ACCESS` on HKLM will also fail more often. |
| Handle unexpected value types without crashing. | See `EMP-06`. |
| Preserve the original type in every backup record. | Required for faithful undo (§15). |
| Represent "value absent" distinctly from "value present and empty". | Required for faithful undo of deletions. |
| Handle `ERROR_MORE_DATA` by re-querying; the buffer contents are undefined in that case. | **[DOC]** |

- **[EMP]** `EMP-07` — whether Windows' own environment builder tolerates an unterminated
  `Path` value, or truncates/ignores it. Worth testing, because it determines how alarming
  your "malformed value" warning should be.

---

## 5. Expansion semantics 🟢 CURRENT

### 5.1 `ExpandEnvironmentStrings`

**[DOC]** Documented behavior:

- Expands `%variableName%` using values defined for the current user.
- **Case is ignored** when looking up the name.
- **If the name is not found, the `%variableName%` portion is left unexpanded** — it is *not*
  replaced with an empty string.
- The destination buffer **cannot be the same as** the source buffer.
- Returns the number of TCHARs stored including the terminating null; if the buffer is too
  small, returns the required size. Call it twice.

→ [`ExpandEnvironmentStringsW`](https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-expandenvironmentstringsw)

**[POLICY]** Surface an unexpanded `%FOO%` as a **warning on that entry**, not a silent
pass-through and not an error. It almost always means a typo or a deleted variable.

### 5.2 What it does not support

**[DOC]** `ExpandEnvironmentStrings` does not support all `Cmd.exe` features. Explicitly
unsupported:

- `%variableName:str1=str2%` (substring replacement)
- `%variableName:~offset,length%` (substring extraction)

**[POLICY]** If the UI previews expansions, state which engine it emulates. A value that
"works in cmd" may not expand identically through the Win32 API.

### 5.3 Nesting

- **[EMP]** `EMP-08` — whether expansion is single-pass (so `%A%` where `A` contains `%B%`
  does not recursively resolve). Strongly expected, but test it, and test self-reference
  (`A=%A%`) and cycles (`A=%B%`, `B=%A%`) for non-termination.

### 5.4 Expanding for another user 🟡 PLANNED

**[DOC]** `ExpandEnvironmentStringsForUser(hToken, ...)`. If `hToken` is `NULL`, the block
contains **system variables only**. The token needs `TOKEN_IMPERSONATE` and `TOKEN_QUERY`,
and as of Windows 7 also `TOKEN_DUPLICATE`.
→ [`ExpandEnvironmentStringsForUserW`](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-expandenvironmentstringsforuserw)

---

## 6. Size limits 🟢 CURRENT

| Limit | Value | Evidence |
|---|---|---|
| Single variable | **32,767 characters** | **[DOC]** [`SetEnvironmentVariableW`](https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-setenvironmentvariablew) |
| Environment block (Vista+) | **No technical limitation**; practical limits depend on access mechanism | **[DOC]** [Environment Variables](https://learn.microsoft.com/en-us/windows/win32/procthread/environment-variables) |
| Environment block (XP / WS2003) | 32,767 characters total | **[DOC]** same page |
| ANSI process creation | `CreateProcessA` **fails** above 32,767 characters of environment block | **[DOC]** [`CreateProcessA`](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessa) |
| Batch files | Cannot set a variable longer than the max command line length | **[DOC]** |
| `setx.exe` | **1,024 characters**, cropped silently | **[DOC]** [`setx`](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/setx) |
| .NET persisted | Recommended under **2,048** for `User`/`Machine` | **[DOC]** [`Environment.SetEnvironmentVariable`](https://learn.microsoft.com/en-us/dotnet/api/system.environment.setenvironmentvariable) |
| Control Panel dialog | Refuses values over **2,047** characters | **[EMP]** `EMP-09` — from the dialog's own error text; no doc page. |
| Registry `Environment` parser | ~2,048-character limit in the parsing code | **[EMP]** `EMP-10` — [The Old New Thing](https://devblogs.microsoft.com/oldnewthing/20100203-00/?p=15083); Microsoft-employee blog, not product documentation. |

**[POLICY]** Hard-fail above 32,767. Warn above ~2,000 that the value will be uneditable in
the built-in Control Panel UI and will be destroyed by any `setx` call.

### 6.1 🚨 Never shell out to `setx`

**[DOC]** `setx PATH "%PATH%;C:\new\bin"` is destructive on any modern machine:

1. `%PATH%` expands to the **merged** machine + user value.
2. The merge is truncated to 1,024 characters.
3. The truncated result is written to the **user** scope.

System entries get duplicated into user scope, everything past 1,024 characters is lost, and
`setx` prints `SUCCESS`. There is no undo.

**[DOC]** `setx` also **expands references before storing**: if `PATH` referenced
`%JAVADIR%`, the current value of `JAVADIR` is written directly, so future updates to
`JAVADIR` are no longer reflected. Same failure class as writing `REG_SZ`.

---

## 7. Propagation and change notification 🟢 CURRENT

**[DOC]** Documented procedure for changing persisted variables:

1. Write the value into the appropriate registry key.
2. Broadcast `WM_SETTINGCHANGE` with `lParam` set to the string `"Environment"`.

This "allows applications, such as the shell, to pick up your updates."
→ [Environment Variables](https://learn.microsoft.com/en-us/windows/win32/procthread/environment-variables)

### 7.1 Precise statement of what the broadcast does

> The broadcast does **not** replace the environment block of running processes. It allows
> interested applications — most importantly the shell — to refresh their stored environment
> so that **subsequently launched** processes inherit the new values. Open terminals, IDEs,
> services, and other existing processes retain their previous environment.

Any user-facing wording along the lines of *"broadcast so running apps pick up the change
without a reboot"* is wrong and should be corrected wherever it appears, including in
architecture docs and commit messages.

- ✅ Newly launched processes (and anything launched from Explorer after it refreshes) see the new value.
- ❌ Already-running processes keep their old environment. Permanently.
- ❌ Processes that don't pump a message loop never observe the broadcast at all.

**[POLICY]** After a successful write, state explicitly that open applications must be
restarted. Never claim the change is "applied everywhere."

### 7.2 Implementation

```c
DWORD_PTR result = 0;
LRESULT rc = SendMessageTimeoutW(
    HWND_BROADCAST,
    WM_SETTINGCHANGE,
    0,
    (LPARAM)L"Environment",
    SMTO_ABORTIFHUNG,
    5000,          // ms
    &result);
// rc == 0 -> timed out or failed; the registry write still succeeded.
```

**[POLICY]** Treat "registry write succeeded" and "broadcast succeeded" as **two independent
outcomes** and record both in the backup/audit record (§15). A failed broadcast is not a
failed save, and must not trigger a rollback.

**[DOC]** .NET's `Environment.SetEnvironmentVariable` with a `User`/`Machine` target performs
the notification itself; don't double-broadcast when using it.

---

## 8. Read/write API matrix ⚪ BACKGROUND

| Task | Win32 | .NET | PowerShell | cmd |
|---|---|---|---|---|
| Read (process) | `GetEnvironmentVariableW` | `Environment.GetEnvironmentVariable(name)` | `$env:NAME` | `%NAME%` |
| Enumerate (process) | `GetEnvironmentStringsW` / `FreeEnvironmentStringsW` | `Environment.GetEnvironmentVariables()` | `Get-ChildItem Env:` | `set` |
| Write (process) | `SetEnvironmentVariableW` | `Environment.SetEnvironmentVariable(n, v)` | `$env:NAME = 'x'` | `set NAME=x` |
| Read (persisted) | `RegGetValueW` | `GetEnvironmentVariable(n, target)` | `[Environment]::GetEnvironmentVariable(n, t)` | `reg query` |
| Write (persisted) | `RegSetValueExW` + broadcast | `SetEnvironmentVariable(n, v, target)` | `[Environment]::SetEnvironmentVariable(n, v, t)` | ⚠️ `setx` — **never** |
| Expand | `ExpandEnvironmentStringsW` | `Environment.ExpandEnvironmentVariables` | `[Environment]::ExpandEnvironmentVariables()` | native |
| Another user's block | `CreateEnvironmentBlock` / `DestroyEnvironmentBlock` | — | — | — |

**[DOC]** .NET `EnvironmentVariableTarget`: `Process`, `User`, `Machine`. `User` and `Machine`
map to `HKCU\Environment` and the HKLM Session Manager key; other applications are notified
via `WM_SETTINGCHANGE`.

**[POLICY]** Use `RegGetValueW` / `RegSetValueExW` directly so you control the value type.
The .NET API is convenient but gives less control over `REG_SZ` vs `REG_EXPAND_SZ`.

---

# Part II — PATH implementation specification

## 9. PATH syntax 🔵 PATH-SPECIFIC

```
C:\Windows\system32;C:\Windows;C:\Program Files\Git\cmd
```

| Property | Rule | Evidence |
|---|---|---|
| Separator | `;` | **[DOC]** [`path`](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/path) |
| Escaping | None. A directory containing `;` cannot be represented reliably. | **[INF]** from the absence of any documented escape. |
| Quoting | Tolerated in practice; not required. | **[EMP]** `EMP-11`. The only Microsoft statement is a TechNet-archive Log Parser page saying paths with spaces "must be enclosed within quotation marks" — weak authority and contradicted by every real system's PATH. **[POLICY]** parse quotes, never emit them. |
| Entry types | Absolute, UNC (`\\server\share`), relative | **[EMP]** `EMP-12` — no page enumerates permitted entry forms; UNC and relative both work in practice. Relative is a security smell. |
| Trailing `\` | Optional | **[EMP]** `EMP-13` — see the `C:\` edge case in §14.2. |
| Order | Significant; first match wins | **[DOC]** [`path`](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/path) |
| Empty entries | Produced by `;;` or a trailing `;` | **[EMP]** `EMP-14`. The folklore that an empty entry means "current directory" has **no source**. **[POLICY]** tolerate on read, drop on write. |

## 9.1 Entry data model 🔵 PATH-SPECIFIC

Parsing, display, validation, comparison, and serialization are **five different operations**
and must not share one representation. Silently trimming whitespace during a parse can turn
a malformed token into a *different, valid* path — a data-corruption bug that looks like a
cleanup.

```
PathEntry {
    RawToken        : string   // exact substring between separators, untouched
    ParsedValue     : string   // RawToken minus surrounding quotes/whitespace — DISPLAY ONLY
    ExpandedValue   : string   // ParsedValue after %VAR% expansion — VALIDATION ONLY
    ComparisonKey   : string   // ExpandedValue, case-folded, trailing-separator normalized
    ExistenceStatus : enum     // Exists | Missing | Unknown | Inaccessible
    SourceScope     : enum     // Machine | User | Process | AppPaths
    OriginalIndex   : int      // position within its source scope, for stable ordering
    Diagnostics     : []       // warnings attached during parse/validate
}
```

Operation contract — all **[POLICY]**:

| Operation | Uses | Rule |
|---|---|---|
| Scan | `RawToken` | Preserve exactly. Never mutate during parse. |
| Display | `ParsedValue` | Show cleaned; indicate visually when it differs from `RawToken`. |
| Validate | `ExpandedValue` | Existence, length, relative-path checks operate here. |
| Duplicate detection | `ComparisonKey` | See §9.3. |
| Save (default) | `RawToken` | Round-trip byte-identical where the entry was untouched. |
| Normalize | explicit command | Trimming, quote removal, and separator cleanup are a **user-invoked action** producing a visible diff — never an invisible side effect of parsing. |

**[POLICY]** Whitespace differences between `RawToken` and `ParsedValue` produce a
`Diagnostics` warning, not a silent fix.

## 9.2 Raw tokens vs effective directories 🔵 PATH-SPECIFIC

> A raw PATH token and an effective search directory are **not necessarily one-to-one.** A
> variable reference may expand into text containing semicolons — e.g. `%TOOLCHAIN%` where
> `TOOLCHAIN=C:\a;C:\b`. One token then contributes two effective directories.

- **[EMP]** `EMP-15` — that the environment builder splits post-expansion (so `%TOOLCHAIN%`
  really does yield two search directories rather than one malformed one). This must be
  tested; it determines whether the case is "supported" or "structurally ambiguous."

**[POLICY]** The model must:

1. Preserve the original `RawToken` as the editable unit.
2. Display that it contributes *N* effective directories, expandable in the UI.
3. Flag the token as **structurally ambiguous** if a round-trip through split→join would not
   reproduce the original.
4. Refuse to auto-normalize or auto-deduplicate such a token.

This becomes more important once general variable editing lands, because editing
`TOOLCHAIN` then silently changes `PATH`'s effective content. A dependency view
(`which variables does PATH reference?`) is worth building for that reason.

## 9.3 Duplicate confidence levels 🔵 PATH-SPECIFIC

Textual normalization does **not** establish that two entries are the same directory.
Different strings can name one location via variable expansion, 8.3 short names, junctions,
symbolic links, `subst` drives, or UNC-vs-drive-letter aliasing.

| Level | Definition | Auto-remove? |
|---|---|---|
| **L1 Exact textual** | `RawToken` identical | ✅ Safe |
| **L2 Normalized textual** | `ParsedValue` identical after case-fold + trailing-separator normalization | ✅ Safe |
| **L3 Expanded-path** | `ExpandedValue` identical, `RawToken` differs | ⚠️ Offer, with a warning — the two tokens may diverge later if the referenced variables change |
| **L4 Filesystem-equivalent** | Different strings resolving to the same object (final path via `GetFinalPathNameByHandle`, or identical volume + file ID) | ❌ Advisory only. Never auto-remove. |

**[POLICY]** L3 removal must warn that it collapses two independently-maintained references
into one. L4 findings are informational and must state the mechanism (junction, short name,
subst, …) when detectable.

- **[EMP]** `EMP-16` — whether duplicate entries have any observable cost beyond lookup time
  (they don't change resolution results, only latency). Determines how aggressively to
  recommend cleanup.

## 9.4 Machine/User composition 🔵 PATH-SPECIFIC

> **Program resolution model:** the effective `PATH` is modeled as **Machine entries followed
> by User entries.** This matches observed Windows behavior but is **not** stated as a formal
> Microsoft contract.

- **[EMP]** `EMP-17` — the composition order. Universally observed; relied on by every
  third-party tool; no Microsoft page states it. Tests should protect the *program's* model
  without asserting a Windows guarantee.

**[POLICY]** Read and edit the two scopes **separately**. Never parse the merged process
`PATH` and write the result back into one scope — that is precisely the `setx` failure mode,
and it permanently duplicates system entries into the user scope.

## 9.5 Resolver profiles 🔵 PATH-SPECIFIC

There is no single global "winner." Different subsystems resolve names differently, and any
conflict analysis must declare which profile it approximates.

| Profile | Uses PATH | Current directory | PATHEXT | App Paths | Evidence |
|---|---|---|---|---|---|
| `CreateProcessW` | Yes — step 6, last | Yes — step 2; suppressible via `NoDefaultCurrentDirectoryInExePath` | No shell expansion; appends `.exe` only when no extension present | **No** (stated explicitly) | **[DOC]** |
| `cmd.exe` command | Yes | Yes, before PATH | Yes | No for a plain command; `start` routes through ShellExecute, so yes there | **[DOC]** |
| PowerShell native command | Yes | **No** — requires explicit `.\` | Yes — treats extensions in `$env:PATHEXT` as executable | Only via the Shell path for non-executables | **[DOC]** |
| `ShellExecuteEx` | Yes | Yes — CWD first | Shell adds the extension when searching App Paths | **Yes** | **[DOC]** |
| `SearchPathW` | Yes | **Order configurable** — see below | No; single `lpExtension` argument | No | **[DOC]** |
| DLL loader (`LoadLibrary`) | Yes — last stage | Position depends on safe DLL search mode | No | **No** (stated explicitly) | **[DOC]** |

### `CreateProcessW` order (fully specified)

**[DOC]** When the file name contains no directory path:

1. The directory from which the application loaded
2. The current directory for the parent process
3. The 32-bit Windows system directory (`GetSystemDirectory`) — `System32`
4. The 16-bit Windows system directory — no API returns its path, but it is searched; name is `System`
5. The Windows directory (`GetWindowsDirectory`)
6. **The directories listed in `PATH`**

**[DOC]** `CreateProcess` does **not** search App Paths; use `ShellExecute` to include it.
**[DOC]** Step 2 is suppressible: the `NoDefaultCurrentDirectoryInExePath` variable determines
what `NeedCurrentDirectoryForExePath` returns, and `CreateProcess` calls it to build the
search path for relative names.
→ [`CreateProcessW`](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw),
[`NeedCurrentDirectoryForExePath`](https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-needcurrentdirectoryforexepatha)

### `SearchPathW` and `SafeProcessSearchMode`

**[DOC]** When `lpPath` is `NULL`, `SearchPath` uses a search order governed by:

```
HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\SafeProcessSearchMode   (REG_DWORD)
```

- `1` → system path first, then the current working folder.
- `0` → **current working folder first**, then the system path.
- **The system default is `0`.**

Per-process override: `SetSearchPathMode` with `BASE_SEARCH_PATH_ENABLE_SAFE_SEARCHMODE`
(optionally `| BASE_SEARCH_PATH_PERMANENT`).

**[DOC]** Microsoft warns `SearchPath` is **not recommended** for locating a `.dll` intended
for `LoadLibrary`, because its order differs from the loader's and can locate the wrong file.
→ [`SearchPathW`](https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-searchpathw),
[`SetSearchPathMode`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setsearchpathmode)

**[POLICY]** If the program offers a "what will run when I type X?" feature, it must (a) name
the profile it simulates, (b) read `SafeProcessSearchMode` when simulating `SearchPath`, and
(c) never present a single answer as *the* answer.

### DLL loader

**[DOC]** In safe DLL search mode the order ends with the 16-bit system folder, the Windows
folder, the current folder, then `PATH`. App Paths is **not** used for DLL resolution.
Disabling safe DLL search mode moves the current folder from position 11 to position 8.
→ [Dynamic-link library search order](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order)

## 9.6 App Paths 🔵 PATH-SPECIFIC

**[DOC]** Both scopes exist, and Microsoft recommends per-user installation where appropriate:

| Scope | Key |
|---|---|
| Per-user | `HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths` |
| Per-machine | `HKLM\Software\Microsoft\Windows\CurrentVersion\App Paths` |

**[DOC]** `ShellExecuteEx` searches: the current working directory; the Windows directory
**only** (no subdirectories); `Windows\System32`; the directories in `PATH`; then App Paths.
If a subkey matches the file name, its `(Default)` value is the fully qualified path, and its
`Path` entry is **prepended to that process's `PATH`** — augmenting it without modifying the
global value.
→ [Application Registration](https://learn.microsoft.com/en-us/windows/win32/shell/app-registration)

**[POLICY]**

- Discover **both** HKCU and HKLM App Paths when diagnosing "why can't I run X?".
- Offer App Paths registration as an alternative to editing `PATH`, matching the
  application's installation scope (per-user → HKCU, all-users → HKLM). Microsoft explicitly
  recommends this over modifying the system `PATH`.

## 9.7 Path length

**[DOC]** `MAX_PATH` = 260. Lifted since Windows 10 1607 only when **both**:

- `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled` (`REG_DWORD`) = 1
- The application manifest declares `longPathAware`

→ [Maximum Path Length Limitation](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation)

---

## 10. PATHEXT and other list-valued variables 🔵 PATH-SPECIFIC

**[DOC]** When a command's first token has no extension, `Cmd.exe` uses `PATHEXT` to
determine which extensions to look for **and in what order**.
→ [`start` command](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc770297(v=ws.11))

Typical default: `.COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC`

> ⚠️ **Documentation conflict.** The `path` command reference claims a fixed precedence of
> `.exe, .com, .bat, .cmd`, contradicting the shipped default `PATHEXT` which begins
> `.COM;.EXE;…`. `PATHEXT` is authoritative in practice; that page appears to carry
> MS-DOS-era text. **[EMP]** `EMP-18` — resolve by test, and record which is true.

- **[EMP]** `EMP-19` — the interleaving. Observed behavior is **directory-major**: for each
  directory in `PATH`, try each extension in `PATHEXT` order, then move on. So `foo.bat` in
  an early directory beats `foo.exe` in a later one. No Microsoft page states the nesting.
  This directly determines conflict-analysis output, so it needs a real test.

### Other semicolon-delimited variables 🟡 PLANNED

| Variable | Consumer |
|---|---|
| `PATH` | Executable search |
| `PATHEXT` | Implicit extensions |
| `INCLUDE`, `LIB`, `LIBPATH` | MSVC toolchain |
| `PSModulePath` | PowerShell module search |

- **[EMP]** `EMP-20` — these are conventions of the consuming tools, not OS guarantees.
  **[DOC]** note: the DLL loader explicitly does **not** use `LIBPATH`.

**[POLICY]** Make "is this a list variable?" a per-variable setting with defaults for the
names above, user-overridable. Inferring from "contains a semicolon" will misfire on
connection strings and similar values.

---

# Part III — General environment-variable management 🟡 PLANNED

## 11. Create / update / delete contract

| Operation | Existing state | Result | Type handling |
|---|---|---|---|
| **Create** | Absent | New value written | Type chosen per §11.1 |
| **Update** | Present | Data replaced | **Preserve existing type** unless explicitly converted |
| **Set empty** | Present or absent | Value **present**, zero-length data | Preserve or apply §11.1 |
| **Delete** | Present | Registry value removed | n/a |
| **Rename** | Present | Create new → verify → remove old (never the reverse) | Carry the original type across |
| **Type conversion** | Present | Explicit, user-invoked operation | Requires confirmation + backup |

**[POLICY]** Rename is not atomic. Ordering matters: a crash between steps must leave the
*old* value intact, never neither.

### 11.1 Type selection for newly created variables

**[POLICY]** — this is program policy, not Windows behavior:

> A new variable receives `REG_EXPAND_SZ` when its raw value contains a `%…%` reference, and
> `REG_SZ` otherwise.

Rationale: it preserves relocatability where it matters without gratuitously marking literal
values as expandable. The user may override per variable. **Existing** variables never have
their type changed implicitly (§11 Update).

## 12. Protected variables

Grouping by name alone is wrong — it conflates persisted values, process-generated values,
profile values, and volatile session values. Protection should key on **scope and origin**.

| Variable | Typical origin | In persistent registry? | Recomputed | Recommended UI behavior |
|---|---|---|---|---|
| `Path` | Machine + User | Yes | At process creation | Strong warning; block delete |
| `PATHEXT` | Machine | Yes | At process creation | Strong warning |
| `ComSpec`, `SystemRoot`, `windir`, `SystemDrive` | System | Not an ordinary user preference | System-defined | Strong warning; block delete |
| `USERNAME`, `USERDOMAIN`, `COMPUTERNAME`, `LOGONSERVER`, `SESSIONNAME` | Session/volatile | Usually no | At logon / process creation | Read-only derived view; not editable |
| `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `ProgramData`, `ProgramFiles*` | Profile / system | Partly | At logon | Read-only by default; expert override |
| `TEMP`, `TMP` | Multiple possible sources | Sometimes | Context-dependent | Warn **and show which scope** the value came from |
| `NUMBER_OF_PROCESSORS`, `PROCESSOR_*`, `OS` | System-generated | No | At boot / process creation | Read-only |

- **[EMP]** `EMP-21` — the origin column for several of these rows. Verify by diffing
  `HKCU\Environment` + HKLM key + `Volatile Environment` against a live process block.
- **[DOC]** Microsoft's closest thing to an official catalog:
  [Recognized Environment Variables](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-7/dd560744(v=ws.10)) — **[DOC-HIST]**, Windows 7-era.

**[POLICY]** Warn, don't hard-block, except where the variable is not persisted at all (in
which case editing is meaningless and the control should simply be disabled).

## 13. Other users, services, elevated contexts

**[DOC]** For constructing or inspecting another user's environment:

| Function | Purpose |
|---|---|
| `CreateEnvironmentBlock(&block, hToken, bInherit)` | Retrieves that user's environment; passable to `CreateProcessAsUser` |
| `DestroyEnvironmentBlock(block)` | Frees it |
| `ExpandEnvironmentStringsForUser(hToken, …)` | Expands using that user's block |
| `LoadUserProfile` | Required first — see below |

**[DOC]** Gotchas:

- `hToken == NULL` → the block contains **system variables only**.
- Primary token needs `TOKEN_QUERY` **and** `TOKEN_DUPLICATE`; impersonation token needs
  `TOKEN_QUERY`.
- Passing the block to `CreateProcessAsUser` requires `CREATE_UNICODE_ENVIRONMENT`. After
  the call returns the child has its own copy, so `DestroyEnvironmentBlock` is safe.
- **User-specific variables such as `%USERPROFILE%` are set only when the user's profile is
  loaded.** Call `LoadUserProfile` first or the block will be missing exactly the variables
  you care about.

→ [`CreateEnvironmentBlock`](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createenvironmentblock)

### Elevation **[POLICY]**

- Default to the **User** scope. No elevation, per-user correct.
- Request elevation only when the user explicitly chooses Machine scope.
- On relaunch-elevated, **re-read the registry after elevation**. Never pass parsed state
  across the privilege boundary.
- A successful User-scope write combined with a failed Machine-scope write is a **partial
  success**. Report it as such; do not roll back the part that worked.

---

# Part IV — Operational specification

## 14. Concurrency: optimistic save 🟢 CURRENT

The realistic threat is an installer modifying `PATH` while the editor is open. Blind
write-back silently reverts it.

**[POLICY]** Save algorithm:

1. **At scan:** record for each value → raw bytes, registry type, and a hash. This is the
   **baseline**.
2. **Immediately before writing:** re-read the value from the registry.
3. **If current ≠ baseline:** stop. Show a three-way comparison — baseline, current on disk,
   pending edit.
4. **Let the user choose:** reload (discard edits), merge, or deliberately overwrite.
5. **Back up the value actually being replaced** — the state read in step 2, *not* the stale
   scan baseline. Backing up the baseline would silently destroy the installer's change with
   no record of it.
6. **After writing:** read back and verify.
7. **Then** broadcast (§7.2), recording broadcast success independently.

Step 5 is the one that's easy to get wrong and impossible to recover from.

## 15. Backup and undo schema 🟢 CURRENT

**[POLICY]** "Absent" must be representable separately from `""`, or undo cannot faithfully
restore a deletion or an intentionally-empty value.

```jsonc
{
  "schemaVersion": 1,
  "programVersion": "x.y.z",
  "timestampUtc": "2026-07-25T09:14:22Z",
  "identity": { "userSid": "S-1-5-21-…", "machineName": "…" },

  "target": {
    "scope": "User",                     // User | Machine
    "registryPath": "HKCU\\Environment",
    "valueName": "Path",
    "valueNameOriginalCasing": "Path"
  },

  "before": {
    "present": true,                     // false => value did not exist
    "registryType": "REG_EXPAND_SZ",
    "rawData": "…",                      // exact, unexpanded
    "hash": "sha256:…"                   // the baseline from §14 step 1
  },

  "after": {
    "present": true,
    "registryType": "REG_EXPAND_SZ",
    "rawData": "…"
  },

  "outcome": {
    "registryWriteSucceeded": true,
    "readBackVerified": true,
    "broadcastSucceeded": false,         // independent of the write
    "notes": "SendMessageTimeout timed out after 5000ms"
  }
}
```

**[POLICY]** Retain backups per-scope, timestamped, with a bounded retention policy and a
one-click restore that reproduces `present`, `registryType`, and `rawData` exactly.

## 16. Implementer's checklist

### Reading
- [ ] Read Machine and User scopes **separately**; expose the merged view read-only and label it derived.
- [ ] Use `RegGetValueW`, or terminate `RegQueryValueExW` output yourself (§4.3).
- [ ] Treat all registry lengths as **bytes**.
- [ ] Capture the registry **value type** alongside the data.
- [ ] Show `RawToken` and `ExpandedValue` distinctly; never conflate.
- [ ] Handle unexpected value types and unterminated strings without crashing.
- [ ] Hide `=X:` entries when enumerating a process block.
- [ ] Treat unexpanded `%FOO%` as a warning, not an error.

### Writing
- [ ] **Preserve the registry value type.** Never implicitly downgrade `REG_EXPAND_SZ` → `REG_SZ`.
- [ ] Never expand `%VAR%` before writing back.
- [ ] Never shell out to `setx`.
- [ ] Include the terminating null in the `RegSetValueExW` byte count.
- [ ] Open keys with least privilege (`KEY_QUERY_VALUE` / `KEY_SET_VALUE`).
- [ ] Optimistic concurrency per §14; back up the value **actually replaced**.
- [ ] Read back and verify; then broadcast; record the two outcomes separately.
- [ ] Length gate: hard-fail > 32,767; warn > ~2,000.
- [ ] Default to User scope; explicit opt-in + elevation for Machine.
- [ ] Tell the user running applications need a restart (§7.1 wording).

### PATH-specific
- [ ] Ordered list editor, not a text box.
- [ ] Duplicate detection reports confidence level (§9.3); only L1/L2 auto-remove.
- [ ] Normalization is an explicit command with a visible diff, never a parse side effect.
- [ ] Flag structurally ambiguous tokens (§9.2); never auto-edit them.
- [ ] Conflict analysis names its resolver profile (§9.5).
- [ ] App Paths discovery covers **both** HKCU and HKLM.
- [ ] Drop empty entries on write; tolerate them on read.

## 17. Test matrix

### Registry / value handling
| Case | Expect |
|---|---|
| `REG_SZ` value missing its terminating null | Read safely; flag as malformed; no buffer overrun |
| `REG_MULTI_SZ` or other unsupported type in `Path` | Degrade gracefully; do not crash; do not silently rewrite |
| `Path` stored as `REG_SZ` where `REG_EXPAND_SZ` is expected | Detect; offer explicit repair; never propagate silently |
| User `Path` completely absent | Handled as "absent", not as `""` |
| Machine `Path` completely absent | Same |
| Value at exactly 32,767 chars | Write succeeds; 32,768 fails cleanly |
| Non-ASCII in name and value | Round-trips exactly (`W` APIs throughout) |
| Name differing only by case from an existing one | Treated as the same variable |
| Delete vs set-empty | Distinguished in state, backup, and undo |
| Machine-scope write without elevation | Clean permission error, not a crash |
| User write succeeds, Machine write fails | Reported as partial success; no rollback of the successful half |

### PATH parsing
| Case | Expect |
|---|---|
| Trailing `;` | Tolerated on read; dropped on write; no phantom empty entry in UI |
| `;;` mid-value | Same |
| Quoted entry `"C:\Program Files\X"` | Parsed; quotes stripped for display; not re-emitted |
| Leading/trailing whitespace in a token | Warning raised; not silently trimmed |
| Root path `C:\` | Survives trailing-separator normalization — must not become `C:` |
| `%VAR%` expanding to multiple `;`-separated dirs | `EMP-15`; flagged structurally ambiguous |
| `%NOT_A_REAL_VAR%` | Left unexpanded; warned; value uncorrupted |
| Self-reference `A=%A%` and cycle `A=%B%`,`B=%A%` | Terminates; no hang; reported |
| Entry exceeding `MAX_PATH` | Flagged, with long-path status noted |
| Duplicate at each of L1–L4 | Correct level assigned; only L1/L2 offered for auto-removal |
| Junction / symlink / `subst` alias of an existing entry | Detected as L4 advisory, never auto-removed |

### Behavior / integration
| Case | Expect |
|---|---|
| External registry modification between scan and save | Detected; three-way diff shown; §14 step 5 backup correct |
| Backup where the prior value was absent | `present: false` recorded; undo restores absence |
| Broadcast timeout with successful write | Reported as write-success / broadcast-failure; no rollback |
| Per-user **and** per-machine App Paths discovery | Both enumerated |
| Resolution under each resolver profile in §9.5 | Distinct, correct results |
| `SafeProcessSearchMode` = 0 vs 1 | `SearchPath` simulation changes accordingly |

---

## Appendix A — Empirical claims backlog

Every **[EMP]** claim above appears here. A claim may only be promoted to **[DOC]** with a
source link, or considered settled with a recorded test result.

| ID | Claim | How to verify | Builds tested | Result | Verified |
|---|---|---|---|---|---|
| EMP-01 | `Volatile Environment` is the origin of session variables | Diff live process block vs three registry keys | — | — | — |
| EMP-02 | Environment block sort collation | Build blocks with mixed case/ordinal-adjacent names | — | — | — |
| EMP-03 | Canonical casing is cosmetic only | Write `PATH` vs `Path`; observe registry + `set` | — | — | — |
| EMP-04 | No escape for literal `%` in `REG_EXPAND_SZ` | Store `100%%` and `100%`; observe expansion | — | — | — |
| EMP-05 | `Path` is `REG_EXPAND_SZ` on current Windows | Read type on clean installs | — | — | — |
| EMP-06 | `REG_MULTI_SZ` tolerated by environment builder | Set `Path` to `REG_MULTI_SZ`, reboot, inspect | — | — | — |
| EMP-07 | Behavior with an unterminated `Path` value | Write raw bytes without terminator; observe | — | — | — |
| EMP-08 | Expansion is single-pass; cycles terminate | `A=%B%`, `B=%A%`; call `ExpandEnvironmentStrings` | — | — | — |
| EMP-09 | Control Panel 2,047-char cap | Attempt to edit a longer value in the GUI | — | — | — |
| EMP-10 | ~2,048-char registry parser limit | Store a longer value; check the built block | — | — | — |
| EMP-11 | Quoted PATH entries are tolerated | Add `"C:\dir with space"`; run an exe from it | — | — | — |
| EMP-12 | UNC and relative entries resolve | Add `\\server\share` and `.\bin`; test resolution | — | — | — |
| EMP-13 | Trailing `\` is insignificant | `C:\dir` vs `C:\dir\`; both resolve | — | — | — |
| EMP-14 | Empty entries are ignored (not CWD) | `PATH=C:\a;;C:\b` with an exe in CWD | — | — | — |
| EMP-15 | Post-expansion `;` splitting | `TOOLCHAIN=C:\a;C:\b`, `PATH=%TOOLCHAIN%`; run exes from both | — | — | — |
| EMP-16 | Cost of duplicate entries | Measure resolution latency vs entry count | — | — | — |
| EMP-17 | **Machine entries precede User entries** | Distinct marker dirs in each scope; check merged order | — | — | — |
| EMP-18 | `PATHEXT` order beats the `path` doc's claim | Same-named `.com` and `.exe` in one dir | — | — | — |
| EMP-19 | **Directory-major PATHEXT interleaving** | `a.bat` in dir1, `a.exe` in dir2; run `a` | — | — | — |
| EMP-20 | `INCLUDE`/`LIB`/`PSModulePath` list semantics | Per-consumer behavior check | — | — | — |
| EMP-21 | Origin classification in §12 table | Three-way registry vs process-block diff | — | — | — |

**Priority:** `EMP-17` and `EMP-19` gate correctness of the core feature (composition order
and conflict analysis). `EMP-05`, `EMP-06`, and `EMP-07` gate data safety. Do those six first.

---

## Appendix B — Source index

| Topic | URL |
|---|---|
| Environment Variables (Win32) | https://learn.microsoft.com/en-us/windows/win32/procthread/environment-variables |
| User Environment Variables | https://learn.microsoft.com/en-us/windows/win32/shell/user-environment-variables |
| Changing Environment Variables | https://learn.microsoft.com/en-us/windows/win32/procthread/changing-environment-variables |
| `processenv.h` index | https://learn.microsoft.com/en-us/windows/win32/api/processenv/ |
| `SetEnvironmentVariableW` | https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-setenvironmentvariablew |
| `ExpandEnvironmentStringsW` | https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-expandenvironmentstringsw |
| `ExpandEnvironmentStringsForUserW` | https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-expandenvironmentstringsforuserw |
| `SearchPathW` | https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-searchpathw |
| `SetSearchPathMode` | https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setsearchpathmode |
| `CreateEnvironmentBlock` | https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createenvironmentblock |
| `DestroyEnvironmentBlock` | https://learn.microsoft.com/en-us/windows/desktop/api/Userenv/nf-userenv-destroyenvironmentblock |
| `RegQueryValueExW` (null-termination warning) | https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regqueryvalueexw |
| `CreateProcessW` | https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw |
| `CreateProcessA` | https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessa |
| `NeedCurrentDirectoryForExePath` | https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-needcurrentdirectoryforexepatha |
| Dynamic-link library search order | https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order |
| Dynamic-link library security | https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-security |
| Application Registration (App Paths) | https://learn.microsoft.com/en-us/windows/win32/shell/app-registration |
| Path Entry (registry, WS2003) — **[DOC-HIST]** | https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc737559(v=ws.10) |
| Recognized Environment Variables — **[DOC-HIST]** | https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-7/dd560744(v=ws.10) |
| `setx` | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/setx |
| `path` | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/path |
| `start` (PATHEXT) | https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc770297(v=ws.11) |
| Maximum Path Length Limitation | https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation |
| Naming Files, Paths, and Namespaces | https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file |
| about_Command_Precedence (PowerShell) | https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_command_precedence |
| `Environment.SetEnvironmentVariable` (.NET) | https://learn.microsoft.com/en-us/dotnet/api/system.environment.setenvironmentvariable |
| `EnvironmentVariableTarget` (.NET) | https://learn.microsoft.com/en-us/dotnet/api/system.environmentvariabletarget |
| The Old New Thing — max env var length | https://devblogs.microsoft.com/oldnewthing/20100203-00/?p=15083 |

---

## Appendix C — Changelog

| Version | Date | Changes |
|---|---|---|
| 2.0 | 2026-07-25 | Merged the standalone PATH reference in; added evidence taxonomy (§0.1) and implementation-status labels (§0.2); added safe registry string handling (§4.3); added the PATH entry data model (§9.1), raw-vs-effective directories (§9.2), and duplicate confidence levels (§9.3); replaced the single search-order narrative with a resolver-profile matrix (§9.5) including `SearchPathW`/`SafeProcessSearchMode`; corrected App Paths to include HKCU (§9.6); added the create/update/delete contract (§11); reworked the protected-variable table by origin and scope (§12); added optimistic concurrency (§14) and the backup schema (§15); added Appendix A empirical claims backlog. |
| 1.0 | 2026-07-25 | Initial combined reference. |
