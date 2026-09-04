Feature: Root cause attribution
    A skipped rule is blamed on the root of the chain and never on its
    immediate parent. The difference is not cosmetic: the immediate parent was
    skipped too, so naming it sends a developer to investigate a rule that is
    fine.

    The build-readiness stage is a chain — toolchain, then configuration, then
    compile-probe — so a failure at the top has two levels of consequence and
    the attribution has somewhere to go wrong. The first scenario asserts the
    wrong name is absent as well as the right one being present, because a
    report naming both would satisfy a presence check and still mislead.

    Background:
        Given a workspace

    Scenario: A gating failure blames the root, not the immediate parent
        Given the workspace needs git "999.0.0" or newer
        When preflight is invoked with "run --stage build-readiness"
        Then it exits with code 1
        And the report says "blocked by  core.workspace.toolchain"
        And the report does not say "blocked by  core.build.configuration"

    # The report is ordered by topological level, and that ordering is what
    # puts the cause on screen before the symptom. A formatter that grouped
    # every failure together first would lose the ordering and leave both lines
    # present, which is why this asserts their order and not their presence.
    Scenario: The cause is printed before the symptom
        Given the workspace needs git "999.0.0" or newer
        When preflight is invoked with "run --stage build-readiness"
        Then "core.workspace.toolchain" is reported before "core.build.compile-probe"

    # "disabled by policy" and "failed, gating" are completely different
    # situations for whoever is reading. One is somebody's decision; the other
    # is a problem.
    Scenario: A dependency disabled by policy says so, rather than reporting a failure
        Given the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage build-readiness --set core.build.configuration:enabled=false"
        Then it exits with code 0
        And the report says "disabled by policy"
        And the report does not say "failed, gating"

    # Disabling a root removes the cost of every rule that existed only to
    # serve it. Taking the closure first and subtracting the disabled after
    # would leave them running and able to fail the run.
    Scenario: Disabling the root of a stage eliminates the whole chain
        Given the workspace needs git "999.0.0" or newer
        When preflight is invoked with "run --stage build-readiness --set core.build.configuration:enabled=false --set core.build.compile-probe:enabled=false"
        Then it exits with code 0
        And the report says "0 rules executed"
