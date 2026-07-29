# DownKyi Core Live Refactoring Plan

Status: active
Last updated: 2026-07-29
Current group: Gate 10 Windows lifecycle blocker and corrective release
Current branch: `fix/windows-test-host-shutdown`

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
- The latest committed candidate authenticated read-only audit passed its navigation gate
  and all 14 allowlisted contracts with HTTP 200, Bilibili code 0, required
  fields present and zero contract drift at `8aa4382`. Gitleaks 8.30.1 then
  reported zero findings across all 986 candidate files.
- PR #112 first-head quality run `30426137294`, protobuf run `30426137279`
  and CodeQL run `30426137276` passed. Each platform uploaded seven distinct
  TRX files; 713 tests executed successfully and the FFmpeg-dependent seek
  integration was the only non-executed CI case.
- Manual package rehearsal `30426554087` then exposed two release-only
  defects: expired BtbN autobuild URLs on Windows x64/Linux and host-derived
  `RuntimeIdentifier` contamination during x64 publication on arm64 macOS.
  Candidate `8aa4382` pins existing immutable FFmpeg releases, resolves all
  asset scripts from their own directory, uses one manifest on every OS, and
  separates asset selection from the SDK runtime identifier.
- Second rehearsal `30428876552` proved those two defects fixed: all release
  gates passed, along with macOS arm64, Windows x64 and every Linux x64
  package. macOS x64, Windows x86 and Linux arm64 then exposed one remaining
  contract: a target asset RID was not propagated through project references,
  so Core selected host binaries. The executable is now the sole asset-RID and
  package-content owner and directly includes the selected Core catalog files;
  no custom RID crosses a project-reference boundary. A clean Windows x86
  self-contained publish passes the common validator, and its aria2, FFmpeg
  and ffprobe SHA-256 values exactly match the x86 source assets. Full local
  validation passes with zero strict build warnings/errors, all 719 tests,
  format, module boundary, dependency, secret and diff gates. A project
  reference property-propagation attempt was rejected because a solution build
  created competing project instances that wrote the same `obj/bin` paths.
  PR quality `30430722500`, protobuf `30430722468` and CodeQL `30430722421`
  pass on `1968c9d`; each platform retains seven distinct TRX files, 718
  tests execute and pass, and only the runner-dependent FFmpeg seek test is
  not executed. Code and test annotations are zero; the one CodeQL annotation
  is GitHub's documented 300-file diff-display limit.
- Third rehearsal `30431043860` passes every release gate and all nine package
  jobs. Manual dispatch correctly skips release publication. All nine package
  sidecars match their package SHA-256; the nine manifests contain 54 valid
  required-file entries with version/RID agreement. Direct archive inspection
  passes for both Windows zips, both debs, the rpm, both AppImages and both
  DMGs, with required DownKyi/aria2/FFmpeg/ffprobe files and zero Config, Logs,
  Cache, Storage, Cookie, database or other user-data paths. Same-RID package
  manifests agree, and remote Windows asset hashes match the repository
  catalog.
- PR #112 is merged into `main` at `fcc1d226`; its tree exactly matches the
  tested integration head. Annotated tag `v1.1.0` points to that immutable
  commit.
- Tag workflow `30434000154` attempt 1 exposed a Windows lifecycle race before
  Desktop test discovery. The xUnit `-assemblyInfo` child printed valid JSON
  and then `Waiting 10 seconds for foreground threads to exit`, corrupting the
  runner protocol. Attempt 2 passed, but a successful rerun is not accepted as
  evidence that the foreground-thread owner has been fixed.
- The release produced by attempt 2 was immediately returned to draft. Package
  publication is frozen until the thread owner, test-host teardown and
  production Host shutdown path are verified and corrected.
- Local unmodified baselines currently pass 200 sequential and 400 concurrent
  `-assemblyInfo` process runs plus 50 full Desktop test runs. This establishes
  that the failure is intermittent; it does not close the blocker.
- The foreground output owner is now identified: the old test-data module
  initializer registered synchronous recursive cleanup on `ProcessExit`, and
  the xUnit v3 watchdog emitted its foreground-thread warning after valid
  `-assemblyInfo` JSON. Test isolation is now an async assembly fixture.
- Desktop tests use Avalonia's assembly-scoped headless xUnit session instead
  of a custom foreground Dispatcher thread. Production desktop shutdown awaits
  App, Host and runtime disposal; the production Host smoke also exposed and
  fixed a pooled SQLite connection surviving store disposal.
