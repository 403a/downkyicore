[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("Local", "PR", "Main", "Rehearsal", "Flaky")]
    [string]$Profile = "Local",
    [ValidateRange(0, 10000)]
    [int]$Iterations = 0,
    [ValidateRange(1, 3600)]
    [int]$PhaseTimeoutSeconds = 180,
    [ValidateRange(0.1, 60)]
    [double]$SlowPhaseThresholdSeconds = 5,
    [ValidateRange(0.01, 60)]
    [double]$ExitThresholdSeconds = 1,
    [string[]]$AssemblyPattern = @("*"),
    [string]$ResultsDirectory = "artifacts/assembly-lifecycle",
    [string]$DiagnosticsToolPath,
    [switch]$ValidateForensics,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "DownKyi.sln"
$probeProject = Join-Path $repositoryRoot "tools/DownKyi.AssemblyLifecycleProbe/DownKyi.AssemblyLifecycleProbe.csproj"
$probeAssembly = Join-Path $repositoryRoot "tools/DownKyi.AssemblyLifecycleProbe/bin/$Configuration/net10.0/DownKyi.AssemblyLifecycleProbe.dll"
$profileIterations = @{
    Local = 1
    PR = 3
    Main = 50
    Rehearsal = 100
    Flaky = 500
}
$resolvedIterations = if ($Iterations -gt 0) {
    $Iterations
}
else {
    $profileIterations[$Profile]
}
$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$outputRoot = [System.IO.Path]::GetFullPath($ResultsDirectory, $repositoryRoot)
$runRoot = Join-Path $outputRoot $runId
$rawRoot = Join-Path $runRoot "raw"
$evidenceRoot = Join-Path $runRoot "evidence"
$ownershipRoot = Join-Path $runRoot "ownership"

New-Item -ItemType Directory -Force -Path $rawRoot | Out-Null
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

function Resolve-DiagnosticsTool {
    if (-not [string]::IsNullOrWhiteSpace($DiagnosticsToolPath)) {
        $resolved = [System.IO.Path]::GetFullPath($DiagnosticsToolPath, (Get-Location).Path)
        if (Test-Path -LiteralPath $resolved -PathType Leaf) {
            return $resolved
        }

        throw "Diagnostics tool was not found: $resolved"
    }

    $localNames = if ($IsWindows) {
        @("dotnet-stack.exe", "dotnet-stack")
    }
    else {
        @("dotnet-stack")
    }
    foreach ($name in $localNames) {
        $candidate = Join-Path $repositoryRoot ".tools/$name"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $command = Get-Command "dotnet-stack" -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }

    return $command.Source
}

