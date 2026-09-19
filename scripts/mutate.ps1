<#
.SYNOPSIS
    Measures what the test suite asserts, not only what it runs.

.DESCRIPTION
    Coverage says every line ran. It cannot say that any test would fail if a
    line were wrong, and those are different claims. Mutation testing asks the
    second one directly: change an operator or a literal in src/, run the tests
    that reach it, and see whether any of them notices. A mutant nothing
    notices is a line that executes with nothing asserting what it does.

    dotnet-stryker does the work, pinned in .config/dotnet-tools.json beside the
    two tools that already collect and render coverage.

    The test runner is the Microsoft Testing Platform one, set in
    stryker-config.json, and that is not a preference. Stryker defaults to
    VSTest, and the .NET 10 SDK runs these projects through MTP instead - the
    same fact that already forced coverlet.console over coverlet.collector,
    which restores, builds, and measures nothing at all.

    Which test projects run against which source project is read from the
    solution rather than written down here. Pairing a source project with the
    test project of the same name is the obvious configuration and it
    manufactures false survivors: a mutant in the core that only the command
    line suite kills would come back marked as unasserted, and somebody would
    then write a test for behaviour that already has one. Stryker derives the
    pairing from the project references, and that derivation was checked once
    against what the coverage reports measured - it agreed on all four
    projects. A second copy of that pairing here would be the copy that rots.

    This is not part of verify.ps1 and does not run in CI. It takes tens of
    minutes, and a step that slow in the edit-run loop becomes a -SkipMutation
    switch nobody ever removes, which is a check lost while still looking like
    a check present.

    Written for Windows PowerShell 5.1, so it runs in the default shell without
    installing anything: no `&&`, no ternary, no null-coalescing.

    Kept to ASCII for the same reason. The file carries no byte-order mark, and
    5.1 reads a mark-less file in the system code page rather than as UTF-8, so
    an em dash written here reaches the reader as three wrong characters.

.PARAMETER Configuration
    Build configuration. Defaults to Release, matching coverage.ps1: the suite
    measured is the suite the pipeline runs.

.PARAMETER Project
    Mutate one project under src/, by directory name. The full run is long, and
    repeating a single project is the ordinary operation once it has been done
    once.

.PARAMETER Concurrency
    How many mutants Stryker tests at once. Defaults to a quarter of the logical
    processors, which is deliberately below what the machine could bear.

    Stryker's own default is half of them, and each one holds a live test server
    process per test assembly. On a 24 processor machine that measured out at 86
    dotnet processes and six gigabytes, held for the best part of an hour, on
    the machine somebody is trying to work on. Twice the speed is not worth a
    tool that gets run once and then avoided, and nothing waits on this run: it
    is on demand and it gates nothing.

    Raise it when nobody is sitting behind the machine.

.EXAMPLE
    .\scripts\mutate.ps1

.EXAMPLE
    .\scripts\mutate.ps1 -Project Preflight.Rules

.EXAMPLE
    .\scripts\mutate.ps1 -Concurrency 12
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Project,
    [int] $Concurrency = 0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$outputRoot = 'artifacts\mutation'

if ($Concurrency -le 0) {
    $Concurrency = [math]::Max(2, [int] ([Environment]::ProcessorCount / 4))
}

<#
.SYNOPSIS
    Counts the mutants in a Stryker JSON report by the status it gave them.

.DESCRIPTION
    The report follows the mutation-testing-elements schema: one entry per
    source file, each holding a list of mutants, each with a status. The
    statuses this script distinguishes are the ones that mean different things
    to a reader:

      Killed        a test failed. The desired outcome.
      Survived      every test passed. The line runs and nothing asserts it.
      Timeout       the mutant hung the suite. Counted as killed, because the
                    suite did notice; it just noticed by not finishing.
      NoCoverage    no test reaches the line. Impossible against a suite at
                    100% coverage, so one of these means the source project was
                    paired with the wrong test projects, not that the suite has
                    a hole.
      CompileError  the mutant does not compile. Not a survivor and not a mark
                    against the suite, but counted, because warnings are errors
                    in this repository and that pushes the number up.
      RuntimeError  the mutant broke the test host rather than failing a test.
                    Outside the score, and worth seeing.
      Pending       never got a verdict. In a finished report it means the run
                    did not finish.
      Ignored       filtered out before testing.

    An unrecognised status throws rather than being folded into a leftover
    bucket. The statuses above are how this script decides whether the run
    measured anything, so one it does not know about is a report whose shape has
    moved underneath the arithmetic - and a mutant quietly counted as nothing is
    the failure this whole script exists to make loud.
