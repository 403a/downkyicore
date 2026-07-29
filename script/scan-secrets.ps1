[CmdletBinding()]
param(
    [string]$GitleaksPath,
    [string]$OutputPath = 'artifacts/security/gitleaks-candidate.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repositoryRoot = (& git rev-parse --show-toplevel).Trim()
if ([string]::IsNullOrWhiteSpace($repositoryRoot)) {
    throw 'The repository root could not be resolved.'
}

if ([string]::IsNullOrWhiteSpace($GitleaksPath)) {
    $command = Get-Command gitleaks -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $GitleaksPath = $command.Source
    }
    else {
        $localCandidate = Join-Path $repositoryRoot '.tools/gitleaks/bin/gitleaks.exe'
        if (Test-Path -LiteralPath $localCandidate -PathType Leaf) {
            $GitleaksPath = $localCandidate
        }
    }
}

if ([string]::IsNullOrWhiteSpace($GitleaksPath) -or
    -not (Test-Path -LiteralPath $GitleaksPath -PathType Leaf)) {
    throw 'Gitleaks is required. Install the pinned version documented in verification-and-rollback.md.'
}

$resolvedOutput = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $OutputPath))
$outputParent = [System.IO.Path]::GetDirectoryName($resolvedOutput)
if (-not [string]::IsNullOrWhiteSpace($outputParent)) {
    [System.IO.Directory]::CreateDirectory($outputParent) | Out-Null
}

$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$scanRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $tempBase "downkyi-gitleaks-$([Guid]::NewGuid().ToString('N'))"))
if (-not $scanRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The temporary scan root resolved outside the system temporary directory.'
}

[System.IO.Directory]::CreateDirectory($scanRoot) | Out-Null
try {
    $candidateFiles = @(
        & git -C $repositoryRoot ls-files --cached --others --exclude-standard
    )
    foreach ($relativePath in $candidateFiles) {
        $source = Join-Path $repositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            continue
        }

        $destination = Join-Path $scanRoot $relativePath
        $destinationParent = [System.IO.Path]::GetDirectoryName($destination)
        if (-not [string]::IsNullOrWhiteSpace($destinationParent)) {
            [System.IO.Directory]::CreateDirectory($destinationParent) | Out-Null
        }

        [System.IO.File]::Copy($source, $destination, $true)
    }

    & $GitleaksPath dir $scanRoot `
        --config (Join-Path $repositoryRoot '.gitleaks.toml') `
        --no-banner `
        --redact `
        --max-target-megabytes 10 `
        --report-format json `
        --report-path $resolvedOutput
    $exitCode = $LASTEXITCODE

    $findingCount = 0
    if (Test-Path -LiteralPath $resolvedOutput -PathType Leaf) {
        $content = Get-Content -LiteralPath $resolvedOutput -Raw
        if (-not [string]::IsNullOrWhiteSpace($content)) {
            $findingCount = @($content | ConvertFrom-Json).Count
        }
    }

    [pscustomobject]@{
        Tool = 'gitleaks'
        Version = (& $GitleaksPath version).Trim()
        CandidateFileCount = $candidateFiles.Count
        FindingCount = $findingCount
        Passed = $exitCode -eq 0
    } | ConvertTo-Json -Compress

    if ($exitCode -ne 0) {
        throw "Gitleaks found $findingCount candidate secret(s)."
    }
}
finally {
    if ($scanRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Directory]::Exists($scanRoot)) {
        [System.IO.Directory]::Delete($scanRoot, $true)
    }
}
