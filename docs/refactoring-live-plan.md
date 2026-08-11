# DownKyi Live Plan

Status: active
Last updated: 2026-08-11
Current work item: v1.1.1 item 3, Bangumi episode identity and playback contract
Current working branch: `fix/bangumi-ep-id-contract`
Current base: `origin/main` at `6e1e1d79b9aa10c06f534a94a845b4707d9766ba`

This file contains only unfinished, blocked or integration-pending work. Accepted
design and completed history belong in `ARCHITECTURE.md`, `docs/design-docs/`,
`docs/maintenance.md` and release notes. Completed v1.1.1 item 1 evidence remains
in `docs/exec-plans/v1.1.1-security-patch.md`.

## Current Item

### v1.1.1 Item 3: Bangumi Episode Identity And Playback Contract

- [x] Require a positive `ep_id` before the Bangumi v2 playback request and
      preserve it through page parsing and persisted download playback.
- [x] Keep DURL and DASH as alternative valid formats while rejecting missing
      envelopes, explicit null playback fields and all-empty playback results
      with typed diagnostics.
- [x] Cover the actual request URI, invalid-ID no-request behavior, malformed
      response matrix and both production callers with deterministic tests.
- [x] Synchronize the Bilibili API audit and AI knowledge graph.
- [x] Pass strict Release, invariant corpus, all seven test projects,
      architecture/lifecycle, format, module, workflow, package, secret and diff
      gates locally.
- [ ] Push one exact head, open the current-architecture replacement for stale
      PR #85, and obtain all required remote checks. Close #85 only after the
      replacement is merged.

## Next Items

These remain deliberately unstarted until the current item is integrated:

1. Remaining v1.1.1 P1, merge-blocking or core runtime review debt, including
   the open dialog/output-path/mux/source-transition stack after its dependency
   graph is reconciled with current `main`.
   This includes the pre-existing `DownKyi.Tests` SQLite pool-disposal race
   observed once during item 3 verification; an isolated rerun and the next full
   run passed, which does not prove that separate lifecycle root cause fixed.
2. Final release rehearsal, package validation and v1.1.1 publication.

## Deferred After v1.1.1

Desktop feature-locality work is accepted but must not enter the v1.1.1 product
scope. Its design is `docs/design-docs/desktop-feature-locality.md`; baseline,
unknowns, acceptance, rollback and the mandatory PR dependency are in
`docs/exec-plans/desktop-feature-locality.md`.

```text
PR A: route / manifest completeness gate
  -> PR B: simple Shell descriptor migration
  -> PR C: stateful Shell migration
  -> PR D: proven legacy cleanup + architecture ratchet
```

Each item must use a separate branch and PR. Do not merge, rebase or copy legacy
architecture wholesale; migrate only valid behavior into current DI, typed
navigation and coordinator boundaries.

## Release Blockers

- All four ordered v1.1.1 items must be complete and integrated into current
  `main`.
- The exact final commit must pass strict quality, CodeQL, Main lifecycle 50,
  complete release rehearsal and Windows/Linux/macOS package validation.
- Settings JSON, legacy SQLite, unfinished tasks, GID, partial-file maps,
  completed keys and resume fixtures must remain compatible.
- The source tree and packages must remain free of Cookie, account data, local
  Config/Logs/Cache/Storage and developer artifacts.
- `v1.1.0` stays immutable. Do not change `version.txt`, tag or publish v1.1.1
  until every blocker is green.

## Verification

Run sequentially in one worktree:

```powershell
dotnet restore ./DownKyi.sln
pwsh ./script/validate-release-version.ps1
dotnet build ./DownKyi.sln -c Release --no-restore --no-incremental `
  -p:EnableNETAnalyzers=true -p:AnalysisMode=All `
  -p:EnforceCodeStyleInBuild=true -p:TreatWarningsAsErrors=true `
  -p:CodeAnalysisTreatWarningsAsErrors=true -p:UseSharedCompilation=false
pwsh ./script/test-review-invariants.ps1 `
  -Configuration Release -NoRestore -NoBuild
pwsh ./script/test-solution.ps1 -Configuration Release -NoRestore -NoBuild
pwsh ./script/audit-lifecycle-ownership.ps1 `
  -OutputDirectory ./artifacts/assembly-lifecycle/ownership
pwsh ./script/test-assembly-lifecycle.ps1 `
  -Configuration Release -Iterations 5 -NoBuild -ValidateForensics `
  -ResultsDirectory ./artifacts/assembly-lifecycle/verification
dotnet format ./DownKyi.sln --verify-no-changes --no-restore
pwsh ./script/audit-module-boundaries.ps1 `
  -OutputPath ./artifacts/architecture/module-boundary-audit.json
$workflowFiles = Get-ChildItem ./.github/workflows -Filter *.yml | `
  Select-Object -ExpandProperty FullName
go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12 -- $workflowFiles
dotnet package list --project ./DownKyi.sln --vulnerable --include-transitive
dotnet package list --project ./DownKyi.sln --deprecated --include-transitive
pwsh ./script/scan-secrets.ps1
git diff --check
```

A result is valid only when its runtime, OS, architecture, commit SHA and
dirty-worktree state are recorded. Cross-machine timings are not compared
directly.

## Completion And Rollback

An item is complete only after implementation, tests, documentation, exact-head
CI and review are green. Remove it from this file after merge and promote its
stable facts to architecture/maintenance documentation. Before merge, close its
Draft PR. After merge, revert the entire item commit range without changing user
data formats or reintroducing a security bypass.

Gate 10 is complete only when its formal local lifecycle verification, exact
Main profile and release rehearsal remain green; item 1 must preserve those
already-established lifecycle conditions rather than replacing them.
