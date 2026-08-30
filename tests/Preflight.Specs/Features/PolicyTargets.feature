Feature: Per-platform limits in one policy file
    A game shipping on PS5 and on Switch 2 does not have the same budget on
    both. Before this, the only way out was one policy file per platform and
    configuration pair — seven files for three projects, repeating 90% of each
    other.

    The `targets` block puts the difference beside what is common, and a
    platform no block mentions goes on being the ordinary case rather than an
    error.

    Scenario: The same rule fails on one platform and passes on another
        Given a workspace
        And the workspace needs nothing
        And the file "preflight.base.json" contains
            """
            {
              "schemaVersion": 1,
              "rules": {
                "core.build.configuration": { "enabled": false },
                "core.build.compile-probe": { "enabled": false },
                "core.workspace.toolchain": { "blocking": true }
              },
              "targets": {
                "switch2": {
                  "rules": { "core.workspace.toolchain": { "enabled": false } }
                }
              }
            }
            """
        When preflight is invoked with "run --stage workspace --platform switch2"
        Then it exits with code 0
        And the report says "disabled by policy"

    Scenario: A platform no block mentions is not an error
        Given a workspace
        And the workspace needs nothing
        And the file "preflight.base.json" contains
            """
            {
              "schemaVersion": 1,
              "targets": {
                "switch2": {
                  "rules": { "core.workspace.toolchain": { "enabled": false } }
                }
              }
            }
            """
        When preflight is invoked with "run --stage workspace --platform ps5"
        Then it exits with code 0
        And the report does not say "disabled by policy"

    Scenario: A target key nobody can parse is a refusal at load time
        Given a workspace
        And the workspace needs nothing
        And the file "preflight.base.json" contains
            """
            {
              "schemaVersion": 1,
              "targets": { "win64|A|B": { "rules": {} } }
            }
            """
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 2
        And the error output says "win64|A|B"
