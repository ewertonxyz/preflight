Feature: Incremental cache
    The cache exists because core.build.compile-probe costs fifteen
    seconds and re-runs over sources that did not change. A tool that spends
    fifteen seconds reconfirming a result it already had is working against its
    own purpose.

    What the scenarios below are for is the half that cannot be seen from
    inside the process: that a second invocation of the real binary genuinely
    skips the work, that it says so where a person will read it, and that the
    two escapes from it — the flag and the clear command — actually escape.

    The safe default gets a scenario of its own. A probe that has not declared
    its inputs is never cached, because there is no approximate
    fingerprint and the engine cannot work out what a compiler reads.

    Scenario: A second run over an unchanged workspace reuses the result
        Given a workspace
        And a workspace whose compile probe declares its inputs
        When preflight is invoked with "run --stage build-readiness"
        Then it exits with code 0
        And the report does not say "(cached)"
        And the cache holds 1 result

    Scenario: The reused result says that it was reused
        Given a workspace
        And a workspace whose compile probe declares its inputs
        When preflight is invoked with "run --stage build-readiness"
        And preflight is invoked with "run --stage build-readiness"
        Then it exits with code 0
        And the report says "(cached)"

    Scenario: --no-cache ignores what was stored
        Given a workspace
        And a workspace whose compile probe declares its inputs
        When preflight is invoked with "run --stage build-readiness"
        And preflight is invoked with "run --stage build-readiness --no-cache"
        Then it exits with code 0
        And the report does not say "(cached)"

    Scenario: cache clear empties it
        Given a workspace
        And a workspace whose compile probe declares its inputs
        When preflight is invoked with "run --stage build-readiness"
        And preflight is invoked with "cache clear"
        Then it exits with code 0
        And the cache holds 0 results

    Scenario: A probe that declares nothing is never cached
        Given a workspace
        And a workspace whose compile probe declares nothing
        When preflight is invoked with "run --stage build-readiness"
        Then it exits with code 0
        And the cache holds 0 results
