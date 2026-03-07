# Integration Execution — VCA Side

> **Living document.** Updated after every integration-relevant decision or step.
> Read this before starting any VCA task that might affect DagEdit integration.

---

## 1. Purpose

This document is the VCA-side execution plan for the staged DagEdit integration.
It records:
- What VCA currently is and what it needs to become
- What work belongs in VCA vs what must stay out
- The ordered phase plan with current status
- Risks, blockers, and the decision checklist for future contributors
- A log of integration-relevant steps taken

The **canonical shared contract** lives in `docs/INTEGRATION_CONTRACT.md`.
This execution document **must not** redefine contract terms — it may only
propose changes, which are then approved and recorded in the canonical doc.

---

## 2. Current State Summary

| Aspect | Status |
|--------|--------|
| VCA version | 0.1.0-dev |
| Tests | 53 passing (36 Core + 13 Avalonia + 4 Smoke) |
| Packages | `VirtualCanvas.Core` + `VirtualCanvas.Avalonia` (nupkg, not published) |
| Coordinate contract | Implemented + tested (`ViewportContractTests.cs`) |
| `ISpatialIndex` / `ISpatialItem` | Stable |
| `IVisualFactory` | Stable contract; provisional advanced members |
| `BeginUpdate` / `EndUpdate` | Implemented; contract details unresolved for drag |
| Selection state | `SelectedItem` (single); consumer-driven styling |
| DagEdit integration stage | Pre-Stage 1 (no integration started) |
| Contract sync | `INTEGRATION_CONTRACT.md` v0.1.0 — not yet mirrored to DagEdit |

DagEdit current state (as of 2026-03-07):
- `DagEditorCanvas` is the items host (not VCA)
- `DagEditorViewModel.ViewportLocation/Scale` is SSoT for viewport state
- `DagEditor.ViewportLocation/Scale` (StyledProperty) are passthrough values
- `FinalizeSelection()` iterates `DagItems` directly (no spatial index)
- `ViewportTransform.ScreenToWorld/WorldToScreen` matches VCA formulas exactly
- `DagEdit/docs/viewport-contract.md` documents the mapping (standalone, pre-mirror)

---

## 3. VCA's Role in Integration

VCA is **rendering and virtualization infrastructure**. It is not an editor widget.

When DagEdit integrates VCA:
- VCA becomes the items host (replaces `DagEditorCanvas` as the rendering surface)
- VCA owns: spatial lookup, viewport culling, control lifecycle, ZIndex ordering
- DagEdit retains: node/connection/port domain model, editor state, interaction, style

VCA's job is to make the answer to "which items are visible right now and how do
I get their controls?" fast, correct, and virtualized. Everything above that layer
stays in DagEdit.

---

## 4. What Belongs in VCA

A capability belongs in VCA if it passes all three tests from `INTEGRATION_CONTRACT.md §7`:

1. **Infrastructure test**: Operates on `ISpatialItem.Bounds` + viewport geometry only
2. **Policy test**: Does not encode which items are "special" (selected, dragged, pinned)
3. **Coupling test**: Does not require knowing DagEdit domain types

**Confirmed VCA territory:**
- `ISpatialIndex.GetItemsIntersecting(worldRect)` — spatial query
- `ActualViewbox` — visible world rect given current Scale/Offset
- `IVisualFactory.Realize/Virtualize` — control lifecycle seam
- `BeginUpdate/EndUpdate` — realization batching
- `Scale`, `Offset`, `RenderTransform` — viewport state primitives
- Bounds-change propagation contract *(not yet implemented — needed for node drag)*
- Virtualization pinning primitive *(not yet implemented — needed for drag/select)*

**Provisional VCA territory** (designed but may change):
- `SelectedItem` / `SelectionChanged` — state storage only; policy is consumer's
- `ThrottlingLimit`, `IsPaused` — throttling tuning
- `RealizationCompleted` — post-realization hook

---

## 5. What Must Stay Out of VCA

Do not add to VCA:

| Capability | Belongs in |
|------------|------------|
| Selection policy (single, multi, modifier rules) | DagEdit |
| Rubber-band selection rectangle overlay | DagEdit |
| Keyboard shortcuts / gesture interpretation | DagEdit |
| Undo / redo | DagEdit |
| Node / connection / port domain types | DagEdit |
| DataTemplate-driven visual factory | Not planned (see DECISION_LOG DEC-007) |
| Semantic zoom UI (LOD switching) | Deferred; post-hybrid |
| Any ReactiveUI dependency | DagEdit's domain |

