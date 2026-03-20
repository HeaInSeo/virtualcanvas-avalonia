# VirtualCanvas.Avalonia — Claude Code Guidelines

## 1. Responsibility boundary (immutable)

**VCA owns**: rendering infrastructure, viewport math primitives (`Offset`/`Scale`/`ActualViewbox`),
spatial index (`ISpatialIndex`/`PriorityQuadTree`), realize/virtualize lifecycle,
batching contract (`BeginUpdate`/`EndUpdate`), visual factory seam (`IVisualFactory`),
and pinning primitive (`Pin`/`Unpin`/`IsPinned`).

**DagEdit owns**: editor state (`Dag`, `DagEditorViewModel`), selection policy, undo/redo
(`UndoRedoStack`), keyboard UX (`EditorGestures`), interaction quality (grid snap, connector
snap, rubber-band, context menu), and all UX semantics.

Do not add selection policy, undo history, modifier-key interpretation, or DagEdit domain
types (`DagNode`, `DagConnection`) into VCA. VCA is infrastructure, not a widget.

## 2. DagEdit WPF reference is read-only

`VirtualCanvas-ref/` (if present) contains the original WPF VirtualCanvas for porting analysis.
**Never modify it.**

## 3. Staged adoption order — no skipping

```
Stage 1  Viewer       [COMPLETE — 2026-03-14]  DagEdit consumes VCA as read-only viewer
Stage 2  Hybrid       DagEditorCanvas → VirtualCanvas items host replacement
Stage 3  Full Editor  VCA stable API consumer only
```

Prerequisites for Stage 2 must be verified before adding Stage 2 VCA API surface.
See `docs/INTEGRATION_EXECUTION_VCA.md §7`.

## 4. Integration contract precedence

Canonical source: `docs/INTEGRATION_CONTRACT.md` (THIS repo)
Mirror: `DagEdit/docs/INTEGRATION_CONTRACT.md`

- **Patch** (wording, typo): update canonical directly; notify DagEdit session.
- **Minor/Major**: update canonical first → DagEdit session mirrors after canonical is merged.
- Section 2 (coordinate formula) requires explicit user approval for any change.

## 5. Decision checklist before every change

- Does it add `DagNode`/`DagConnection`/domain knowledge into VCA? **Block immediately.**
- Does it add selection policy into VCA? **Block immediately.**
- Does it touch the coordinate formula? **Requires user approval + both test suites green.**
- Does it implement a Stage 2 API without Stage 2 prerequisites verified? **Block.**
- Does it unilaterally resolve a deferred item from `docs/INTEGRATION_CONTRACT.md §4.3`? **Block.**

## 6. Small diffs; no unrelated refactors

Each commit must have a single, stated purpose. Do not clean up surrounding code, add
comments to unchanged lines, or refactor while fixing a bug.

## 7. Warning policy

VCA maintains a **zero-warning baseline**. This must never regress.

If a change introduces new warnings, fix them in the same commit before closing the batch.
Run `dotnet build VirtualCanvas.Avalonia.sln` after every change to verify warning count = 0.
The `.editorconfig` at repo root is authoritative for code style rules.

## 8. Coordinate formula (frozen)

```
world  = (screen + Offset) / Scale
screen =  world  × Scale   − Offset
```

Validated by `tests/VirtualCanvas.Avalonia.Tests/ViewportContractTests.cs`.
Any change to this formula is a **Major** contract update requiring user approval
and atomic sync to DagEdit's `docs/INTEGRATION_CONTRACT.md` and test files.

## 9. Validation responsibility

| Change type | Expected validation |
|---|---|
| New API surface | Tests in matching `*.Tests` project |
| Bug fix | Regression test that would have caught the bug |
| Refactor | Existing tests green; add tests if coverage was absent |
| Purely mechanical cleanup | No new tests required; existing tests must still pass |

## 10. Completion reporting

A task is not complete until stated explicitly:

- **What changed**: files and logic affected
- **Validation run**: which tests, lint checks, or manual verifications were performed
- **Results**: pass/fail counts, warning counts, any regressions
- **Remaining risks**: known unknowns, deferred items, or assumptions not verified