- The Assembly Lifecycle Stability Gate now probes load, assembly info,
  discovery, execution, assembly teardown and process exit in independent
  children. Its ownership audit covers production/test/benchmark/tool owners,
  and Windows delay forensics captures process/thread state and managed stacks.
- Formal local Verification passes all seven test assemblies for five
  iterations: 211 phase results, zero failures, zero unknown owners, teardown
  at no more than 65 ms and process exit at no more than 170 ms on this Windows x64
  worktree. The report is deliberately marked dirty and is not release evidence.

## Gate 10 Checklist

- [x] Create the integration branch from latest `origin/main`.
- [x] Merge the complete release-hardening stack without conflicts.
- [x] Pass strict build and all tests before changing the version.
- [x] Verify version-derived assembly, file and informational metadata for
      `1.1.0`; UI and package validation remains part of the publish rehearsal.
- [x] Repeat the sanitized authenticated read-only Bilibili contract audit on
      the final candidate and run Gitleaks.
- [x] Pass strict PR CI and CodeQL on Windows, Linux and macOS for the final
      candidate SHA.
- [x] Manually dispatch `.github/workflows/build.yml` on that SHA.
- [x] Require Windows x64/x86, Linux x64/arm64 and macOS x64/arm64 package jobs
      plus their release-gate jobs to pass.
- [x] Inspect every package, `.sha256` sidecar and publish manifest; confirm
      DownKyi, aria2, FFmpeg, ffprobe, Fluent theme and version.
- [x] Confirm settings JSON, legacy SQLite, unfinished tasks, GID, partial file
      map, completed segment keys and resume fixtures remain green.
- [x] Confirm the source candidate and inspected packages contain no
      credential, account data, Config, Logs, Cache, Storage or user database.
- [x] Merge the integration PR into `main`.
- [x] Verify clean `main` points at the tested integration tree.
- [x] Create immutable annotated tag `v1.1.0` and run the tag workflow.
- [x] Identify the owner of the intermittent Windows foreground thread; record
      whether it belongs to xUnit discovery, the Avalonia test Dispatcher, a
      hosted service or production application lifecycle.
- [x] Replace the custom headless Avalonia foreground thread with the official
      assembly-scoped headless test session and deterministic async teardown.
- [x] Exercise production-style Host startup, shutdown and desktop-lifetime
      exit with bounded completion assertions.
- [x] Add the ownership audit, six-phase Assembly Lifecycle Stability Gate,
      automatic Windows timeout forensics and blocking PR/main/rehearsal jobs.
- [x] Pass the formal local five-iteration lifecycle Verification with managed
      stack forensics validated and retain its machine-readable report.
- [ ] Repeat Windows assembly-info and Desktop/full-solution tests enough to
      make the original race observable if it remains.
- [ ] Pass a new strict PR quality run and a complete manual release rehearsal
      on the corrected commit.
- [ ] Keep `v1.1.0` immutable. Document the withdrawn draft and publish a
      corrective version only after every lifecycle and release gate is green.

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
pwsh ./script/audit-lifecycle-ownership.ps1 `
  -OutputDirectory ./artifacts/assembly-lifecycle/ownership
pwsh ./script/test-assembly-lifecycle.ps1 `
  -Configuration Release `
  -Iterations 5 `
  -NoBuild `
  -ValidateForensics `
  -ResultsDirectory ./artifacts/assembly-lifecycle/verification
dotnet format ./DownKyi.sln --verify-no-changes --no-restore
pwsh ./script/audit-module-boundaries.ps1
dotnet package list --project ./DownKyi.sln --vulnerable --include-transitive
dotnet package list --project ./DownKyi.sln --deprecated --include-transitive
pwsh ./script/scan-secrets.ps1
git diff --check
```

## Completion

Gate 10 is complete only when the Windows foreground-thread owner is identified,
the relevant test and production teardown paths are deterministic, the formal
five-iteration Verification report passes, the release `Rehearsal` profile
runs at least 50 iterations per assembly with diagnostics enabled, a complete
rehearsal passes on the corrected commit, and the corrective GitHub Release is
published with validated artifacts and notes. A successful blind rerun is not
completion evidence.

## Rollback

- Before merge: close the integration PR and leave `main` unchanged.
- After merge but before tag: revert the merge commit; do not create the tag.
- After publication: never move or reuse `v1.1.0`. Withdraw invalid artifacts,
  document the reason and publish a corrective version.