If a new capability is being considered and it is not on the "confirmed VCA territory"
list, apply the three tests from §4. If in doubt, record it as "Deferred" in §7 and
do not implement.

---

## 6. Phase Plan

### Near-term (pre-Stage 1 prerequisites)

| Task | Status | Notes |
|------|--------|-------|
| Resolve Risk A: node drag + SpatialIndex update strategy | 🔲 | Spike needed; see §7 |
| Resolve Risk B: virtualization pinning contract | 🔲 | Design decision needed |
| Risk C: Node TemplatedControl ↔ VCA lifetime spike | 🔲 | Prototype needed |
| Mirror `INTEGRATION_CONTRACT.md` to DagEdit repo | 🔲 | Pending first sync |
| Measure DagEdit graph scale (typical node count) | 🔲 | Determines if virtualization ROI justifies Stage 1 |

### Stage 1 — Viewer (DagEditorCanvas → VirtualCanvas)

| Task | Status | Notes |
|------|--------|-------|
| DagEdit: implement `IVisualFactory` for `Node` | 🔲 | Risk C must be resolved first |
| DagEdit: two-way bind `VCA.Offset ↔ ViewportLocation` | 🔲 | Formula verified; binding plumbing TBD |
| DagEdit: two-way bind `VCA.Scale ↔ ViewportScale` | 🔲 | Same |
| VCA: verify no regression in pan/zoom with DagEdit binding | 🔲 | Integration test needed |
| DagEdit: remove `DagEditorCanvas` rendering logic | 🔲 | After binding verified |

### Stage 2 — Hybrid

| Task | Status | Notes |
|------|--------|-------|
| DagEdit: wrap node drag in `VCA.BeginUpdate/EndUpdate` | 🔲 | Risk A resolution |
| DagEdit: `FinalizeSelection` → `ISpatialIndex.GetItemsIntersecting` | 🔲 | Formula parity already confirmed |
| VCA: bounds-change propagation contract | 🔲 | Required for drag |
| VCA: pinning primitive (pin set or `IVisualFactory` contract clarification) | 🔲 | Risk B resolution |

### Stage 3 — Full Editor (later)

| Task | Status | Notes |
|------|--------|-------|
| VCA `SelectedItem` ↔ DagEdit selection model integration | 🔲 | Deferred; classification pending |
| Semantic zoom primitive | 🔲 | Deferred; scope TBD |
| VCA NuGet publish (1.0.0) | 🔲 | After API stable across Stage 2 |

---

## 7. Risks / Blockers

### Risk A — Node drag: SpatialIndex update frequency

**Problem**: Each pointer-move during node drag updates node position.
If `SpatialIndex.Insert` + `RaiseChanged` fires per-frame, VCA triggers
re-virtualization on every event → excessive layout churn.

**Options being considered** (not yet decided):
1. Wrap entire drag gesture in `BeginUpdate` / `EndUpdate` — simplest; only
   re-virtualizes at drag-end. Downside: item may disappear mid-drag if it leaves
   `ActualViewbox` before the drag ends.
2. "Provisional bounds" API — VCA receives an in-flight bounds update that
   adjusts the control's `Arrange` position but does not trigger re-virtualization.
   More complex; new VCA API required.
3. Pin the dragged node during drag (Risk B overlap) — combine with option 1.

**Resolution required before Stage 2.**

### Risk B — Virtualization pinning during drag/select

**Problem**: A node being dragged or part of the current selection must not be
virtualized when it temporarily leaves `ActualViewbox`.

**Current VCA mechanism**: `IVisualFactory.Virtualize(control) → false` keeps
the control alive. But the DagEdit-supplied factory must know *which* items
to pin (dragged item, selected items). This policy knowledge must come from
DagEdit, not VCA.

**Options being considered**:
1. DagEdit maintains a `HashSet<ISpatialItem>` of pinned items; its
   `IVisualFactory.Virtualize` checks this set.
2. VCA exposes a `PinnedItems` property — factory checks VCA's set.
   Simpler for DagEdit; adds API to VCA.
