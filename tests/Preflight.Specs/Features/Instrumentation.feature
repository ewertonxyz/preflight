Feature: Instrumentation
    The tool measures itself, and no synthetic data enters the report. Two of its claims are only observable from outside the
    process, which is what these scenarios are for.

    The first is that a run leaves a record behind at all, in the place the
    policy names, without anybody asking it to. The second is the contract of
    preflight measure: it is a transparent wrapper, so the exit code a script
    sees has to be the child's and nothing else. An in-process test can assert
    what the handler returns; only a real process can show what a caller reads.

    Scenario: A run records itself in the history
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        And the history holds 1 record

    Scenario: An inspection command records nothing
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        When preflight is invoked with "rules"
        Then it exits with code 0
        And the history holds 0 records

    Scenario: measure returns the child's exit code, not its own
        Given a workspace
        When preflight is invoked with "measure --label build -- git --version"
        Then it exits with code 0
        And the history holds 1 record

    Scenario: measure propagates a child that failed
        Given a workspace
        When preflight is invoked with "measure --label build -- git no-such-subcommand"
        Then it exits with code 1
        And the history holds 1 record

    Scenario: A child that cannot be started is 127, not a configuration error
        Given a workspace
        When preflight is invoked with "measure --label build -- preflight-no-such-binary"
        Then it exits with code 127
        And the history holds 0 records

    Scenario: measure without a label is refused before the child starts
        Given a workspace
        When preflight is invoked with "measure -- git --version"
        Then it exits with code 2
        And the history holds 0 records

    Scenario: An empty history is a valid answer
        Given a workspace
        When preflight is invoked with "report --since 30d"
        Then it exits with code 0
        And the report says "Nothing recorded in this window"

    Scenario: A window nobody chose is a configuration error
        Given a workspace
        When preflight is invoked with "report --since 30x"
        Then it exits with code 2

    Scenario: What a run writes is what the report reads
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage workspace"
        And preflight is invoked with "report --since 30d"
        Then it exits with code 0
        And the report says "Runs"
        And the report does not say "could not be read"