function Get-ProcessTree {
    param(
        [Parameter(Mandatory)]
        [int]$RootProcessId,
        [DateTimeOffset]$NotBeforeUtc = [DateTimeOffset]::MinValue
    )

    if ($IsWindows) {
        $pending = [System.Collections.Generic.Queue[int]]::new()
        $pending.Enqueue($RootProcessId)
        $result = @()
        while ($pending.Count -gt 0) {
            $parent = $pending.Dequeue()
            $children = @(
                Get-CimInstance `
                    -ClassName Win32_Process `
                    -Filter "ParentProcessId = $parent" `
                    -ErrorAction SilentlyContinue
            )
            foreach ($child in $children) {
                $creationTime = [DateTimeOffset]$child.CreationDate
                if ($creationTime -lt $NotBeforeUtc) {
                    continue
                }

                $result += [pscustomobject]@{
                    processId = [int]$child.ProcessId
                    parentProcessId = [int]$child.ParentProcessId
                    name = [string]$child.Name
                    createdAtUtc = $creationTime.ToUniversalTime().ToString("O")
                }
                $pending.Enqueue([int]$child.ProcessId)
            }
        }

        return $result
    }

    $rows = @(& ps -eo pid=,ppid=,comm= 2>$null)
    $processes = @(
        foreach ($row in $rows) {
            if ($row -match '^\s*(\d+)\s+(\d+)\s+(.+?)\s*$') {
                [pscustomobject]@{
                    processId = [int]$Matches[1]
                    parentProcessId = [int]$Matches[2]
                    name = $Matches[3]
                }
            }
        }
    )
    $pending = [System.Collections.Generic.Queue[int]]::new()
    $pending.Enqueue($RootProcessId)
    $result = @()
    while ($pending.Count -gt 0) {
        $parent = $pending.Dequeue()
        foreach ($child in @($processes | Where-Object { $_.parentProcessId -eq $parent })) {
            $result += $child
            $pending.Enqueue($child.processId)
        }
    }

    return $result
}

function Save-ManagedStack {
    param(
        [Parameter(Mandatory)]
        [int]$TargetProcessId,
        [Parameter(Mandatory)]
        [string]$Destination
    )

    if ([string]::IsNullOrWhiteSpace($script:diagnosticsTool)) {
        Set-Content -LiteralPath $Destination -Encoding utf8 `
            -Value "dotnet-stack is unavailable. Install it in .tools to capture managed stacks."
        return [pscustomobject]@{
            available = $false
            exitCode = $null
            timedOut = $false
        }
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:diagnosticsTool
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add("report")
    $startInfo.ArgumentList.Add("--process-id")
    $startInfo.ArgumentList.Add(
        $TargetProcessId.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    $stackProcess = [System.Diagnostics.Process]::new()
    $stackProcess.StartInfo = $startInfo
    try {
        if (-not $stackProcess.Start()) {
            throw "dotnet-stack did not start."
        }

        $stdoutTask = $stackProcess.StandardOutput.ReadToEndAsync()
        $stderrTask = $stackProcess.StandardError.ReadToEndAsync()
        $timedOut = -not $stackProcess.WaitForExit(15000)
        if ($timedOut) {
            $stackProcess.Kill($true)
            $stackProcess.WaitForExit()
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        [System.IO.File]::WriteAllText(
            $Destination,
            $stdout + $stderr,
            [System.Text.UTF8Encoding]::new($false))
        return [pscustomobject]@{
            available = $true
            exitCode = $stackProcess.ExitCode
            timedOut = $timedOut
        }
    }
    finally {
        $stackProcess.Dispose()
    }
}

function Save-ProcessEvidence {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)]
        [string]$AssemblyName,
        [Parameter(Mandatory)]
        [int]$Iteration,
        [Parameter(Mandatory)]
        [string]$Phase,
        [Parameter(Mandatory)]
        [string]$Reason
    )

    $safeReason = $Reason -replace '[^A-Za-z0-9_.-]', '-'
    $directory = Join-Path $evidenceRoot (
        "$AssemblyName/iteration-{0:D4}/{1}-{2}" -f $Iteration, $Phase, $safeReason)
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $threadSnapshot = @()
    if ($IsWindows -and -not $Process.HasExited) {
        try {
            $Process.Refresh()
            foreach ($thread in @($Process.Threads)) {
                $waitReason = $null
                if ($thread.ThreadState -eq [System.Diagnostics.ThreadState]::Wait) {
                    try {
                        $waitReason = $thread.WaitReason.ToString()
                    }
                    catch [System.InvalidOperationException] {
                        $waitReason = "unavailable"
                    }
                }

                $threadSnapshot += [pscustomobject]@{
                    id = $thread.Id
                    state = $thread.ThreadState.ToString()
                    waitReason = $waitReason
                    totalProcessorTimeMs = $thread.TotalProcessorTime.TotalMilliseconds
                }
            }
        }
        catch [System.InvalidOperationException] {
            $threadSnapshot = @()
        }
    }

    $processTree = @(Get-ProcessTree -RootProcessId $Process.Id)
    $stackResult = if ($Process.HasExited) {
        [pscustomobject]@{
            available = $false
            exitCode = $null
            timedOut = $false
        }
    }
    else {
        Save-ManagedStack `
            -TargetProcessId $Process.Id `
            -Destination (Join-Path $directory "managed-stack.txt")
    }
    $evidence = [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        reason = $Reason
        processId = $Process.Id
        processName = if ($Process.HasExited) { $null } else { $Process.ProcessName }
        hasExited = $Process.HasExited
        threads = $threadSnapshot
        processTree = $processTree
        managedStack = $stackResult
    }
    $evidence |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $directory "process-evidence.json") -Encoding utf8
    return [System.IO.Path]::GetRelativePath($runRoot, $directory).
        Replace([System.IO.Path]::DirectorySeparatorChar, '/')
}

function Read-TeardownMarker {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $states = @()
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ($line -match '^(started|disposing|disposed)\|(\d+)\|(\d+)$') {
            $states += [pscustomobject]@{
                state = $Matches[1]
                processId = [int]$Matches[2]
                timestamp = [long]$Matches[3]
            }
        }
    }

    $started = @($states | Where-Object state -eq "started" | Select-Object -Last 1)
    $disposing = @($states | Where-Object state -eq "disposing" | Select-Object -Last 1)
    $disposed = @($states | Where-Object state -eq "disposed" | Select-Object -Last 1)
    return [pscustomobject]@{
        states = $states
        started = if ($started.Count -eq 0) { $null } else { $started[0] }
        disposing = if ($disposing.Count -eq 0) { $null } else { $disposing[0] }
        disposed = if ($disposed.Count -eq 0) { $null } else { $disposed[0] }
    }
}

