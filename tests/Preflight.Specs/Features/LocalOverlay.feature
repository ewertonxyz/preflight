Feature: The local overlay and CI
    `preflight.local.json` is unversioned and exists so a
    developer can loosen a rule while investigating something. It is also an
    obvious integrity hole: nothing stops a `"blocking": false` from surviving in
    it and being submitted that way.

    The alternative — trusting nobody to forget — is the kind of
    thing that works until gold week. So every scenario here asserts the effect
    on the verdict rather than whether a file was read: a run where the overlay
    was resolved correctly and never merged would satisfy the second and fail
    the first.

    Background:
        Given a workspace
        And the workspace needs git "999.0.0" or newer
        And the file "preflight.local.json" contains
            """
            {
              "schemaVersion": 1,
              "rules": { "core.workspace.toolchain": { "blocking": false } }
            }
            """

    Scenario: Outside CI the overlay applies, and the header says so
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        And the report says "local overlay active"

    Scenario: Inside CI the overlay is ignored
        Given the environment variable "TEAMCITY_VERSION" is "2026.1"
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 1
        And the report says "CI detected: TEAMCITY_VERSION"

    Scenario: --no-local ignores the overlay outside CI too
        When preflight is invoked with "run --stage workspace --no-local"
        Then it exits with code 1
        And the report says "--no-local"

    # This flag has one purpose: debugging CI locally. It is the only case where
    # the overlay survives an automation server.
    Scenario: --allow-local applies the overlay even inside CI
        Given the environment variable "CI" is "true"
        When preflight is invoked with "run --stage workspace --allow-local"
        Then it exits with code 0
        And the report says "local overlay active"

    # Detection is "the variable is present and non-empty", which makes
    # CI=false mean CI. It reads backwards, and it is pinned here because the
    # next reader will assume otherwise and "fix" it.
    Scenario: CI set to the string false is still CI
        Given the environment variable "CI" is "false"
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 1
        And the report says "CI detected: CI"
