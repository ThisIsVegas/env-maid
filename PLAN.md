# Plan: PATH Dashboard + Conflict Resolution redesign

Scope: **Windows PATH only.** No broader environment-variable management.

Design principle (internal, never surfaced in UI): surface simple info first, go
deep on demand. No "maid" wording anywhere — plain professional labels.

---

## 1. Summary strip (always-visible, above the tabs)

A horizontal strip sitting above the existing `TabControl` in `MainWindow`. Not a
separate view, not a tab — no navigation. Five stat tiles:

| Tile | Value | Source |
|------|-------|--------|
| **Health** | worst-severity label: `Healthy` / `Minor` / `Needs attention` | any High-confidence flag → Needs attention; else any Low → Minor; else Healthy |
| **Broken** | count | non-exist + empty entries (High) |
| **Duplicates** | count | duplicate flag (High) |
| **Conflicts** | count | number of shadowed exe names |
| **Length** | `N / 2047`, colored | existing length calc; green/orange/red |

- **Scope toggle** `Combined | User | System`, default **Combined**. Segmented
  control at the strip's right. Filters the strip's numbers **only** — does NOT
  drive the tab selection. Tabs stay independent.
- **Conflicts tile is clickable** → selects the Conflicts tab.
- Health is qualitative on purpose. No 0–100 score (fake precision).

New: `DashboardViewModel` (or computed properties on `MainViewModel`) that
aggregates counts across both scopes and recomputes on any `Entries` change and
after `Rescan`. `MainViewModel` already subscribes to both collections'
`CollectionChanged` — hook the recompute there.

---

## 2. Tabs

`User PATH | System PATH | Conflicts`. The third tab is new.

---

## 3. Confidence — 3 bands (conflicts only)

