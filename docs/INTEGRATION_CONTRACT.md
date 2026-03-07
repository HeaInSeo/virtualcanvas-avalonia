# Integration Contract — VCA ↔ DagEdit

```
Contract-ID:    VCA-DAGEDIT-001
Version:        0.1.0
Status:         initial-draft
Change-Type:    Major (first issuance)
Canonical-Repo: virtualcanvas-avalonia
Canonical-Path: docs/INTEGRATION_CONTRACT.md
Mirrored-In:    DagEdit/docs/viewport-contract.md  (pending sync)
Last-Updated:   2026-03-07
Last-Synced:    —  (not yet synced to mirror)
```

> **Canonical source rule**: This file in the `virtualcanvas-avalonia` repo is the
> single authoritative version of the shared integration contract.
> The mirror in `DagEdit/docs/viewport-contract.md` must be kept in sync after
> every Minor or Major change. Patch-level edits do not require a sync trigger.

---

## 1. Purpose

This document fixes the integration boundary between **VCA** (VirtualCanvas.Avalonia —
rendering/virtualization infrastructure) and **DagEdit** (DAG editor — editor state,
commands, interaction, style). It is the reference that governs all future VCA API
decisions and DagEdit integration steps.

Neither repo's implementation is allowed to silently violate this contract.
A violation requires a contract update with the full update protocol (§9).

---

## 2. Fixed Contracts

### 2.1 Coordinate System

The following formulas are **immutable**. Changes require a Major version bump and
explicit approval from both repos.

```
world  = (screen + Offset) / Scale
screen = world  ×  Scale   − Offset
```

These formulas are validated by regression tests in both repos:
- VCA: `tests/VirtualCanvas.Avalonia.Tests/ViewportContractTests.cs`
- DagEdit: `tests/DagEdit.Tests/ViewportTransformTests.cs`
           `tests/DagEdit.Tests/SelectionRectTests.cs`

**Invariants:**
- World coordinates are the single source of truth for spatial data (node positions,
  connector anchors, spatial index bounds).
- Screen coordinates are derived values used only at input/render boundaries.
- `ActualViewbox.TopLeft = ScreenToWorld(Point(0, 0)) = (Offset.X / Scale, Offset.Y / Scale)`

### 2.2 Viewport State Mapping

| VCA concept | DagEdit concept | Contract |
|-------------|-----------------|---------|
| `VirtualCanvas.Offset` (`Point`) | `DagEditorViewModel.ViewportLocation` | Identical formula; direct mapping |
| `VirtualCanvas.Scale` (`double`) | `DagEditorViewModel.ViewportScale` | Identical formula; direct mapping |
| `VirtualCanvas.ActualViewbox` | derived from `ViewportLocation / ViewportScale + ControlSize / ViewportScale` | VCA computes; DagEdit uses read |
| `ISpatialIndex.GetItemsIntersecting(worldRect)` | `FinalizeSelection()` inner loop | Drop-in replacement at hybrid stage |

DagEdit's `DagEditor.ViewportLocation` / `DagEditor.ViewportScale` (StyledProperty)
are **passthrough** values — they relay to/from the ViewModel for template-binding
purposes only. These are not the source of truth; `DagEditorViewModel` is.

### 2.3 RenderTransform Convention

Both repos apply the same `TransformGroup` to their items host:

```
TransformGroup(Scale(s, s), Translate(-offset.X, -offset.Y))
```

This makes `GetPosition(itemsHost)` return world coordinates directly, without
manual inverse-transform in pointer event handlers.

---

## 3. Responsibility Boundary

### VCA is responsible for

- Spatial index management (`ISpatialIndex`, `PriorityQuadTree`)
- Viewport culling: which items intersect `ActualViewbox`
- Realize / virtualize control lifecycle
- `Scale` / `Offset` state storage and `ActualViewbox` derivation
- Batching contract (`BeginUpdate` / `EndUpdate`)
- Visual factory seam (`IVisualFactory.Realize` / `Virtualize`)
- Rendering infrastructure: `VisualChildren` management, ZIndex ordering,
  `RenderTransform`

### DagEdit is responsible for

- Editor state: node positions, connections, port layout
- `DagEditorViewModel` as SSoT for viewport state
- Selection semantics: what "selected" means, multi-selection rules, modifier keys
- Undo / redo stack
- Interaction UX: pan gesture, zoom gesture, rubber-band selection overlay,
  keyboard shortcuts
- Node / connection / port UI quality and visual design
- `ViewportTransform` utility (currently DagEdit-local; may be consolidated later)
- `FinalizeSelection()` logic (currently a foreach loop; will become an index query)