#>
function Get-MutantCounts {
    param([string] $ReportPath)

    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json

    $counts = @{
        Killed = 0
        Survived = 0
        Timeout = 0
        NoCoverage = 0
        CompileError = 0
        RuntimeError = 0
        Pending = 0
        Ignored = 0
    }

    foreach ($file in $report.files.PSObject.Properties) {
        foreach ($mutant in $file.Value.mutants) {
            if (-not $counts.ContainsKey($mutant.status)) {
                Write-Error "Unknown mutant status '$($mutant.status)' in $ReportPath. The report schema has changed and these counts can no longer be trusted."
            }

            $counts[$mutant.status]++
        }
    }

    return New-Object psobject -Property $counts
}

<#
.SYNOPSIS
    Stops the run when a project produced testable mutants and killed none.

.DESCRIPTION
    This is why the measurement is a script rather than a line in a readme
    telling people to run Stryker.

    The Microsoft Testing Platform runner has a failure mode in which the mutant
    switch never reaches the test process. Every mutant is then applied to
    nothing, every test passes, every mutant comes back as survived, and the run
    ends successfully having written a complete and plausible report.

    That report is indistinguishable by eye from the report of a suite that
    asserts nothing at all - which is exactly the hypothesis this measurement
    exists to test. The worst imaginable result and a broken instrument produce
    the same picture, and only one of them is about the code.

    coverage.ps1 carries the same guard one floor down, against measuring zero
    assemblies, and it is there because it caught a real one. Every failure this
    project's instrumentation has had was silent.

    A timeout counts as a kill: the suite did notice the mutant, it just
    noticed by not finishing. A project with no testable mutants at all - every
    one a compile error or filtered out - is not a failure and passes through.
#>
function Assert-MutantsWereKilled {
    param(
        [string] $Project,
        [psobject] $Counts
    )

    $testable = $Counts.Killed + $Counts.Survived + $Counts.Timeout

    if ($testable -eq 0) { return }
    if (($Counts.Killed + $Counts.Timeout) -gt 0) { return }

    Write-Host ''
    Write-Host "error: $Project produced $testable testable mutants and killed none of them." -ForegroundColor Red
    Write-Host 'Every test passed against every single one, which a suite this size does not do.' -ForegroundColor Red
    Write-Host 'This is an instrumentation failure and not a suite failure. The likely cause is' -ForegroundColor Red
    Write-Host 'the mutant switch never reaching the test process, so each mutant was measured' -ForegroundColor Red
    Write-Host 'against unmodified code. Do not record this run. Fix the instrument first.' -ForegroundColor Red

    exit 1
}

# Every directory under src/ is mutated, and the list is not written out here
# for the same reason coverage.ps1 does not write out its own: two copies of one
# list means one of them is behind, and the one that is behind fails quietly.
# samples/ and tests/ stay out by not being under src/ rather than by an
# exclusion rule. The sample plugin in particular must never have its shape
# decided by a measurement target, because it is the thing a reader copies.
$sourceProjects = Get-ChildItem -Path 'src' -Directory | Select-Object -ExpandProperty Name

if ($Project) {
    if ($sourceProjects -notcontains $Project) {
        Write-Error "No project named '$Project' under src. Found: $($sourceProjects -join ', ')."
    }

    $sourceProjects = @($Project)
}

dotnet tool restore
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet tool restore failed with exit code $LASTEXITCODE." }

# Said before the wait rather than discovered during it. Stryker's only progress
# output is a live console bar that writes nothing once the output is
# redirected, so a run from a script or a pipeline is silent from the first
# minute to the last - and a silent hour is indistinguishable from a hang to
# whoever is watching. The measured shape of one project on a 24 processor
# machine: roughly an hour, and a test server process per test assembly per
# concurrent mutant.
Write-Host ''
Write-Host "Mutating $($sourceProjects.Count) project(s) at concurrency $Concurrency of $([Environment]::ProcessorCount) logical processors."
Write-Host 'Expect tens of minutes per project, and no output at all while each one runs.'
Write-Host 'Nothing depends on this: it gates no build and no pipeline.'

$results = @()

