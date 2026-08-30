Feature: Choosing the pipeline without typing the flag
    In a studio running three games on one engine, nobody types `--pipeline`
    correctly every day. The checkout declares which pipeline it is, and the
    flag still wins when somebody needs to validate against another one.

    What does not happen is the tool choosing on its own: several pipelines and
    no choice made is a refusal, not a quiet fall back to the base policy.

    Scenario: The key in the checkout makes the flag unnecessary
        Given a workspace
        And the workspace needs nothing
        And the file "preflight.base.json" contains
            """
            { "schemaVersion": 1, "pipeline": "atlas" }
            """
        And the file "preflight.atlas.json" contains
            """
            { "schemaVersion": 1 }
            """
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        And the report says "atlas"

    Scenario: Several pipelines and no choice made is a refusal
        Given a workspace
        And the workspace needs nothing
        And the file "preflight.base.json" contains
            """
            { "schemaVersion": 1 }
            """
        And the file "preflight.atlas.json" contains
            """
            { "schemaVersion": 1 }
            """
        And the file "preflight.switch2.json" contains
            """
            { "schemaVersion": 1 }
            """
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 2
        And the error output says "atlas"
        And the error output says "switch2"

    Scenario: The flag beats the key in the checkout
        Given a workspace
        And the workspace needs nothing
        And the file "preflight.base.json" contains
            """
            { "schemaVersion": 1, "pipeline": "atlas" }
            """
        And the file "preflight.switch2.json" contains
            """
            { "schemaVersion": 1 }
            """
        When preflight is invoked with "run --stage workspace --pipeline switch2"
        Then it exits with code 0
        And the report says "switch2"
