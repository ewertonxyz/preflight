Feature: Turning Preflight on in a project that never had it
    A new project has no manifest, and the toolchain rule fails when one is
    missing — by decision, because a NotApplicable there would be a trapdoor
    that left the rule green forever in the face of a mistyped path.

    The `create workspace` command is what closes that gap: it writes the
    skeleton the project does not have yet, discovers nothing on its own, and
    refuses to touch a manifest that already exists.

    Scenario: A new workspace starts validating instead of blocking
        Given a workspace
        When preflight is invoked with "create workspace"
        Then it exits with code 0
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        And the report says "n/a"
        And the report does not say "FAIL"

    Scenario: The command refuses to overwrite an existing manifest
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        And the file "preflight.workspace.json" is remembered
        When preflight is invoked with "create workspace"
        Then it exits with code 2
        And the file "preflight.workspace.json" is unchanged
