# DownKyi Core Live Refactoring Plan

Status: active
Last updated: 2026-07-28
Current group: Gate 9 naming and large-owner convergence
Current branch: `refactor/naming-boundaries`

This file contains only unfinished or not-yet-integrated work. Completed PR 02-32 items are not restored. Design rationale belongs in `design-docs`; product acceptance belongs in `product-specs`.

## State Correction

The previous `Status: complete` was incorrect.

- `origin/refactor/pr-30-32-release-hardening` is not an ancestor of `origin/main`.
- PR #78 was merged into the stacked base `refactor/pr-25-29-remove-legacy`, not into `main`.
- PR #75 and PR #77 are closed after their replacement was validated in PR #82.
- PR #79 and PR #80 were superseded by green PR #83, closed, and their typed replacement was merged into the stacked release-hardening base.
- Gate 4 passed Windows/Linux/macOS quality CI and CodeQL, then PR #87 was merged into `refactor/pr-30-32-release-hardening` as merge commit `d8342abc`.
- Gate 5 and the authenticated read-only Bilibili audit passed Windows/Linux/macOS quality CI and CodeQL, then PR #88 was merged into `refactor/pr-30-32-release-hardening` as merge commit `fadd7eb3`.
- The authenticated audit was repeated on 2026-07-28: its `/nav` login gate and all 14 contract probes passed with zero drift. Only the allowlisted sanitized diagnostics artifact is retained; Gitleaks scanned 934 candidate files and reported zero findings.
- Gate 6 stage extraction passed two complete Windows/Linux/macOS quality and CodeQL rounds, then PR #89 was merged into `refactor/pr-30-32-release-hardening` as merge commit `e288913f`.
- Gate 6 retry policy passed three complete remote rounds. Final Windows/Linux/macOS quality run `30187455431` and CodeQL run `30187455441` had zero check annotations, then PR #90 was merged into `refactor/pr-30-32-release-hardening` as merge commit `ba0a928e`.
- Gate 7 async Bilibili Infrastructure ownership passed Windows/Linux/macOS quality run `30189537538`, protobuf run `30189537553`, and CodeQL run `30189537541`, then PR #91 was merged into `refactor/pr-30-32-release-hardening` as merge commit `55070903`.
- Gate 8 passed Windows/Linux/macOS quality run `30191251004`, protobuf run `30191250997`, and CodeQL run `30191250992`; PR #92 was merged into `refactor/pr-30-32-release-hardening` as `f8e78c9a`. CodeQL reported no alert, but GitHub emitted one platform annotation because the single required ownership PR changed 396 files and its diff API is capped at 300 files.
- Gate 9 logging ownership passed Windows/Linux/macOS quality run `30345373830`, protobuf run `30345371405`, and CodeQL run `30345371181`, with zero check annotations. PR #94 was merged into `refactor/pr-30-32-release-hardening` as merge commit `b290b204`.
- `version.txt` remains `1.0.32`; v1.1.0 has not passed its release gate.

No release tag may be created while any release blocker below remains.

## Execution Order

### Gate 9: Naming And Large-Owner Convergence

Owner branches: separate naming and large-owner PRs.

Scope:

- Rename `Languanges`, QR/FFmpeg casing, duplicate SeasonsSeries owners and proven generic buckets in isolated rename PRs.
- Split hand-written oversized owners by responsibility; do not split generated/protocol files only to satisfy LOC.

Naming implementation progress, pending PR integration:

- Canonical resource/runtime casing is `Languages` and `DownKyi.Core.FFmpeg`; architecture tests reject the historical spellings.
- Seasons/series detail and user-space list owners now have distinct View/ViewModel names. Application owns the sole `VideoInputResolver`; Desktop owns only `PlayStreamTypeResolver`.
- Generic type names fell from 5 to 0 and file/type mismatches from 4 to 0. JSON/XML DTOs were split into same-name files without changing CLR names or wire contracts.
- Duplicate simple-name groups fell from 9 to 4. The remaining complete sets are endpoint/role-scoped `BangumiType`, `FavoritesMedia`, `Subtitle`, and `VideoPage` contracts and cannot grow.
- Local gates: strict `AnalysisMode=All` Release build has zero warnings/errors; all 617 tests pass; format changed 0/805 files; vulnerable/deprecated package audits, `git diff --check`, 180 architecture tests, 13 Desktop smoke tests, and 931-candidate Gitleaks checks pass.

Verification:

- all XAML/resource URI and typed route smoke tests pass after rename.
- module-boundary ratchet entries decrease and no new entries are added.

Completion:

- remaining naming exceptions have a documented protocol/generated-code reason.
- knowledge graph and architecture docs match final ownership.

Rollback:

- Revert each rename or owner extraction as an atomic commit.

### Gate 10: Integrate Main And Release v1.1.0

Owner branch: release branch from latest `main` only after Gates 1-9.

Scope and acceptance are defined in `product-specs/v1.1.0-release-gate.md`.

Completion:

- all required branches are integrated into latest `main`.
- Windows/Linux/macOS package validation is green for the same SHA.
- user data and resume fixtures pass.
- `version.txt` is changed once to `1.1.0` and all version consumers derive from it.
- clean `main` is tagged `v1.1.0` and the GitHub Release is published with verified artifacts/checksums.

Rollback:

- Never retag a different commit. If the release is invalid, publish a corrective version and document artifact withdrawal.

## Every-PR Checklist

- Read `AGENTS.md`, `ARCHITECTURE.md`, knowledge graph and this plan.
- State goal, scope, stable contracts, tests, completion and rollback in the PR.
- Add a test that fails on the old behavior when behavior changes.
- Preserve settings, SQLite, unfinished tasks and resume state.
- Update knowledge graph and live plan when ownership or dependencies change.
- Run strict build, full tests, format, diff and package audits sequentially.
- Do not add broad suppressions, restore legacy composition, or hide failure with null/empty sentinels.
