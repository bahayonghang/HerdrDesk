# Frontend Development Guidelines

**Deferred WinUI/React templates.** `src/HerdDesk.App` is a BCL composition-root host plus HD-011 L1 ViewModels. There is no WinUI product window, no React app, and no WebView2 renderer host. `App.xaml` / visual Shell remain not admitted.

The Markdown files in this folder are Trellis init templates (component / hook / React-style type safety). They are **not** live implementation contracts. Do not write React, Next.js, Vue, or generic CSS from those templates. Do not treat hook-guidelines or state-management as if a UI exists.

Shared facts: [AGENTS.md](../../../AGENTS.md). Planned UI names (workspace / session, renderer) are in [CLAUDE.md](../../../CLAUDE.md) and `docs/plan/`. Current compiled types for a future UI to consume are in `src/HerdDesk.Contracts`.

---

## Overview

Product UI is planned as WinUI on Windows. That work is not started. Renderer choice is recorded in `docs/spikes/renderer-decision.md`: WebView2/xterm remains the delivery baseline; native is `UNVERIFIED` and not promoted. Filling this index as deferred does **not** complete `.trellis/tasks/00-bootstrap-guidelines`. Remaining frontend guideline files stay placeholders until a UI task is approved.

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | Trellis React-style template | Deferred / N/A — not a live contract |
| [Component Guidelines](./component-guidelines.md) | Trellis component template | Deferred / N/A — not a live contract |
| [Hook Guidelines](./hook-guidelines.md) | Trellis hook template | Deferred / N/A — not a live contract |
| [State Management](./state-management.md) | Trellis state template | Deferred / N/A — not a live contract |
| [Quality Guidelines](./quality-guidelines.md) | Trellis frontend quality template | Deferred / N/A — not a live contract |
| [Type Safety](./type-safety.md) | Trellis TypeScript template | Deferred / N/A — not a live contract |

Do not implement features by “filling” those templates as if they described HerdDesk.

---

## Pre-Development Checklist

- [ ] Confirm an approved task actually asks for UI. HD-011 shipped BCL ViewModels only. Do not fill `App.xaml`. Do not add WinUI PackageReference until the package is admitted.
- [ ] Read [AGENTS.md](../../../AGENTS.md): no live WinUI writes in the G0 gate.
- [ ] Use Contracts types (`PaneKey`, `ConnectionEpoch`, `TerminalAccess`, `ControlVerified`) when UI work starts. Do not invent a second identity model.

---

## Quality Check

- [ ] No React/TypeScript UI was added from these templates during G0 harness or protocol tasks.
- [ ] No WinUI XAML or admitted WASDK PackageReference was added by HD-011 L1 ViewModels.
- [ ] No claim that frontend guidelines are complete because this index says deferred.
- [ ] Bootstrap task `00-bootstrap-guidelines` remains `in_progress` until remaining frontend files are filled from a real UI, or a later owner changes that task.

**Language**: English.