### Neither side should

- VCA must not encode selection policy, keyboard UX, or editor semantics.
- DagEdit must not implement its own spatial quad-tree or viewport culling logic
  once VCA integration is active (hybrid stage and beyond).

---

## 4. Feature Classification

### 4.1 Promote to VCA

These capabilities belong in VCA as stable primitives:

| Capability | Status in VCA | Notes |
|------------|---------------|-------|
| Viewport math / `ActualViewbox` | ✅ implemented | Tested in `ViewportContractTests` |
| Spatial query / visible range query | ✅ implemented | `GetItemsIntersecting` |
| Realize / virtualize lifecycle | ✅ implemented | `IVisualFactory` |
| `BeginUpdate` / `EndUpdate` batching | ✅ implemented | Provisional API tier |
| Bounds-change propagation contract | 🔲 not yet defined | Needed for node drag (Risk A) |
| Virtualization pinning primitive | 🔲 not yet defined | Needed for drag/select (Risk B) |
| Visual factory / host adapter seam | ✅ implemented | `IVisualFactory` |
| Semantic zoom primitive | 🔲 deferred | Post-hybrid |

### 4.2 Keep in DagEdit

These capabilities must remain in DagEdit regardless of integration depth:

| Capability | Reason |
|------------|--------|
| Selection policy (single, multi, modifier key rules) | Editor semantics, not infra |
| Selection rectangle overlay UI | Visual design / interaction quality |
| Undo / redo | Editor command domain |
| Node / port / connection meaning | Domain model, not spatial infra |
| Keyboard shortcut policy | Editor UX |
| `FinalizeSelection()` → replaced by index query at hybrid stage | Transition path, not VCA responsibility |

### 4.3 Deferred / Unresolved

| Topic | Blocker |
|-------|---------|
| `VCA.SelectedItem` usage in DagEdit | DagEdit has richer selection model; mapping unclear |
| Pinning: `IVisualFactory.Virtualize → false` vs separate pin set | Risk B; needs design before hybrid |
| Semantic zoom parity scope | Post-hybrid; depends on DagEdit use case |
| Template visual factory parity | WPF origin; Avalonia DataTemplate integration not planned |
| Hybrid stage: what moves first | Depends on DagEdit graph scale requirements |

---

## 5. Staged Adoption Path

```
Stage 1 — Viewer
  DagEditorCanvas replaced by VirtualCanvas (items host only).
  VCA.Offset ↔ DagEditorViewModel.ViewportLocation  (two-way binding)
  VCA.Scale  ↔ DagEditorViewModel.ViewportScale     (two-way binding)
  DagEdit retains all interaction handlers; VCA just renders.
  No selection integration. No drag integration.

Stage 2 — Hybrid
  FinalizeSelection() inner loop → ISpatialIndex.GetItemsIntersecting(worldRect).
  Node drag updates SpatialIndex (bounds-change propagation contract must be resolved).
  VCA.BeginUpdate / EndUpdate wraps drag operations.
  DagEdit still owns selection state and all UX.

Stage 3 — Full Editor
  VCA SelectedItem / SelectionChanged integrated (or replaced by richer primitive).
  Pinning contract defined and implemented.
  Semantic zoom (if required) implemented in VCA.
  DagEdit becomes a consumer of VCA stable API only.
```

Prerequisites for Stage 1:
- Risks A/B/C assessed (§6)
- DagEdit graph scale measured against VCA virtualization threshold

---

## 6. Current Blockers / Risks

### Risk A — Node drag: SpatialIndex update frequency

During node drag in DagEdit, the node's world position changes every pointer-move
event. If `SpatialIndex.Insert` + `RaiseChanged` is called per frame, VCA will
attempt re-virtualization on every frame, causing excessive layout churn.

**Unresolved**: Batching strategy — does DagEdit call `BeginUpdate` at drag-start
and `EndUpdate` at drag-end? Or does VCA need a "provisional bounds" path that
skips re-virtualization until commit?

### Risk B — Virtualization pinning during drag/select

When a node is being dragged or is part of the current selection, it must not
be virtualized even if it temporarily leaves `ActualViewbox`.

Current VCA mechanism: `IVisualFactory.Virtualize(control) → false` keeps the
control alive. But DagEdit must know to implement this in its `IVisualFactory`,
and the contract for "when to pin" is not yet defined.

**Unresolved**: Pin-set ownership — does VCA expose a pin API, or does
`IVisualFactory.Virtualize` carry the full burden?

