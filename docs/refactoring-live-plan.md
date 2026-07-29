# DownKyi Core Live Refactoring Plan

Status: active
Last updated: 2026-07-29
Current group: Gate 10 integrate main and release v1.1.0
Current branch: `release/v1.1.0-integration`

This file contains only unfinished or not-yet-integrated work. Completed Gate
1-9 detail is retained in `docs/maintenance.md`, design documents and Git
history, not repeated here.

## Current State

- Gate 1-9 are integrated into
  `origin/refactor/pr-30-32-release-hardening` at merge commit `182745e`.
- PR #111 closed the final oversized production owner. Its final head passed
  Windows/Linux/macOS quality run `30424802172` and CodeQL run `30424802168`;
  all seven checks had zero annotations and each platform retained seven
  assembly-named TRX artifacts.
- Latest `main` and the full stacked branch merge cleanly. Integration commit
  `fb8c922` has parents `661a7c4` and `182745e`.
- The pre-version integration commit passes strict `AnalysisMode=All` Release
  build with zero warnings/errors and all 713 tests across seven projects.
- `version.txt` is now the only `1.1.0` source. Generated DownKyi metadata is
  Assembly/File `1.1.0.0` and informational `1.1.0+<commit>`.
- The local version candidate passes strict build with zero warnings/errors,
  all 714 tests, format, module audit, vulnerable/deprecated package audits,
  `git diff --check`, and Gitleaks across 986 candidate files.
- The final candidate authenticated read-only audit passed its navigation gate
  and all 14 allowlisted contracts with HTTP 200, Bilibili code 0, required
  fields present and zero contract drift. Gitleaks 8.30.1 then reported zero
  findings across all 986 candidate files.
- Final remote and package validation is not complete, so no tag may be
  created.

## Gate 10 Checklist

- [x] Create the integration branch from latest `origin/main`.
- [x] Merge the complete release-hardening stack without conflicts.
- [x] Pass strict build and all tests before changing the version.
- [x] Verify version-derived assembly, file and informational metadata for
      `1.1.0`; UI and package validation remains part of the publish rehearsal.
- [x] Repeat the sanitized authenticated read-only Bilibili contract audit on
      the final candidate and run Gitleaks.
- [ ] Pass strict PR CI and CodeQL on Windows, Linux and macOS for the final
      candidate SHA.
- [ ] Manually dispatch `.github/workflows/build.yml` on that SHA.
- [ ] Require Windows x64/x86, Linux x64/arm64 and macOS x64/arm64 package jobs
      plus their release-gate jobs to pass.
- [ ] Inspect every package, `.sha256` sidecar and publish manifest; confirm
      DownKyi, aria2, FFmpeg, ffprobe, Fluent theme and version.
- [x] Confirm settings JSON, legacy SQLite, unfinished tasks, GID, partial file
      map, completed segment keys and resume fixtures remain green.
- [x] Confirm the source candidate contains no credential or account data.
      Package-specific Config, Logs, Cache, Storage and user-data inspection
      remains part of the cross-platform publish rehearsal.
- [ ] Merge the integration PR into `main`.
- [ ] Verify clean `main` points at the tested integration tree.
- [ ] Create tag `v1.1.0` once, wait for the tag workflow, inspect its release
      artifacts and checksums, then publish curated release notes.

## Stable Contracts

- Do not change settings JSON names, SQLite schema semantics, task IDs, GIDs,
  partial-file maps or resume identity while preparing the release.
- Do not reintroduce Prism, DryIoc, EventAggregator, RegionManager,
  ContainerLocator, string ViewName navigation, static HTTP state or another
  logging sink.
- `version.txt` remains the only version source; project files cannot define a
  second `Version`, `VersionPrefix`, `AssemblyVersion`, `FileVersion` or
  `InformationalVersion`.
- Manual workflow dispatch may build artifacts but cannot publish a GitHub
  Release. Only the tested `v1.1.0` tag may publish.
- Release artifacts must not contain developer credentials or application
  data.

## Verification

Run sequentially in one worktree:

```powershell
dotnet restore ./DownKyi.sln
dotnet build ./DownKyi.sln -c Release --no-restore --no-incremental `
  -p:EnableNETAnalyzers=true -p:AnalysisMode=All `
  -p:EnforceCodeStyleInBuild=true -p:TreatWarningsAsErrors=true `
  -p:CodeAnalysisTreatWarningsAsErrors=true
pwsh ./script/test-solution.ps1 -Configuration Release -NoRestore -NoBuild
dotnet format ./DownKyi.sln --verify-no-changes --no-restore
pwsh ./script/audit-module-boundaries.ps1
dotnet package list --project ./DownKyi.sln --vulnerable --include-transitive
dotnet package list --project ./DownKyi.sln --deprecated --include-transitive
pwsh ./script/scan-secrets.ps1
git diff --check
```

## Completion

Gate 10 is complete only when the tested tree is merged into `main`, tag
`v1.1.0` points to that release commit, every release package is validated, the
GitHub Release is published with checksums and curated notes, and this file is
updated or removed from the next development branch.

## Rollback

- Before merge: close the integration PR and leave `main` unchanged.
- After merge but before tag: revert the merge commit; do not create the tag.
- After publication: never move or reuse `v1.1.0`. Withdraw invalid artifacts,
  document the reason and publish a corrective version.
