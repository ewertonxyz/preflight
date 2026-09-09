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
    #
    # Every rule of the stage is named, including the ones whose only dependency
    # is already disabled. Leaving those to be skipped by the dependency would
    # test skip propagation instead of the empty run, and the two produce
    # different summaries.
    Scenario: Every rule of the stage disabled succeeds, out loud
        Given the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage workspace --set core.workspace.toolchain:enabled=false --set core.workspace.dependencies:enabled=false --set core.workspace.free-space:enabled=false --set core.workspace.approved-dependencies:enabled=false --set core.workspace.environment:enabled=false --set core.workspace.vcs-configuration:enabled=false --set core.workspace.submodule-pin:enabled=false"
        Then it exits with code 0
        And the report says "0 rules executed"
        And the report says "disabled by policy"

    # The only place free space is proved end to end, in a real process where
    # the observable is an exit code. Every layer below this one substitutes the
    # probe, so all of them stay green with the shipped one never handed to a
    # rule at all — and the rule that never receives it reports that it checked
    # nothing, forever, in silence.
    #
    # The floor is larger than any volume that exists, which is what makes the
    # verdict independent of the disk the suite happens to run on. A realistic
    # number would fail everywhere today and pass on the first machine with a
    # bigger array.
    Scenario: A free-space floor no volume can meet blocks the run
        Given the file "preflight.workspace.json" contains
            """
            {
              "tools": [],
              "freeSpace": [{ "path": ".", "minimumBytes": 9000000000000000000 }]
            }
            """
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 1
        And the report says "core.workspace.free-space"

    # The only place the environment probe is proved end to end, and the reason
    # is the same one the free-space scenario above gives: every layer below
    # this substitutes the probe, so all of them stay green with the shipped one
    # never handed to a rule at all — and a rule that never receives it reports
    # that it checked nothing, forever, in silence.
    #
    # The name is one nothing sets, so the verdict does not depend on the
    # machine. The pair of scenarios is what makes the assertion real: the same
    # manifest passes once the runner defines the variable, which is the only
    # way to tell "the probe read a real block" from "the rule always fails".
    Scenario: A declared environment variable that is not set blocks the run
        Given the file "preflight.workspace.json" contains
            """
            {
              "tools": [],
              "environment": ["PREFLIGHT_SPEC_NOTHING_SETS_THIS"]
            }
            """
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 1
        And the report says "core.workspace.environment"

    Scenario: A declared environment variable that is set lets the run through
        Given the environment variable "PREFLIGHT_SPEC_NOTHING_SETS_THIS" is "a value"
        And the file "preflight.workspace.json" contains
            """
            {
              "tools": [],
              "environment": ["PREFLIGHT_SPEC_NOTHING_SETS_THIS"]
            }
            """
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