New enum, **separate** from the existing `FlagConfidence` (which stays as the
grid's cleanup severity — do not overload it):

```csharp
public enum ConflictConfidence { LikelyFalsePositive, Possibly, LikelyReal }
```

Band rule per shadowed exe, **first match wins**:

1. **LikelyFalsePositive** — exe name matches the hardcoded **denylist**
   (`unins*`, `setup`, `install*`, `*redist*`, `update*`, `vcredist*`, `*setup*`…),
   OR winner and loser files are **byte-size identical** (`FileInfo.Length` equal
   = same tool copied twice, nobody cares which wins). No hashing.
2. **LikelyReal** — exe name (without extension) is in the CLI-tool **allowlist**
   (built-in ∪ user, see §4).
3. **Possibly** — everything else (a real shadow, unknown exe, differing sizes).

---

## 4. CLI-tool lists

### Built-in allowlist — embedded resource
- Ships compiled into the exe (`<EmbeddedResource>` in csproj). User can't corrupt
  or delete it. Consistent with the self-contained single-file build.
- Populate as broadly as possible: language runtimes (`java node python python3
  ruby php perl`), package managers (`npm pip cargo dotnet gem composer`),
  compilers/toolchains (`gcc clang rustc go javac`), common CLIs (`git docker
  kubectl terraform aws az gh`), etc. Stored without extension, matched
  case-insensitively.
- Viewable in-app via a "View known tools" action (read-only), so users can see
  what's covered before overriding.

### User allowlist — loose editable file
- Location: `%APPDATA%\EnvMaid\cli-tools.txt` (beside existing `backups/`).
- Plain text, one entry per line, `#` comments. Two behaviors:
  - `sometool` → **add** to allowlist (promote to LikelyReal).
  - `!builtinname` → **suppress** a built-in entry (failsafe for a wrong built-in).
- **Failsafes:**
  - Adding a name already in the built-in list → silent no-op (case-insensitive
    dedupe). No error, no duplicate.
  - `!name` removes a built-in from the effective allowlist even though the
    embedded list is read-only.
  - Missing/unreadable file → just use built-in, no crash.
- In-app: **"Open CLI tools file"** (launches in the default text editor) +
  **"Reload"** (re-reads file, recomputes confidence). No custom editor UI.

### Denylist — hardcoded, not user-editable
Fixed set of installer/uninstaller name patterns. Stable and universal; no user
management. (Revisit only if a real false-negative shows up.)

---

## 5. Conflicts tab

New view + viewmodel. Data grouped by **exe name** (not by folder — this is a
reshape from the current model, which attaches shadows to the losing entry).

New aggregation: `ConflictGroup { ExeName, Winner (folder+scope), Losers[]
(folder+scope), Confidence }`. Built from the existing shadow scan
(`OrphanDetectionService.ApplyShadowFlags`), regrouped by exe name.

Per group, the tab shows:
- Exe name.
- **Winner** folder clearly marked (the one that resolves first) + its scope.
- **Losers** listed, each with folder + scope.
- **Confidence band** (colored, §3).
- For each loser folder: **what else it contains** (other CLI exes), so "is the
  loser still needed?" is answered on screen.

### Actions

**Pick winner** — same-scope reorder only.
- If winner and loser are in the **same scope**: move the winner's folder directly
  above the loser's in that scope's `Entries` (one `Entries.Move`, staged).
- If **cross-scope** (winner=User, loser=System, or vice-versa): reorder can't fix
  it (System PATH always resolves before User). No button — show an **advisory
  note**: "System PATH always wins; delete the losing copy to resolve." That note
  is the only meaning of "advise" — explanatory UI text, not a feature.

**Delete loser** — remove the loser folder from PATH (staged), gated by a
**coverage prompt** (Conflicts tab only, not the grid):

> Delete `C:\Python39` from PATH?
> This folder provides: `pip.exe`, `python.exe`, `idle.exe`
> - `python.exe` — still covered by `C:\Python311` ✓ (safe)
> - `pip.exe` — **not on PATH anywhere else** ✗ (you lose this command)
> - `idle.exe` — **not elsewhere** ✗
> [Delete] [Cancel]

Per exe in the folder, check whether another PATH folder still provides it (reuse
the shadow scan). Green ✓ = redundant/safe. Red ✗ = unique here, deleting kills
that command. This is "notice if deleting commands will be won by other
conflicts."

---

## 6. Grid changes (PathPanel)

- **Remove** the inline shadow expander: the `Conflicts` toggle-button column
  content that expands, the shadow section of `RowDetailsTemplate`, and the four
  shadow context-menu commands (`OpenShadowFolder`, `CopyExeName`,
  `CopyShadowedFolderPath`, `SearchMultipleVersions`) plus `ConflictToggle`
  handler. Net deletion. (Keep the length-boundary row-detail block.)
- **Keep a static confidence-colored marker** in the Conflicts column: a single
  dot/pill colored by conflict band (red=LikelyReal, amber=Possibly,
  grey=LikelyFalsePositive). Not a count, not expandable — a marker only.
- **Double-click a conflicted row** (or the strip's Conflicts tile) → jump to the
  Conflicts tab.

---

## 7. Save flow — everything staged, mandatory diff gate

PATH edits are dangerous — nothing touches the real environment until Save. All
edits (grid + Conflicts-tab reorder/delete) mutate the in-memory `Entries` only;
the strip and conflict list recompute live from staged state.

**Save All → mandatory diff dialog** (the confirm gate, replaces the current
one-click save). Per scope, showing only what changed:

```
User PATH
  + C:\NewTool\bin        (added)
  - C:\Python39           (removed)
  ⚠ Entry order changed

System PATH
  (no changes — or: ⚠ requires elevation to apply)
```

- Added `+` / removed `-` computed by set diff on paths.
- Reorder shown as a **single "order changed" banner** per scope if the surviving
  entries' relative order differs. No per-row move arrows (no LCS).
- Unchanged entries hidden.
- System section flags elevation if it changed.
- Buttons: `[Confirm & Save] [Cancel]`. Auto-backup on confirm (existing
  `BackupService`, unchanged). Cancel → nothing written.

---

## Deliberate non-goals (ponytail)

- **No LCS precise move-diff** — "order changed" banner instead. Add when
  reordering becomes common enough that the banner isn't enough.
- **No cross-scope auto-resolve / winner-swap** beyond same-scope reorder — delete
  or advisory note handles cross-scope. Add when users actually ask for it.
- **No 0–100 health/confidence scores** — qualitative bands only.
- **No user-editable denylist** — hardcoded. Add when a real false-negative appears.
- **No custom list-editor UI** — open the file in the default editor.

---

## Build / structure impact

- `EnvMaid.App.csproj`: add `<EmbeddedResource>` for the built-in allowlist.
- New models: `ConflictConfidence` enum, `ConflictGroup`.
- New service: `CliToolListService` (load embedded + user file, apply `!`
  overrides, dedupe, expose allowlist + reload).
- Extend `OrphanDetectionService`: assign `ConflictConfidence` per shadow using
  allow/deny lists + size compare; build `ConflictGroup`s.
- New: `ConflictsView` + `ConflictsViewModel`; `SaveDiffDialog` +
  diff-computation; dashboard aggregation on `MainViewModel`.
- `MainWindow.xaml`: summary strip + third tab.
- `PathPanel.xaml`: strip inline shadow UI, add static marker, double-click nav.
- Tests: `CliToolListService` (dedupe, `!` override, missing file),
  confidence banding (deny/allow/size cases), coverage computation, save-diff
  add/remove/reorder detection.
