<#
.SYNOPSIS
    Collects code coverage across every test project and renders a report.

.DESCRIPTION
    Two local tools do the work, both pinned in .config/dotnet-tools.json:
      coverlet.console   collects   (MIT, github.com/coverlet-coverage/coverlet)
      ReportGenerator    renders    (MIT, github.com/danielpalme/ReportGenerator)

    Neither needs an account, a hosted service, or a licence file. That matters:
    the alternative under Microsoft.Testing.Platform is
    Microsoft.Testing.Extensions.CodeCoverage, whose package ships a proprietary
    licence file rather than an OSI expression.

    Why a script rather than `dotnet test --collect` or a coverlet MSBuild
    property: the .NET 10 SDK runs these projects through
    Microsoft.Testing.Platform, and both coverlet.collector and coverlet.msbuild
    hook the VSTest pipeline. They restore, build, and produce nothing at all.
    coverlet.console is runner-agnostic because it instruments the assemblies and
    executes the test binary directly, which works because an xUnit v3 test
    project is a real executable.

    Written for Windows PowerShell 5.1, so it runs in the default shell without
    installing anything: no `&&`, no ternary, no null-coalescing.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.EXAMPLE
    .\scripts\coverage.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$rawDir = 'artifacts\coverage\raw'
$reportDir = 'artifacts\coverage\report'

function Assert-LastExitCode {
    param([string] $What)

    if ($LASTEXITCODE -ne 0) {
        Write-Error "$What failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path -LiteralPath $rawDir) { Remove-Item -LiteralPath $rawDir -Recurse -Force }
if (Test-Path -LiteralPath $reportDir) { Remove-Item -LiteralPath $reportDir -Recurse -Force }
New-Item -ItemType Directory -Path $rawDir -Force | Out-Null

dotnet tool restore
Assert-LastExitCode 'dotnet tool restore'

dotnet build --configuration $Configuration
Assert-LastExitCode 'dotnet build'

$collected = 0

# Not every directory under tests/ holds a test project. Preflight.TestSupport
# is a plain library, and the discriminator used here is the same one
# Directory.Build.props uses to decide what becomes an OutputType=Exe test
# binary: the .Tests or .Specs suffix. Matching on "has a dll at the expected
# path" instead would pick the library up, hand it to `dotnet <dll>`, and get a
# missing-hostpolicy failure - which coverlet reports through its target's exit
# code, not its own, so it would pass straight through Assert-LastExitCode and
# land in the report as a meaningless 100%.
$testProjects = Get-ChildItem -Path 'tests' -Directory |
    Where-Object { $_.Name.EndsWith('.Tests') -or $_.Name.EndsWith('.Specs') }

foreach ($project in $testProjects) {
    $assembly = Join-Path $project.FullName "bin\$Configuration\net10.0\$($project.Name).dll"

    if (-not (Test-Path -LiteralPath $assembly)) {
        Write-Host "skip  $($project.Name) - no test assembly at $assembly"
        continue
    }

    Write-Host "cover $($project.Name)"

    # --threshold is intentionally absent. At the time of writing the tool has
    # no code, so any threshold would be either vacuous or a guaranteed failure.
    # It is turned on once there is something to measure - see the note at the
    # bottom of this file.
    #
    # samples/Sample.Production.Rules is excluded for a different reason from the
    # three above it. It is not test code: it is the worked example of
    # the worked example, and it is documentation that happens to compile. It
    # has tests, in Preflight.Rules.Tests, and they are there because a reader
    # will copy it. What it must not have is a shape decided by a coverage
    # target - the moment a branch is added or removed to move this number, the
    # example stops being the thing anyone should copy.
    dotnet coverlet $assembly `
        --target dotnet `
        --targetargs $assembly `
        --format cobertura `
        --output (Join-Path $rawDir "$($project.Name).cobertura.xml") `
        --exclude '[*.Tests]*' `
        --exclude '[*.Specs]*' `
        --exclude '[Preflight.TestSupport]*' `
        --exclude '[Sample.Production.Rules]*' `
        --exclude-by-attribute 'Obsolete' `
        --exclude-by-attribute 'GeneratedCode' `
        --exclude-by-attribute 'CompilerGenerated' `
        --skipautoprops
    Assert-LastExitCode "coverlet on $($project.Name)"

    $collected++
}

# Zero was the wrong bar. The skip above is deliberately quiet — an assembly that
# is not on disk prints one line and the loop moves on - so three test projects
# out of four used to arrive here with $collected non-zero, publish a plausible
# percentage, and hide the missing one in a log nobody reads. The count of test
# projects actually found is the honest expectation, and it adjusts itself on
# the day a fifth project arrives.
if ($collected -ne $testProjects.Count) {
    Write-Error "Measured $collected of $($testProjects.Count) test projects. A coverage run that silently leaves one out must fail loudly rather than report a number for the rest."
}

# Cobertura is absent from this list because the merged file it produced has no
# consumer left, not because of what it is called. CI uploads the four raw
# per-project reports instead: the merged one records absolute Windows paths,
# which match nothing in a repository tree once they leave this machine. Html is
# the artifact, TextSummary feeds the guard below, MarkdownSummaryGithub feeds
# the CI job summary. Generating a fourth for nobody is what this file already
# refuses elsewhere.
dotnet reportgenerator `
    "-reports:$rawDir\*.cobertura.xml" `
    "-targetdir:$reportDir" `
    '-reporttypes:Html;TextSummary;MarkdownSummaryGithub'
Assert-LastExitCode 'reportgenerator'

$summaryPath = Join-Path $reportDir 'Summary.txt'
$summary = Get-Content -LiteralPath $summaryPath -Raw

Write-Host ''
Write-Host $summary

# The guard.
#
# Every failure mode this script has hit so far has been silent: the collector
# that hooks the wrong test pipeline, a deterministic build that breaks PDB
# mapping, a report path that resolves to nothing. In each case the run ends 0
# and produces a report that measures nothing.
#
# Assemblies: 0 is therefore an error here, not a curiosity. This exists because
# it has already caught a real one.
$assemblies = 0
$match = [regex]::Match($summary, '(?m)^\s*Assemblies:\s*(\d+)')
if ($match.Success) { $assemblies = [int] $match.Groups[1].Value }

if ($assemblies -eq 0) {
    Write-Host ''
    Write-Host 'error: coverage ran to completion and measured 0 assemblies.' -ForegroundColor Red
    Write-Host 'The tests passed, so this is an instrumentation failure, not a test failure.' -ForegroundColor Red
    Write-Host 'Most likely cause: something rewrote the PDB source paths, so coverlet could' -ForegroundColor Red
    Write-Host 'not map IL back to source and instrumented nothing.' -ForegroundColor Red
    exit 1
}

Write-Host "HTML report: $reportDir\index.html"

# Turning on a threshold, later:
#
#   Add --threshold N --threshold-type line --threshold-stat total to the coverlet
#   invocation above. Pick N from what the code actually reaches, not from a
#   round number that feels respectable. A threshold set above what
#   the suite honestly achieves gets lowered under deadline pressure, and after
#   that nobody believes any of the numbers.
