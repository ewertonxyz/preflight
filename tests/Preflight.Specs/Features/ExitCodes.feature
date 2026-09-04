Feature: Exit codes
    Each exit code has a distinct meaning, and the
    distinction is the tool's contract with a pipeline: 2 calls the owner of the
    tool, 1 calls the author of the commit. A defect that returns the wrong one
    does not look like a defect — it routes an incident to the wrong person,
    quietly, every time.

    Every scenario here shapes the six real rules through policy rather than
    through fakes. A fake rule proves the engine, and the engine already has
    unit tests; what has no other test is whether the shipped rules, the policy
    chain and the exit codes agree with each other when a real process runs
    them.

    Scenario: A workspace that satisfies its rules exits zero
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        And the report says "Passed"

    Scenario: A blocking failure exits one
        Given a workspace
        And the workspace needs git "999.0.0" or newer
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 1
        And the report says "Blocked"

    Scenario: A warning alone exits zero
        Given a workspace
        And the workspace declares a dependency that was never restored
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        And the report says "Passed with warnings"

    # --fail-on-warning is applied after aggregation, as a last
    # transformation. The same run, one flag apart, is what makes that visible.
    Scenario: The same warning under --fail-on-warning exits one
        Given a workspace
        And the workspace declares a dependency that was never restored
        When preflight is invoked with "run --stage workspace --fail-on-warning"
        Then it exits with code 1
        And the report says "Blocked"

    Scenario: An invalid policy exits two
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        And the file "preflight.base.json" contains
            """
            {
              "schemaVersion": 1,
              "rules": { "core.workspace.toolchain": { "blockng": false } }
            }
            """
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 2

    # The exit code a build pipeline is most likely to misread. System.CommandLine
    # returns 1 for a parse failure, and 1 means "code was rejected" — so a typo
    # in a CI yaml would page the author of the commit.
    Scenario: A misspelled stage exits two and not one
        Given a workspace
        When preflight is invoked with "run --stage workspce"
        Then it exits with code 2

    # The same trap, on the flag the reporters constrain. AcceptOnlyFromAmong on
    # --format is another parse-failure path, and System.CommandLine returns 1 by
    # default for every one of them — so the override that maps a parse failure
    # to 2 has to cover this one too. A capital letter is the misspelling
    # somebody actually types, and before the override it produced a console
    # screen and exit 0 for a caller that had asked for a machine.
    Scenario: A misspelled format exits two and not one
        Given a workspace
        When preflight is invoked with "run --stage workspace --format Json"
        Then it exits with code 2

    # A rule that throws is Errored, which is a defect in the rule
    # and not in the workspace. A settings value of the wrong type is the
    # cheapest way to provoke one, because the schema leaves settings uninspected
    # on purpose — the validator cannot catch it at load time.
    Scenario: A rule that errors exits three
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        When preflight is invoked with "run --stage workspace --set core.workspace.dependencies:settings.manifestPath=42"
        Then it exits with code 3
        And the report says "Errored"

    Scenario: An inspection command never exits one
        Given a workspace
        And the workspace needs git "999.0.0" or newer
        When preflight is invoked with "rules"
        Then it exits with code 0
