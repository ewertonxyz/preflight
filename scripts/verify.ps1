<#
.SYNOPSIS
    Runs the whole local check: format, build, test, coverage.

.DESCRIPTION
    One command, so there is nothing to remember and nothing to chain by hand.

    This exists because chaining the steps at the prompt is a trap in Windows
    PowerShell 5.1: `&&` is a PowerShell 7 operator and 5.1 rejects it outright
    with "The token '&&' is not a valid statement separator in this version".
    Separating with `;` is worse than useless here, because it runs every step
    regardless of whether the previous one failed, and the final summary then
    hides the failure that mattered.

    Every step below stops the run on failure, and the exit code is non-zero so
    the result is usable from another script.

    Written for Windows PowerShell 5.1: no `&&`, no ternary, no null-coalescing,
    and ASCII only. The file carries no byte-order mark, and 5.1 reads a
    mark-less file in the system code page rather than as UTF-8, so an em dash
    written here reaches the reader as three wrong characters.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER SkipCoverage
    Skip the coverage step. Useful in a tight edit-run loop, where instrumenting
    every assembly costs more than the number is worth.

.EXAMPLE
    .\scripts\verify.ps1

.EXAMPLE
    .\scripts\verify.ps1 -SkipCoverage
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipCoverage
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$step = 0

function Invoke-Step {
    param(
        [string] $Name,
        [scriptblock] $Action
    )

    $script:step++
    Write-Host ''
    Write-Host "=== $script:step. $Name ===" -ForegroundColor Cyan

    & $Action

    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host "FAILED at step $script:step ($Name), exit code $LASTEXITCODE." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Invoke-Step 'restore' { dotnet restore }

Invoke-Step 'format' {
    # .editorconfig is enforced at build time through EnforceCodeStyleInBuild, but
    # whitespace and using-ordering are not compiler diagnostics. This closes that
    # gap so style never becomes a code review topic.
    dotnet format --verify-no-changes --no-restore
}

Invoke-Step 'build' {
    # TreatWarningsAsErrors is set in Directory.Build.props; there is no separate
    # warning gate here.
    dotnet build --configuration $Configuration --no-restore
}

Invoke-Step 'test' {
    # The runner is Microsoft.Testing.Platform, selected in global.json. A test
    # project containing zero tests fails with exit code 8, deliberately.
    dotnet test --configuration $Configuration --no-build
}

if ($SkipCoverage) {
    Write-Host ''
    Write-Host 'Coverage skipped (-SkipCoverage).' -ForegroundColor Yellow
}
else {
    Invoke-Step 'coverage' { & (Join-Path $PSScriptRoot 'coverage.ps1') -Configuration $Configuration }
}

Write-Host ''
Write-Host 'All checks passed.' -ForegroundColor Green
