# DownKyi Core Live Refactoring Plan

Status: active
Last updated: 2026-07-26
Current group: Gate 4 Domain download authority
Current branch: `refactor/domain-download-authority`

This file contains only unfinished or not-yet-integrated work. Completed PR 02-32 items are not restored. Design rationale belongs in `design-docs`; product acceptance belongs in `product-specs`.

## State Correction

The previous `Status: complete` was incorrect.

- `origin/refactor/pr-30-32-release-hardening` is not an ancestor of `origin/main`.
- PR #78 was merged into the stacked base `refactor/pr-25-29-remove-legacy`, not into `main`.
- PR #75 and PR #77 are closed after their replacement was validated in PR #82.
- PR #79 and PR #80 were superseded by green PR #83, closed, and their typed replacement was merged into the stacked release-hardening base.
- `version.txt` remains `1.0.32`; v1.1.0 has not passed its release gate.

No release tag may be created while any release blocker below remains.

## Execution Order

### Gate 4: Make Domain DownloadTask The Runtime Authority

Owner branch: `refactor/domain-download-authority`.

Progress (2026-07-26):

- Added `IDownloadTaskApplicationService` and `DownloadTaskApplicationService` as the authoritative typed command/query owner; commands load the aggregate, apply one legal transition, persist with optimistic versioning, then publish the committed snapshot.
- Worker and pipeline entry points now use `DownloadTaskId`; transfer requests carry Domain progress/GID callbacks and no longer carry `DownloadingItem` as task authority.
- Replaced normal UI-to-Domain reconstruction with one-way Domain-to-UI projection. `DownloadTask.Restore` is limited to the SQLite materializer and explicit NRBF migration mapper.
- Preserved unfinished task IDs, GID, transfer file map, completed keys, progress, output size and versions across reopen and shutdown recovery.
- Pause now remains `Pausing` until the transfer worker has actually stopped; built-in and aria2 pause outcomes preserve partial files before the worker confirms `Paused`.
- Active deletion is durable and retryable: a failed physical cleanup leaves `Canceled` state, while a later delete skips duplicate cancellation and can finish file/store removal.
- Projection changes notify all affected bindings. UI-thread dispatch of projection events remains explicit Gate 8 work; the current 500 ms UI-list queue scan remains explicit Gate 5 work.
- Local strict Release build is green with zero warnings, all 552 solution tests pass, format changed 0/750 files, `git diff --check` passes, the module-boundary audit completes, and vulnerable/deprecated package audits are empty.
- The .NET 10 dependency-audit command is verified with the required `dotnet package list --project <solution>` syntax; stale positional examples were removed from the Agent and rollback guides.
- Commit/PR publication and remote matrix validation remain. Gate 4 stays in this live plan until its integration evidence is available.

Scope:

- Commands address tasks by `DownloadTaskId`.
- Load aggregate, invoke legal transition, persist, then publish projection changes.
- Remove UI model -> Domain reconstruction as the normal write path.
- Preserve legacy SQLite/JSON readers only at migration adapters.
- Preserve unfinished tasks, GID, partial file map, completed segment keys and optimistic version.

Verification:

- state transition tests cover start, pause, resume, fail, complete, cancel and shutdown recovery.
- legacy database fixtures migrate and reopen without data loss.
- architecture tests reject `DomainDownloadTask.Restore` outside migration/store adapters.

Completion:

- worker/pipeline APIs no longer accept `DownloadingItem` as task authority.
- `CreateUnfinishedTask`, `ToLegacyStatus` and reverse Domain mapping are removed from runtime flow.

Rollback:

- Keep a feature-compatible adapter commit boundary so runtime authority can be reverted without reverting schema compatibility.

### Gate 5: Replace UI Polling With Event-Driven Enqueue

Owner branch: `refactor/event-driven-download-queue` after Gate 4.

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
