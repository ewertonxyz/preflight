Feature: Blocking and gating
    These are two independent axes, and the whole point is that they answer
    different questions. `blocking` decides the
    verdict; `gating` decides whether the rules after this one are worth running.
    Collapsing them into one flag is the mistake this separation prevents, and
    a run where only one of the two took effect looks correct from either side
    alone.

    Every scenario asserts both halves — the verdict *and* whether the dependent
    executed — because that is the only pair that distinguishes the four
    quadrants from each other. The function's own tests live in Core.Tests; these observe the same four combinations through the executable,
    where the observable is an exit code and a line of report.

    The lever is core.workspace.toolchain: the only root of the workspace stage,
    with core.workspace.dependencies depending on it. A version nothing
    satisfies fails it on demand, with no compiler and no SDK involved.

    Background:
        Given a workspace
        And the workspace needs git "999.0.0" or newer

    Scenario: Blocking and gating both on
        When preflight is invoked with "run --stage workspace --set core.workspace.toolchain:blocking=true --set core.workspace.toolchain:gating=true"
        Then it exits with code 1
        And the report says "skipped"
        And the report says "blocked by  core.workspace.toolchain"

    Scenario: Blocking on, gating off
        When preflight is invoked with "run --stage workspace --set core.workspace.toolchain:blocking=true --set core.workspace.toolchain:gating=false"
        Then it exits with code 1
        And the report does not say "skipped"

    # The quadrant that is easy to miss: gating and blocking are separate axes,
    # so a rule can stop what depends on it without stopping the build. The root
    # fails without blocking, the run passes with warnings — and the dependent
    # never runs at all.
    Scenario: Blocking off, gating on
        When preflight is invoked with "run --stage workspace --set core.workspace.toolchain:blocking=false --set core.workspace.toolchain:gating=true"
        Then it exits with code 0
        And the report says "skipped"

    Scenario: Blocking off, gating off
        When preflight is invoked with "run --stage workspace --set core.workspace.toolchain:blocking=false --set core.workspace.toolchain:gating=false"
        Then it exits with code 0
        And the report does not say "skipped"

    # --no-skip turns off the propagation, not the accounting: the dependent
    # runs and its own status counts. It would be easy to read the flag as one
    # that cannot change a verdict. It can, and this is the run that proves it.
    Scenario: --no-skip runs the dependents and says so
        When preflight is invoked with "run --stage workspace --set core.workspace.toolchain:gating=true --no-skip"
        Then it exits with code 1
        And the report does not say "skipped"
        And the report says "--no-skip in effect"