### Risk C — VCA direct child management vs DagEdit control model

VCA inserts controls directly into `VisualChildren` via `IVisualFactory.Realize`.
DagEdit's `Node` is a `TemplatedControl` with its own lifecycle (data binding,
`Loaded`/`Unloaded`, focus, dispose). At Stage 1, `Node` creation must be
delegated to a `DagEdit`-supplied `IVisualFactory`, which means:
- `Node` data binding is set up inside `Realize`
- `Node` disposal is triggered inside `Virtualize`
- VCA must not interfere with `Node`'s internal focus or `Unloaded` chain

**Unresolved**: The exact seam between VCA lifetime and `Node` Avalonia lifetime
has not been prototyped. A spike is needed before Stage 1 commit.

---

## 7. Decision Rules

When evaluating whether a new capability belongs in VCA or DagEdit:

1. **Infrastructure test**: Does it operate purely on `ISpatialItem.Bounds` and
   viewport geometry, with no knowledge of domain types (node, connection, port)?
   → If yes: VCA candidate.

2. **Policy test**: Does it encode a rule about *which* items to treat specially
   (selected, focused, pinned, editable)?
   → If yes: DagEdit responsibility; VCA may expose a *primitive* (e.g., a pin set)
   but must not encode the policy.

3. **Coupling test**: Would adding this to VCA require VCA to import or know about
   DagEdit-specific types, ReactiveUI observables, or DagEdit ViewModel state?
   → If yes: Not VCA.

4. **Reversibility test**: If this capability turns out wrong, can it be removed
   without breaking DagEdit's stable-contract usage?
   → If no: defer until the design is confident.

---

## 8. Contract Update Protocol

```
Patch  (typo, example, wording — no meaning change):
  Author updates canonical directly. No sync trigger. No version bump.

Minor  (classification change, primitive candidate added, stage adjustment):
  1. Author proposes in INTEGRATION_EXECUTION_VCA.md under "Proposed Changes".
  2. Status set to "proposed" in this document.
  3. After approval: canonical updated, version bumped (0.x → 0.x+1),
     status set to "approved".
  4. Mirror (DagEdit/docs/) updated and "Last-Synced" recorded.

Major  (coordinate formula, responsibility boundary, staged path):
  1. Author proposes in INTEGRATION_EXECUTION_VCA.md. Explicit approval required.
  2. Status set to "pending-approval".
  3. After approval: canonical updated, version bumped (x.0 → x+1.0),
     change-type set to "Major".
  4. Both repos updated atomically (or within the same sprint).
```

---

## 9. Sync Rules for Agents

Any Claude Code session working in VCA **must**:

1. Read this document before proposing a change that touches `Scale`, `Offset`,
   `ActualViewbox`, `IVisualFactory`, `ISpatialIndex`, or the `BeginUpdate`/`EndUpdate`
   lifecycle.
2. Cross-reference `DagEdit/docs/viewport-contract.md` to verify parity before
   modifying the coordinate formula.
3. Never add a capability classified as "DagEdit 유지" (§4.2) to VCA.
4. Propose classification changes in `INTEGRATION_EXECUTION_VCA.md` first; do not
   directly edit this contract document.
5. Record every change to this document in the Contract Change Log (§11).

Any Claude Code session working in DagEdit must:

1. Treat `DagEdit/docs/viewport-contract.md` as a mirror, not canonical.
2. If a contradiction is found between the mirror and this file, flag it and do
   not auto-resolve — escalate to user.

---

## 10. Non-Goals / Forbidden Directions

The following are explicitly **out of scope** for VCA, permanently or until a
Major contract revision:

| Forbidden direction | Reason |
|--------------------|--------|
| VCA encoding selection policy | Violates responsibility boundary |
| VCA implementing undo/redo | Editor state domain |
| VCA drawing rubber-band selection overlay | Interaction UX quality |
| VCA knowing about `DagNode`, `DagConnection`, port types | Domain coupling |
| VCA implementing modifier-key interpretation | Keyboard UX policy |
| VCA becoming a DataTemplate-driven MVVM component | Architectural identity (infra, not widget) |
| DagEdit implementing its own quad-tree after Stage 2 | Duplicates VCA responsibility |

---

## 11. Contract Change Log

| Version | Date | Change-Type | Summary | Synced to Mirror |
|---------|------|-------------|---------|-----------------|
| 0.1.0 | 2026-03-07 | Major | Initial issuance. Coordinate formula, mapping, responsibility boundary, classification, staged path, risks A/B/C, decision rules, update protocol. | No — pending |