foreach ($name in $sourceProjects) {
    $output = Join-Path $outputRoot $name

    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }

    Write-Host ''
    Write-Host "=== mutate $name ===" -ForegroundColor Cyan

    $started = Get-Date

    # --solution is required rather than convenient: it is what lets Stryker read
    # the project references and work out which test projects reach this one.
    # --test-project is ignored in this mode, so the pairing cannot be narrowed
    # from here even by someone who wanted to.
    dotnet dotnet-stryker `
        --solution 'Preflight.slnx' `
        --project "$name.csproj" `
        --configuration $Configuration `
        --concurrency $Concurrency `
        --output $output

    $strykerExitCode = $LASTEXITCODE
    $minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)

    $report = Join-Path $output 'reports\mutation-report.json'

    # A project the tool refused to measure is recorded as not measured, and the
    # loop carries on to the next one. The tempting alternatives are both worse:
    # stopping the whole run hides the projects that would have measured fine,
    # and quietly omitting this one turns a gap into a number that looks whole.
    if ($strykerExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        Write-Host ''
        Write-Host "not measured: $name" -ForegroundColor Yellow
        Write-Host "Stryker exited $strykerExitCode after $minutes minutes without writing a report." -ForegroundColor Yellow
        Write-Host 'Record this as not measured, with the reason. Never estimate the number it' -ForegroundColor Yellow
        Write-Host 'would have produced: a score invented for a project the tool refused to' -ForegroundColor Yellow
        Write-Host 'measure is worse than an admitted gap.' -ForegroundColor Yellow

        $results += New-Object psobject -Property @{
            Project = $name
            Measured = $false
            Killed = 0
            Survived = 0
            Timeout = 0
            NoCoverage = 0
            CompileError = 0
            RuntimeError = 0
            Pending = 0
            Minutes = $minutes
        }

        continue
    }

    $counts = Get-MutantCounts -ReportPath $report

    Assert-MutantsWereKilled -Project $name -Counts $counts

    Write-Host ''
    Write-Host ("measured {0}: {1} killed, {2} survived, {3} timeout, {4} no coverage, {5} compile error, {6} min" -f `
        $name, $counts.Killed, $counts.Survived, $counts.Timeout, $counts.NoCoverage, $counts.CompileError, $minutes)

    # Not an error, and not a finding about the suite either. The suite reaches
    # every line, so a mutant no test covers means this source project was paired
    # with test projects that do not reach it - a pairing defect, to be fixed and
    # remeasured rather than written into the record.
    if ($counts.NoCoverage -gt 0) {
        Write-Host "warning: $($counts.NoCoverage) mutants report no coverage against a suite that covers every line." -ForegroundColor Yellow
        Write-Host 'That is a pairing defect between this project and its test projects, not a hole' -ForegroundColor Yellow
        Write-Host 'in the suite. Fix the pairing and measure again before recording anything.' -ForegroundColor Yellow
    }

    # Neither counts towards the score, and neither may pass unmentioned. A
    # runtime error is a mutant that broke the test host instead of failing a
    # test, and a pending one in a finished report is a mutant that never got a
    # verdict at all - so the run measured less than its own summary implies.
    if ($counts.RuntimeError -gt 0 -or $counts.Pending -gt 0) {
        Write-Host "warning: $($counts.RuntimeError) mutants ended in a runtime error and $($counts.Pending) never got a verdict." -ForegroundColor Yellow
        Write-Host 'Neither counts towards the score, so the score below is over fewer mutants than' -ForegroundColor Yellow
        Write-Host 'the totals suggest. Record both numbers beside it.' -ForegroundColor Yellow
    }

    $results += New-Object psobject -Property @{
        Project = $name
        Measured = $true
        Killed = $counts.Killed
        Survived = $counts.Survived
        Timeout = $counts.Timeout
        NoCoverage = $counts.NoCoverage
        CompileError = $counts.CompileError
        RuntimeError = $counts.RuntimeError
        Pending = $counts.Pending
        Minutes = $minutes
    }
}

Write-Host ''
# Out-String before it is written, and not Out-Host. Out-Host renders straight
# to the console and leaves the pipeline empty, so the one table worth keeping
# was the one line missing from every redirected log of this run.
$results |
    Select-Object Project, Measured, Killed, Survived, Timeout, NoCoverage, CompileError, RuntimeError, Pending, Minutes |
    Format-Table -AutoSize |
    Out-String |
    Write-Host

Write-Host "Reports: $outputRoot\<project>\reports\mutation-report.html"

# A run that could not measure everything it set out to measure does not end
# successfully, and this is the same bar coverage.ps1 sets one floor down when it
# measured fewer test projects than it found. The numbers above are real and
# worth keeping - which is why the loop finished rather than stopping at the
# first refusal - but a caller that sees exit 0 will read the run as whole, and
# it was not. The projects are named again here because the line that skipped
# each one has scrolled past by now.
$unmeasured = @($results | Where-Object { -not $_.Measured } | Select-Object -ExpandProperty Project)

if ($unmeasured.Count -gt 0) {
    Write-Host ''
    Write-Host "error: $($unmeasured.Count) of $($results.Count) projects were not measured: $($unmeasured -join ', ')." -ForegroundColor Red
    Write-Host 'The scores above are real and cover only the rest. Treat this run as partial.' -ForegroundColor Red
    exit 1
}
