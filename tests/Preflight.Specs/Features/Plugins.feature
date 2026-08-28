Feature: Plugins
    A production writes its own rules, builds them into an assembly, and points
    preflight at the directory holding it. What happens when that goes right,
    and at more length what happens when it goes wrong, is fixed here.

    The scenarios below are about the contract a process has, which is what a
    build agent sees: an exit code and two output streams. Everything about how
    the loader reaches its decisions is asserted in the unit and integration
    layers; what none of them can show is whether a real preflight.dll, started
    by a real shell, honours those decisions.

    The last scenario is the one worth reading twice. It asserts the absence of
    a message rather than its presence, and that absence is the whole reason
    plugins are loaded before policy is validated: a broken plugin must
    be reported as a broken plugin, never as a typo in a policy file that is
    perfectly correct.

    Scenario: A plugin adds its rule to the tool
        Given a workspace
        And a plugin directory holding the sample rule
        When preflight is invoked with "rules" and the plugin directories
        Then it exits with code 0
        And the report says "atlas.content.texture-dimension"

    Scenario: Without the plugin directory, that rule does not exist
        Given a workspace
        When preflight is invoked with "rules"
        Then it exits with code 0
        And the report does not say "atlas.content.texture-dimension"

    Scenario: A plugin that will not load aborts the run
        Given a workspace
        And a plugin directory holding a corrupt assembly
        When preflight is invoked with "run --stage workspace" and the plugin directories
        Then it exits with code 2
        And the error output says "Broken.Rules.dll"

    Scenario: Two directories claiming one rule id are a configuration error
        Given a workspace
        And a plugin directory holding the sample rule
        And a second plugin directory holding the sample rule
        When preflight is invoked with "rules" and the plugin directories
        Then it exits with code 2
        And the error output says "atlas.content.texture-dimension"

    Scenario: A broken plugin is reported as a broken plugin, not as a typo
        Given a workspace
        And a plugin directory holding a corrupt assembly
        And the file "preflight.base.json" contains
            """
            {
              "schemaVersion": 1,
              "rules": { "atlas.content.texture-dimension": { "enabled": true } }
            }
            """
        When preflight is invoked with "rules" and the plugin directories
        Then it exits with code 2
        And the error output says "Broken.Rules.dll"
        And the error output does not say "unknown rule id"
