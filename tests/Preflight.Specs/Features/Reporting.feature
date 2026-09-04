Feature: Reporting formats
    run, graph and report each take a --format flag, and the same input produces
    the same bytes in every one of them. The
    scenarios here are the rules of the system that the unit tests state in the
    language of a mapping: which format a caller gets, and what the tool says
    about itself when one of its own rules is defective.

    Every scenario shapes the six real rules through policy rather than through
    fakes, for the reason the rest of this suite does: a fake rule proves the
    tool, and the tool already has unit tests.

    # Errored comes first in aggregation so that a defect in a rule is
    # never reported as a problem with the workspace. Failed and Errored are
    # false friends: one is a verdict about the workspace, the other about the
    # rule. In SARIF that means the errored rule produces no result at all — it
    # goes to the invocation, where exit code 3 already calls the tool's owner
    # rather than the author of the commit. This is the only layer that says it
    # in the vocabulary a review tool reads.
    Scenario: A rule that errors is reported as a tool problem, not a workspace problem
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage workspace --format sarif --set core.workspace.dependencies:settings.manifestPath=42"
        Then it exits with code 3
        And the report says "toolExecutionNotifications"

    # The 2-versus-1 distinction: a format the tool
    # does not implement is a broken invocation, which calls the owner of the
    # tool, and not a rejected commit, which calls its author.
    Scenario: An unknown report format is a configuration error, not a rejected commit
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage workspace --format bogus"
        Then it exits with code 2

    # Both arms of graph --format. text is the default and
    # its output is what the command printed before the flag existed.
    Scenario: The graph renders as text by default and as DOT on request
        Given a workspace
        When preflight is invoked with "graph"
        Then it exits with code 0
        And the report does not say "digraph"
        When preflight is invoked with "graph --format dot"
        Then it exits with code 0
        And the report says "digraph"

    # The format of the report does not decide the verdict. A reporter that
    # swallowed an exception and returned 0, or that promoted the run to 3
    # because it had written a notification, would be caught here and nowhere
    # else.
    Scenario: SARIF does not change the exit code
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        When preflight is invoked with "run --stage workspace --format sarif"
        Then it exits with code 0
