# Assembly Lifecycle Stability Gate

Status: required quality and release gate

## Purpose

A green test summary does not prove that a test executable loaded cleanly,
preserved its runner protocol, disposed assembly fixtures, stopped foreground
threads or exited deterministically. This gate treats each xUnit test assembly
as a real process and measures all lifecycle boundaries separately.

The first incident covered by this policy occurred when
`TestDataIsolation.ProcessExit` ran synchronous recursive cleanup after xUnit
had returned from its runner. xUnit's foreground-thread watchdog then wrote
`Waiting 10 seconds for foreground threads to exit` after the valid
`-assemblyInfo` JSON. The process returned exit code 0, but the Visual Studio
adapter could no longer parse stdout as one JSON object.

The permanent correction is:

- test data isolation is an xUnit assembly fixture implementing
  `IAsyncDisposable`;
- fixture cleanup emits a private lifecycle marker and does not register
  `ModuleInitializer` or `ProcessExit`;
- Desktop tests use `Avalonia.Headless.XUnit` per-assembly isolation instead of
  a custom process-lifetime dispatcher thread;
- `DesktopApplication.RunAsync` awaits `App.DisposeAsync` after the Avalonia
  main loop, and application disposal requests Host shutdown before releasing
  resources;
- `SqliteDownloadTaskStore.Dispose` clears only its owned connection pool;
- the system benchmark dispatcher uses a bounded join.

## Dynamic Phases

`script/test-assembly-lifecycle.ps1` discovers every `*.Tests.csproj` and runs
each phase in an independent child process:

1. `load`: a collectible `AssemblyLoadContext` loads the assembly, runs its
   module constructor, unloads it and proves the context is no longer rooted.
2. `assembly-info`: the xUnit executable runs `-assemblyInfo`; stdout must be
   exactly one JSON object.
3. `discovery`: the xUnit executable lists tests in automated mode; stdout must
   be exactly one JSON array.
4. `execution`: the xUnit executable runs tests without inter-assembly process
   reuse; every non-empty stdout line must be valid JSON.
5. `assembly-teardown`: the xUnit assembly fixture must emit
   `started -> disposing -> disposed`, and its process-specific data root must
   be absent.
6. `process-exit`: the process must exit within the configured post-teardown
   deadline without residual children or runner-protocol pollution.

Every phase records its exit code, duration, timeout state, stdout/stderr
protocol state, residual child count and evidence paths. The report aggregates
P50, P95, P99 and maximum duration per assembly and phase.

## Static Ownership

`script/audit-lifecycle-ownership.ps1` scans production, test, benchmark and
tool sources for:

- module initializers and process-exit handlers;
- static constructors and static field initialization;
- explicit threads, `Task.Run`, dispatchers and timers;
- global event registration;
- Generic Host startup and shutdown;
- synchronous waits, thread joins and synchronous file cleanup.

`docs/testing/assembly-lifecycle-owners.json` is the machine-readable ownership
policy. Each path must identify who starts the work, who stops it, and how
teardown performs cancellation, wake-up, wait and bounded join. An unowned or
unapproved mechanism fails the gate. The inventory is evidence, not a broad
suppression list.

## Profiles

| Profile | Iterations per assembly | Required use |
| --- | ---: | --- |
| `Local` | 1 | Script development and focused validation |
| `PR` | 3 | Every pull request on Windows |
| `Main` | 50 | Every push to `main` |
| `Rehearsal` | 100 | Release rehearsal and tag release gate |
| `Flaky` | 500 | Focused investigation; override up to 10000 |

Formal local Verification overrides the profile with `-Iterations 5` and runs
`-ValidateForensics`. Release evidence must run at least 50 iterations per
assembly; the repository's `Rehearsal` profile deliberately runs 100.

Use `-AssemblyPattern` to isolate one or more suspect assemblies without
weakening the normal PR or release profiles:

```powershell
pwsh ./script/test-assembly-lifecycle.ps1 `
  -Configuration Release `
  -Profile Flaky `
  -AssemblyPattern DownKyi.Desktop.Tests `
  -NoBuild `
  -ValidateForensics
```

## Timeout Forensics

On Windows, a slow phase or slow post-teardown exit automatically captures:

- managed process/thread IDs;
- `ThreadState`, wait reason and processor time;
- a sanitized process tree containing PID, parent PID and process name;
- `dotnet-stack report --process-id` output when the tool is available.

CI installs the pinned Microsoft `dotnet-stack` tool and runs
`-ValidateForensics`. That self-test deliberately holds a probe child process
after assembly unload and fails unless evidence and a managed stack are
actually produced. Timeout evidence is saved before the process tree is
terminated.

Raw stdout/stderr, JSON evidence, the machine report and Markdown summary are
written below `artifacts/assembly-lifecycle/<run-id>/`. CI uploads the entire
directory even when the gate fails.

## Comparing Results

Do not compare timing numbers collected on different machines. Every report
records:

- .NET runtime version;
- operating system and architecture;
- Commit SHA and whether the worktree was dirty;
- profile and iteration count;
- timeout and exit thresholds.

Only compare reports with compatible runner metadata, datasets and test
configuration. A faster rerun does not close a lifecycle failure; the owner and
teardown path must be identified and corrected.

## Completion And Rollback

A lifecycle fix is complete only when the relevant owner has a deterministic
teardown test, the focused flaky profile is stable, normal solution tests pass,
and PR plus release gates produce clean reports.

To roll back a gate change, revert the probe, scripts, policy, tests, docs and
workflow wiring together. Never retain workflow references to a removed
measurement phase, and never replace a failed lifecycle gate with a blind
rerun.
