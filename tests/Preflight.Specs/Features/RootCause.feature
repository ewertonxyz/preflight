Feature: Root cause attribution
    A skipped rule is blamed on the root of the chain
    and never on its immediate parent, and shows the wrong format alongside the
    right one. The difference is not cosmetic: the wrong one sends a developer
    to investigate a rule that is fine.

    The build-readiness stage is the documented chain — toolchain, then
    configuration, then compile-probe — so a failure at the top has two levels
    of consequence and the attribution has somewhere to go wrong.

    Background:
        Given a workspace

    Scenario: A gating failure blames the root, not the immediate parent
        Given the workspace needs git "999.0.0" or newer
        When preflight is invoked with "run --stage build-readiness"
        Then it exits with code 1
        And the report says "blocked by  core.workspace.toolchain"
        And the report does not say "blocked by  core.build.configuration"

    # The report is ordered by topological level, and that is what buys
    # the cause reading before the symptom is what that ordering buys. A
    # formatter that grouped failures first would spend it, and both lines
    # would still be present.
    Scenario: The cause is printed before the symptom
        Given the workspace needs git "999.0.0" or newer
        When preflight is invoked with "run --stage build-readiness"
        Then "core.workspace.toolchain" is reported before "core.build.compile-probe"

    # "disabled by policy" and "failed, gating" are completely
    # different situations for whoever is reading. One is somebody's decision;
    # the other is a problem.
    Scenario: A dependency disabled by policy says so, rather than reporting a failure
        Given the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage build-readiness --set core.build.configuration:enabled=false"
        Then it exits with code 0
        And the report says "disabled by policy"
        And the report does not say "failed, gating"

    # Through the executable. Disabling a root removes the cost of the
    # rules that existed only to serve it — read literally, 4.3 would leave
    # them running and able to fail the run.
    Scenario: Disabling the root of a stage eliminates the whole chain
        Given the workspace needs git "999.0.0" or newer
        When preflight is invoked with "run --stage build-readiness --set core.build.configuration:enabled=false --set core.build.compile-probe:enabled=false"
        Then it exits with code 0
        And the report says "0 rules executed"
