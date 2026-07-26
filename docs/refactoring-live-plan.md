# DownKyi Core Live Refactoring Plan

Status: active
Last updated: 2026-07-26
Current group: Gate 5 Event-driven download queue
Current branch: `refactor/event-driven-download-queue`

This file contains only unfinished or not-yet-integrated work. Completed PR 02-32 items are not restored. Design rationale belongs in `design-docs`; product acceptance belongs in `product-specs`.

## State Correction

The previous `Status: complete` was incorrect.

- `origin/refactor/pr-30-32-release-hardening` is not an ancestor of `origin/main`.
- PR #78 was merged into the stacked base `refactor/pr-25-29-remove-legacy`, not into `main`.
- PR #75 and PR #77 are closed after their replacement was validated in PR #82.
- PR #79 and PR #80 were superseded by green PR #83, closed, and their typed replacement was merged into the stacked release-hardening base.
- Gate 4 passed Windows/Linux/macOS quality CI and CodeQL, then PR #87 was merged into `refactor/pr-30-32-release-hardening` as merge commit `d8342abc`.
- The explicit authenticated read-only Bilibili audit passed its `/nav` login gate and all 14 contract probes. Only the allowlisted diagnostics artifact is retained; strict build, architecture tests, format verification and a zero-finding candidate secret scan are green, but the audit changes are not integrated yet.
- `version.txt` remains `1.0.32`; v1.1.0 has not passed its release gate.

No release tag may be created while any release blocker below remains.

## Execution Order

### Gate 5: Replace UI Polling With Event-Driven Enqueue

Owner branch: `refactor/event-driven-download-queue` after Gate 4.

Progress (2026-07-26):

- New, resumed, and persisted startup tasks directly enqueue `DownloadTaskId`; the bounded channel and fixed workers no longer receive or scan `DownloadingItem`.
- The one startup query returns Domain snapshots and UI projections together. Interrupted `Downloading`/`Pausing` snapshots recover to `Queued` before admission.
- Every executing task owns a linked cancellation source; deleting one active task does not stop its worker or unrelated tasks.
- Pre-start admissions are deduplicated in `DownloadTaskQueueGateway` and flushed when the runtime attaches. A closing runtime returns admissions to that pending owner instead of dropping committed work.
- `DispatchAsync`, the 500 ms delay, `_queuedDownloads`, and UI collection membership checks are removed.
- Strict Release build is at zero warnings; all 565 solution tests, format verification, `git diff --check`, module-boundary audit, vulnerable/deprecated package audits, and the candidate secret scan are green. Commit/PR publication and remote matrix validation remain.

Scope:

- Add `EnqueueAsync(DownloadTaskId, CancellationToken)`.
- Restore queued/interrupted task IDs from SQLite once during startup.
- Give each active task an explicit cancellation owner.
- Remove `DispatchAsync`, 500 ms polling, `_queuedDownloads` and collection-membership validity checks.

Verification:

- enqueue latency tests use deterministic clock/channel controls.
- 1/4/8 worker tests prove no duplicate execution.
- shutdown cancellation restores resumable state.

Completion:

- runtime never scans an `ObservableCollection` for work.

Rollback:

- Revert queue wiring only after confirming stored queued tasks are still discoverable on next launch.

### Gate 6: Split DownloadPipeline And Centralize Retry

Owner branches: one stage extraction PR, followed by one retry-policy PR.

Scope:

- Introduce `DownloadExecutionContext` and typed stage results.
- Extract resolve, media transfer, artifacts, mux, validate and finalize stages.
- Move localized UI text to Desktop presenter.
- Establish one retry budget owner with typed decisions for timeout/5xx, 429, expired URL, invalid media, disk error and cancellation.

Verification:

- each stage has deterministic unit tests.
- fake HTTP tests cover interrupted, empty, wrong length, 403, 429, 500 and slow responses.
- retry-count tests prove no multiplicative pipeline x backend attempts.

Completion:

- pipeline only orders stages.
- no stage references `DictionaryResource`, UI collection or ViewModel types.

Rollback:

- Each extracted stage is one commit and can be reverted without changing stored task format.

### Gate 7: Finish HTTP And Infrastructure Ownership

Owner branch: `refactor/async-bilibili-infrastructure`.

Scope:

- Define injected `IBilibiliApiClient`, `IBuvidProvider` and existing `IWbiKeyProvider` ports.
- Move implementation to Infrastructure.
- Use `SendAsync`, async content/stream reads and `Task.Delay`.
- Remove static `WebClient` state and `Configure()`.
- Move aria2, FFmpeg, file system and logging sink configuration toward Infrastructure in test-protected steps.

Verification:

- cancellation during request/backoff propagates immediately.
- retry exhaustion preserves typed HTTP/API error.
- all Bilibili fixtures remain green.
- architecture tests reject static client facades and sync network IO.

Completion:

- Application and Desktop depend on ports, not Core static facades.

Rollback:

- Keep endpoint adapters behavior-compatible until all callers migrate; remove facade only in the final commit.

### Gate 8: Complete Desktop Boundary And UI Projection Ownership

Owner branch: `refactor/desktop-boundary` after runtime ports stabilize.

Scope:

- Move Views, ViewModels, UI projections, navigation/dialog adapters, dispatcher and lifecycle to `DownKyi.Desktop`.
- Keep executable as minimal startup/composition.
- Replace `ImmutableObservableCollection<T>` with owner-only `ObservableCollection<T>` exposed as `ReadOnlyObservableCollection<T>`.
- Move QR rendering and Core XAML resources to Desktop.
- Replace presentation-bound service contracts with Application DTO/ports.

Verification:

- Host smoke resolves full XAML and key ViewModels from the new Desktop assembly.
- collection contract and UI-thread tests pass.
- Core has no Avalonia dependency or `.axaml` resource.
- Application/service interfaces have no `DownKyi.ViewModels` types.

Completion:

- `DownKyi.Desktop` is the actual Desktop owner described in `ARCHITECTURE.md`.
- executable contains no runtime service, ViewModel or platform adapter implementation.

Rollback:

- Move by responsibility slice with rename maps; revert a slice as one commit if XAML/resource/DI smoke fails.

### Gate 9: Logging, Naming And Large-Owner Convergence

Owner branches: separate ADR/implementation, naming, and large-owner PRs.

Scope:

- Decide logging sink ownership through an ADR and benchmark before adding a dependency.
- Keep project-specific redaction before every persistence/cache/export path.
- Separate recent buffer, diagnostic exporter and retention responsibilities.
- Rename `Languanges`, QR/FFmpeg casing, duplicate SeasonsSeries owners and proven generic buckets in isolated rename PRs.
- Split hand-written oversized owners by responsibility; do not split generated/protocol files only to satisfy LOC.

Verification:

- redaction, flush, rotation, retention and shutdown tests remain green.
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