3. DagEdit overrides `ShouldVirtualize` via subclassing `VirtualCanvas`.
   Requires `ShouldVirtualize` to be virtual/protected — currently private.

**Resolution required before Stage 2.**

### Risk C — VCA direct child management vs DagEdit Node lifecycle

**Problem**: VCA inserts controls into `VisualChildren` via `IVisualFactory.Realize`.
DagEdit's `Node` is a `TemplatedControl` that manages its own lifecycle (data
binding, `Loaded`/`Unloaded`, `Dispose`, focus, `CompositeDisposable`).

**Specific concerns**:
- `Node` subscribes to ReactiveUI observables in its constructor/`OnApplyTemplate`.
  If VCA virtualizes and re-realizes `Node`, these subscriptions must be re-established
  or must survive re-parenting.
- `BaseNode.Unloaded += Dispose()` — if VCA removes `Node` from `LogicalChildren`,
  `Unloaded` fires → `Dispose()` is called → subscriptions torn down. Re-realize
  would produce a disposed `Node`.
- Focus: `ShouldVirtualize` returns false if `control.IsFocused`. This guard exists
  in VCA but has not been tested with `Node`'s focus model.

**Resolution**: A prototype spike must verify that a `Node` can be virtualized
(removed from VCA's `VisualChildren`) and re-realized without:
- Subscription leak
- Premature dispose
- Focus loss causing virtualization failure

**Resolution required before Stage 1.**

---

## 8. Decision Checklist for Future Changes

Before merging any VCA change, answer:

- [ ] Does this change touch `Scale`, `Offset`, or `ActualViewbox`?
      → Verify `ViewportContractTests` still pass. Verify DagEdit formula parity.
- [ ] Does this change add a new public member?
      → Is it in "Confirmed VCA territory" (§4)? If not, defer.
- [ ] Does this change encode any selection or interaction policy?
      → If yes, reject. Route to DagEdit.
- [ ] Does this change require knowing DagEdit domain types?
      → If yes, reject. Create an abstraction or a protocol instead.
- [ ] Does this change affect `IVisualFactory` or `ISpatialIndex` contracts?
      → Record in `DECISION_LOG.md` and update `INTEGRATION_CONTRACT.md` if needed.
- [ ] Does this change affect the `BeginUpdate`/`EndUpdate`/`InvalidateReality` lifecycle?
      → Risk A dependency — check if batching strategy is impacted.
- [ ] Is the contract version still correct after this change?
      → If a Minor or Major contract change is implied, follow the update protocol.

---

## 9. Step Log

| Step | Date | Summary |
|------|------|---------|
| Phase A–C | 2026-03 | Core library, VirtualCanvas control, DevApp, packaging |
| Phase D-0 | 2026-03-07 | Release hardening: CHANGELOG, XML docs, CI pack |
| Phase D-0.1 | 2026-03-07 | Contract alignment: viewport tests, README/API doc, LICENSE |
| Phase D-0.2 | 2026-03-07 | Integration contract documents created (this file + INTEGRATION_CONTRACT.md) |

---

## 10. Next Single Small Diff

**Completed (2026-03-07)**: DagEdit repo now has `docs/INTEGRATION_CONTRACT.md` as
the mirror file. Header-level sync against canonical v0.1.0 is done.

**Next proposed**: Body semantic sync — verify that the mirror's body sections
(responsibility boundary, classification, staged path, risks) are semantically
equivalent to the canonical. Record completion by updating `Last-Synced` in canonical
from "header-only" to "full".

**Alternative next diff** (if body sync is deferred):
- Risk C spike: prototype `Node` virtualize/re-realize cycle in a headless test
  to verify lifecycle correctness before Stage 1 commits.

---

## 11. Contract Sync Dependency

```
Canonical contract version:  0.1.0
Mirror (DagEdit):             docs/INTEGRATION_CONTRACT.md  — header-only sync done 2026-03-07
Body semantic sync:           Pending (not yet verified for full equivalence)
Next sync action:             Verify body sections → update Last-Synced to "full" in canonical
Who triggers:                 Author of next DagEdit or VCA session that reviews body.
```

**Rule**: Do not edit `docs/INTEGRATION_CONTRACT.md` directly to propose changes.
Use this execution document's §10 "Next Single Small Diff" to record a proposal.
Only move the proposal to the canonical contract after explicit approval.