function Invoke-IsolatedProcess {
    param(
        [Parameter(Mandatory)]
        [string]$AssemblyName,
        [Parameter(Mandatory)]
        [int]$Iteration,
        [Parameter(Mandatory)]
        [string]$Phase,
        [Parameter(Mandatory)]
        [string]$FileName,
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [hashtable]$Environment = @{},
        [string]$LifecycleMarkerPath,
        [double]$EvidenceThresholdSeconds = $SlowPhaseThresholdSeconds
    )

    $phaseDirectory = Join-Path $rawRoot (
        "$AssemblyName/iteration-{0:D4}" -f $Iteration)
    New-Item -ItemType Directory -Force -Path $phaseDirectory | Out-Null
    $stdoutPath = Join-Path $phaseDirectory "$Phase.stdout.txt"
    $stderrPath = Join-Path $phaseDirectory "$Phase.stderr.txt"

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $repositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    foreach ($entry in $Environment.GetEnumerator()) {
        $startInfo.Environment[$entry.Key] = [string]$entry.Value
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $evidence = @()
    $slowEvidenceCaptured = $false
    $exitEvidenceCaptured = $false
    $teardownObservedAt = $null
    try {
        if (-not $process.Start()) {
            throw "Process did not start for $AssemblyName/$Phase."
        }

        $processId = $process.Id
        $processStartedAt = [DateTimeOffset]$process.StartTime.ToUniversalTime()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        while (-not $process.WaitForExit(25)) {
            if (-not $slowEvidenceCaptured -and
                [string]::IsNullOrWhiteSpace($LifecycleMarkerPath) -and
                $stopwatch.Elapsed.TotalSeconds -ge $EvidenceThresholdSeconds) {
                $evidence += Save-ProcessEvidence `
                    -Process $process `
                    -AssemblyName $AssemblyName `
                    -Iteration $Iteration `
                    -Phase $Phase `
                    -Reason "slow-phase"
                $slowEvidenceCaptured = $true
            }

            if (-not [string]::IsNullOrWhiteSpace($LifecycleMarkerPath)) {
                $marker = Read-TeardownMarker -Path $LifecycleMarkerPath
                if ($null -eq $teardownObservedAt -and $null -ne $marker?.disposed) {
                    $teardownObservedAt = [DateTimeOffset]::UtcNow
                }

                if ($null -ne $teardownObservedAt -and
                    -not $exitEvidenceCaptured -and
                    ([DateTimeOffset]::UtcNow - $teardownObservedAt).TotalSeconds -ge
                        $ExitThresholdSeconds) {
                    $evidence += Save-ProcessEvidence `
                        -Process $process `
                        -AssemblyName $AssemblyName `
                        -Iteration $Iteration `
                        -Phase $Phase `
                        -Reason "slow-exit-after-teardown"
                    $exitEvidenceCaptured = $true
                }
            }

            if ($stopwatch.Elapsed.TotalSeconds -ge $PhaseTimeoutSeconds) {
                $timedOut = $true
                $evidence += Save-ProcessEvidence `
                    -Process $process `
                    -AssemblyName $AssemblyName `
                    -Iteration $Iteration `
                    -Phase $Phase `
                    -Reason "timeout"
                $process.Kill($true)
                $process.WaitForExit()
                break
            }
        }

        $stopwatch.Stop()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        [System.IO.File]::WriteAllText(
            $stdoutPath,
            $stdout,
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            $stderrPath,
            $stderr,
            [System.Text.UTF8Encoding]::new($false))
        $residualChildren = @(
            Get-ProcessTree `
                -RootProcessId $processId `
                -NotBeforeUtc $processStartedAt
        )
        return [pscustomobject]@{
            assembly = $AssemblyName
            iteration = $Iteration
            phase = $Phase
            processId = $processId
            exitCode = $process.ExitCode
            durationMs = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
            timedOut = $timedOut
            stdout = $stdout
            stderr = $stderr
            stdoutPath = [System.IO.Path]::GetRelativePath($runRoot, $stdoutPath).
                Replace([System.IO.Path]::DirectorySeparatorChar, '/')
            stderrPath = [System.IO.Path]::GetRelativePath($runRoot, $stderrPath).
                Replace([System.IO.Path]::DirectorySeparatorChar, '/')
            residualChildren = $residualChildren
            evidence = $evidence
            endedAtUnixMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Test-JsonProtocol {
    param(
        [Parameter(Mandatory)]
        [string]$Phase,
        [Parameter(Mandatory)]
        [string]$Content
    )

    $lines = @($Content -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    try {
        if ($Phase -eq "execution") {
            if ($lines.Count -eq 0) {
                return $false
            }

            foreach ($line in $lines) {
                $null = $line | ConvertFrom-Json -ErrorAction Stop
            }
            return $true
        }

        if ($lines.Count -ne 1) {
            return $false
        }

        $payload = $lines[0] | ConvertFrom-Json -ErrorAction Stop
        if ($Phase -eq "load") {
            return $payload.Success -eq $true -and $payload.Unloaded -eq $true
        }

        if ($Phase -eq "discovery") {
            return $payload -is [System.Array]
        }

        return $null -ne $payload
    }
    catch [System.ArgumentException] {
        return $false
    }
    catch [System.Management.Automation.RuntimeException] {
        return $false
    }
}

function New-ProcessPhaseResult {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$ProcessResult
    )

    $forbiddenOutput = @(
        "Waiting 10 seconds for foreground threads to exit",
        "Unhandled exception",
        "Fatal error",
        "The active test run was aborted"
    )
    $unexpectedText = @(
        $forbiddenOutput |
            Where-Object {
                $ProcessResult.stdout.Contains($_, [StringComparison]::OrdinalIgnoreCase) -or
                $ProcessResult.stderr.Contains($_, [StringComparison]::OrdinalIgnoreCase)
            }
    )
    $protocolValid = Test-JsonProtocol `
        -Phase $ProcessResult.phase `
        -Content $ProcessResult.stdout
    $stderrClean = [string]::IsNullOrWhiteSpace($ProcessResult.stderr)
    $success = $ProcessResult.exitCode -eq 0 -and
        -not $ProcessResult.timedOut -and
        $ProcessResult.residualChildren.Count -eq 0 -and
        $protocolValid -and
        $stderrClean -and
        $unexpectedText.Count -eq 0
    return [pscustomobject]@{
        assembly = $ProcessResult.assembly
        iteration = $ProcessResult.iteration
        phase = $ProcessResult.phase
        processId = $ProcessResult.processId
        success = $success
        exitCode = $ProcessResult.exitCode
        durationMs = $ProcessResult.durationMs
        timedOut = $ProcessResult.timedOut
        stdoutPolluted = -not $protocolValid -or $unexpectedText.Count -gt 0
        stderrPolluted = -not $stderrClean
        unexpectedOutput = $unexpectedText
        residualChildCount = $ProcessResult.residualChildren.Count
        stdoutPath = $ProcessResult.stdoutPath
        stderrPath = $ProcessResult.stderrPath
        evidence = $ProcessResult.evidence
    }
}

function Get-Percentile {
    param(
        [Parameter(Mandatory)]
        [double[]]$Values,
        [Parameter(Mandatory)]
        [ValidateRange(0, 1)]
        [double]$Percentile
    )

    if ($Values.Count -eq 0) {
        return $null
    }

    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(
        0,
        [Math]::Ceiling($Percentile * $sorted.Count) - 1)
    return [Math]::Round([double]$sorted[$index], 3)
}

function New-Statistics {
    param(
        [Parameter(Mandatory)]
        [object[]]$Results
    )

    return @(
        $Results |
            Group-Object assembly, phase |
            Sort-Object Name |
            ForEach-Object {
                $durations = [double[]]@($_.Group | ForEach-Object { $_.durationMs })
                $passed = @($_.Group | Where-Object success).Count
                [pscustomobject]@{
                    assembly = $_.Group[0].assembly
                    phase = $_.Group[0].phase
                    runs = $_.Count
                    passed = $passed
                    successRate = [Math]::Round($passed / $_.Count, 6)
                    p50Ms = Get-Percentile -Values $durations -Percentile 0.50
                    p95Ms = Get-Percentile -Values $durations -Percentile 0.95
                    p99Ms = Get-Percentile -Values $durations -Percentile 0.99
                    maxMs = [Math]::Round(
                        [double]($durations | Measure-Object -Maximum).Maximum,
                        3)
                }
            }
    )
}

$script:diagnosticsTool = Resolve-DiagnosticsTool
$ownershipPassed = $true
$ownershipError = $null
try {
    & (Join-Path $PSScriptRoot "audit-lifecycle-ownership.ps1") `
        -OutputDirectory $ownershipRoot
}
catch {
    $ownershipPassed = $false
    $ownershipError = $_.Exception.GetType().Name
    Write-Warning "Lifecycle ownership audit failed; dynamic probing will continue."
}

if (-not $NoBuild) {
    & dotnet build $solutionPath `
        -c $Configuration `
        --no-incremental `
        -p:TreatWarningsAsErrors=true `
        -p:CodeAnalysisTreatWarningsAsErrors=true `
        -p:EnableNETAnalyzers=true `
        -p:AnalysisMode=All `
        -p:EnforceCodeStyleInBuild=true `
        -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "Strict solution build failed."
    }
}

if (-not (Test-Path -LiteralPath $probeAssembly -PathType Leaf)) {
    throw "Assembly lifecycle probe was not built: $probeAssembly"
}

$testProjects = @(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "tests") `
        -Filter "*.Tests.csproj" `
        -File `
        -Recurse |
        Where-Object {
            $project = $_
            @($AssemblyPattern | Where-Object { $project.BaseName -like $_ }).Count -gt 0
        } |
        Sort-Object BaseName
)
if ($testProjects.Count -eq 0) {
    throw "No xUnit test assemblies were found."
}

$phaseResults = @()
if ($ValidateForensics) {
    if ([string]::IsNullOrWhiteSpace($script:diagnosticsTool)) {
        throw "Forensics validation requires dotnet-stack."
    }

    $selfTestAssembly = Join-Path $testProjects[0].DirectoryName (
        "bin/$Configuration/net10.0/$($testProjects[0].BaseName).dll")
    $selfTest = Invoke-IsolatedProcess `
        -AssemblyName "Gate.Forensics" `
        -Iteration 1 `
        -Phase "load" `
        -FileName "dotnet" `
        -Arguments @(
            $probeAssembly,
            "--assembly",
            $selfTestAssembly,
            "--hold-after-unload-ms",
            "5000"
        ) `
        -EvidenceThresholdSeconds 0.25
    $selfTestPhase = New-ProcessPhaseResult -ProcessResult $selfTest
    $evidenceReports = @(
        foreach ($relativeEvidence in $selfTest.evidence) {
            $evidencePath = Join-Path $runRoot $relativeEvidence "process-evidence.json"
            if (Test-Path -LiteralPath $evidencePath -PathType Leaf) {
                Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
            }
        }
    )
    $forensicsValid = $selfTestPhase.success -and
        $evidenceReports.Count -gt 0 -and
        @($evidenceReports | Where-Object { $_.managedStack.available -eq $true }).Count -gt 0
    $phaseResults += [pscustomobject]@{
        assembly = "Gate.Forensics"
        iteration = 1
        phase = "forensics-self-test"
        processId = $selfTest.processId
        success = $forensicsValid
        exitCode = if ($forensicsValid) { 0 } else { 1 }
        durationMs = $selfTest.durationMs
        timedOut = $selfTest.timedOut
        stdoutPolluted = $selfTestPhase.stdoutPolluted
        stderrPolluted = $selfTestPhase.stderrPolluted
        unexpectedOutput = $selfTestPhase.unexpectedOutput
        residualChildCount = $selfTestPhase.residualChildCount
        stdoutPath = $selfTest.stdoutPath
        stderrPath = $selfTest.stderrPath
        evidence = $selfTest.evidence
    }
}

foreach ($testProject in $testProjects) {
    $assemblyName = $testProject.BaseName
    $assemblyPath = Join-Path $testProject.DirectoryName (
        "bin/$Configuration/net10.0/$assemblyName.dll")
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Test assembly was not built: $assemblyPath"
    }

    Write-Host "Lifecycle probing $assemblyName ($resolvedIterations iteration(s))"
    for ($iteration = 1; $iteration -le $resolvedIterations; $iteration++) {
        $load = Invoke-IsolatedProcess `
            -AssemblyName $assemblyName `
            -Iteration $iteration `
            -Phase "load" `
            -FileName "dotnet" `
            -Arguments @($probeAssembly, "--assembly", $assemblyPath)
        $phaseResults += New-ProcessPhaseResult -ProcessResult $load

        $assemblyInfo = Invoke-IsolatedProcess `
            -AssemblyName $assemblyName `
            -Iteration $iteration `
            -Phase "assembly-info" `
            -FileName "dotnet" `
            -Arguments @($assemblyPath, "-assemblyInfo")
        $phaseResults += New-ProcessPhaseResult -ProcessResult $assemblyInfo

        $discovery = Invoke-IsolatedProcess `
            -AssemblyName $assemblyName `
            -Iteration $iteration `
            -Phase "discovery" `
            -FileName "dotnet" `
            -Arguments @(
                $assemblyPath,
                "-list",
                "full",
                "-automated",
                "-noLogo",
                "-noColor"
            )
        $phaseResults += New-ProcessPhaseResult -ProcessResult $discovery

        $markerPath = Join-Path $rawRoot (
            "$assemblyName/iteration-{0:D4}/execution.lifecycle" -f $iteration)
        $execution = Invoke-IsolatedProcess `
            -AssemblyName $assemblyName `
            -Iteration $iteration `
            -Phase "execution" `
            -FileName "dotnet" `
            -Arguments @(
                $assemblyPath,
                "-automated",
                "-noLogo",
                "-noColor",
                "-parallel",
                "none"
            ) `
            -Environment @{
                DOWNKYI_LIFECYCLE_MARKER = $markerPath
            } `
            -LifecycleMarkerPath $markerPath
        $phaseResults += New-ProcessPhaseResult -ProcessResult $execution

        $marker = Read-TeardownMarker -Path $markerPath
        $markerValid = $null -ne $marker -and
            $null -ne $marker.started -and
            $null -ne $marker.disposing -and
            $null -ne $marker.disposed -and
            $marker.started.processId -eq $marker.disposing.processId -and
            $marker.started.processId -eq $marker.disposed.processId
        $testRootRemoved = $false
        $teardownDuration = 0.0
        $exitDuration = [double]$execution.durationMs
        if ($markerValid) {
            $testRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
                "downkyi-tests/$assemblyName/$($marker.started.processId)")
            $testRootRemoved = -not (Test-Path -LiteralPath $testRoot)
            $teardownDuration = [Math]::Max(
                0,
                [double]($marker.disposed.timestamp - $marker.disposing.timestamp))
            $exitDuration = [Math]::Max(
                0,
                [double]($execution.endedAtUnixMs - $marker.disposed.timestamp))
        }

        $phaseResults += [pscustomobject]@{
            assembly = $assemblyName
            iteration = $iteration
            phase = "assembly-teardown"
            success = $markerValid -and $testRootRemoved
            exitCode = if ($markerValid -and $testRootRemoved) { 0 } else { 1 }
            durationMs = $teardownDuration
            timedOut = $false
            stdoutPolluted = $false
            stderrPolluted = $false
            unexpectedOutput = @()
            residualChildCount = 0
            stdoutPath = $null
            stderrPath = $null
            evidence = @()
        }
        $exitSucceeded = $execution.exitCode -eq 0 -and
            -not $execution.timedOut -and
            $execution.residualChildren.Count -eq 0 -and
            $exitDuration -le ($ExitThresholdSeconds * 1000)
        $phaseResults += [pscustomobject]@{
            assembly = $assemblyName
            iteration = $iteration
            phase = "process-exit"
            success = $exitSucceeded
            exitCode = if ($exitSucceeded) { 0 } else { 1 }
            durationMs = [Math]::Round($exitDuration, 3)
            timedOut = $execution.timedOut
            stdoutPolluted = $false
            stderrPolluted = $false
            unexpectedOutput = @()
            residualChildCount = $execution.residualChildren.Count
            stdoutPath = $execution.stdoutPath
            stderrPath = $execution.stderrPath
            evidence = $execution.evidence
        }
    }
}

$statistics = New-Statistics -Results $phaseResults
$failedResults = @($phaseResults | Where-Object { -not $_.success })
$runtime = (& dotnet --version).Trim()
$commitSha = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$workingTreeDirty = @(& git -C $repositoryRoot status --porcelain).Count -gt 0
$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    profile = $Profile
    iterations = $resolvedIterations
    runtime = $runtime
    operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    commitSha = $commitSha
    workingTreeDirty = $workingTreeDirty
    testAssemblyCount = $testProjects.Count
    phaseTimeoutSeconds = $PhaseTimeoutSeconds
    slowPhaseThresholdSeconds = $SlowPhaseThresholdSeconds
    exitThresholdSeconds = $ExitThresholdSeconds
    diagnosticsTool = if ($null -eq $script:diagnosticsTool) {
        "unavailable"
    }
    else {
        [System.IO.Path]::GetFileName($script:diagnosticsTool)
    }
    ownershipAuditPassed = $ownershipPassed
    ownershipAuditErrorType = $ownershipError
    successful = $ownershipPassed -and $failedResults.Count -eq 0
    failedPhaseCount = $failedResults.Count
    statistics = $statistics
    results = $phaseResults
}
$jsonPath = Join-Path $runRoot "assembly-lifecycle-report.json"
$markdownPath = Join-Path $runRoot "assembly-lifecycle-report.md"
$report | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding utf8

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add("# Assembly Lifecycle Stability Report")
$markdown.Add("")
$markdown.Add("- Profile: ``$Profile``")
$markdown.Add("- Iterations per assembly: $resolvedIterations")
$markdown.Add("- Runtime: ``$runtime``")
$markdown.Add("- OS: ``$($report.operatingSystem)``")
$markdown.Add("- Architecture: ``$($report.architecture)``")
$markdown.Add("- Commit: ``$commitSha``")
$markdown.Add("- Working tree dirty: ``$workingTreeDirty``")
$markdown.Add("- Assemblies: $($testProjects.Count)")
$markdown.Add("- Ownership audit: $(if ($ownershipPassed) { 'passed' } else { 'failed' })")
$markdown.Add("- Failed phases: $($failedResults.Count)")
$markdown.Add("")
$markdown.Add("| Assembly | Phase | Pass / Runs | Success | P50 ms | P95 ms | P99 ms | Max ms |")
$markdown.Add("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |")
foreach ($item in $statistics) {
    $markdown.Add(
        "| $($item.assembly) | $($item.phase) | $($item.passed) / $($item.runs) | " +
        "$([Math]::Round($item.successRate * 100, 2))% | $($item.p50Ms) | " +
        "$($item.p95Ms) | $($item.p99Ms) | $($item.maxMs) |")
}
$markdown.Add("")
$markdown.Add("## Failures")
$markdown.Add("")
if ($failedResults.Count -eq 0) {
    $markdown.Add("None.")
}
else {
    foreach ($failure in $failedResults) {
        $markdown.Add(
            "- ``$($failure.assembly)`` iteration $($failure.iteration), " +
            "``$($failure.phase)``: exit=$($failure.exitCode), " +
            "timeout=$($failure.timedOut), stdoutPolluted=$($failure.stdoutPolluted), " +
            "stderrPolluted=$($failure.stderrPolluted), " +
            "residualChildren=$($failure.residualChildCount)")
    }
}
$markdown | Set-Content -LiteralPath $markdownPath -Encoding utf8

$latestPath = Join-Path $outputRoot "latest-run.txt"
Set-Content -LiteralPath $latestPath -Encoding ascii -Value $runId
Write-Host "Assembly lifecycle report: $markdownPath"
Write-Host "Assemblies: $($testProjects.Count); phase results: $($phaseResults.Count); failures: $($failedResults.Count)"

if (-not $report.successful) {
    throw "Assembly Lifecycle Stability Gate failed."
}
