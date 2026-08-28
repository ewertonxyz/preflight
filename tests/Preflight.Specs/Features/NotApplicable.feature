Feature: Not applicable is not passed
    A rule that examined nothing reports `n/a`, never
    a tick, because saying it passed would claim more than is known — and
    small lies in a validation report erode trust in the whole thing.

    The distinction is invisible in the exit code, which is why it is asserted
    in the report. A tool that reported `Passed` here would be right about the
    run and wrong about the reason, and nobody would notice until they relied
    on it.

    Background:
        Given a workspace

    Scenario: A manifest declaring no tools makes the toolchain rule report n/a
        Given the workspace needs nothing
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        And the report says "n/a"
        And the report does not say "0 rules executed"

    # The empty-execution case, and the shape it must not be confused with. Nothing
    # ran because a versioned file said so — the run succeeds, and the summary
    # says in words that nothing was checked, because that line is how somebody
    # finds an overlay that disabled everything.
    Scenario: Every rule of the stage disabled succeeds, out loud
        Given the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage workspace --set core.workspace.toolchain:enabled=false --set core.workspace.dependencies:enabled=false"
        Then it exits with code 0
        And the report says "0 rules executed"
        And the report says "disabled by policy"
