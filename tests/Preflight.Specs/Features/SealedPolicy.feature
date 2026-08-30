Feature: What a project may not loosen
    A studio with three games needs rules that hold for all of them. Without a
    seal, nothing stopped a project writing `"blocking": false` on a rule the
    studio requires: the run went green having checked less than the policy
    asked for, and nobody was told.

    The seal closes that. And it closes only what was named — disabling a rule
    nobody sealed goes on being intended use.

    Scenario: A seal in the baseline refuses the local loosening
        Given a workspace
        And the workspace needs nothing
        And the file "preflight.studio.json" contains
            """
            {
              "schemaVersion": 1,
              "sealed": ["core.workspace.toolchain:blocking"],
              "rules": { "core.workspace.toolchain": { "blocking": true } }
            }
            """
        And the file "preflight.base.json" contains
            """
            {
              "schemaVersion": 1,
              "extends": "preflight.studio.json",
              "rules": { "core.workspace.toolchain": { "blocking": false } }
            }
            """
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 2
        And the error output says "core.workspace.toolchain:blocking"
        And the error output says "preflight.studio.json"

    Scenario: The seal holds against the command line
        Given a workspace
        And the workspace needs nothing
        And the file "preflight.studio.json" contains
            """
            {
              "schemaVersion": 1,
              "sealed": ["core.workspace.toolchain:blocking"],
              "rules": { "core.workspace.toolchain": { "blocking": true } }
            }
            """
        And the file "preflight.base.json" contains
            """
            { "schemaVersion": 1, "extends": "preflight.studio.json" }
            """
        When preflight is invoked with "run --stage workspace --set core.workspace.toolchain:blocking=false"
        Then it exits with code 2
        And the error output says "--set"

    Scenario: Disabling an unsealed rule goes on being intended use
        Given a workspace
        And the workspace needs nothing
        And the file "preflight.studio.json" contains
            """
            {
              "schemaVersion": 1,
              "sealed": ["core.workspace.toolchain:blocking"]
            }
            """
        And the file "preflight.base.json" contains
            """
            {
              "schemaVersion": 1,
              "extends": "preflight.studio.json",
              "rules": { "core.workspace.toolchain": { "enabled": false } }
            }
            """
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        And the error output does not say "sealed"
