Feature: Escolher o pipeline sem digitar o flag
    Num estúdio com três jogos sobre a mesma engine, ninguém digita `--pipeline`
    corretamente todo dia. O checkout declara qual pipeline ele é, e o flag
    continua vencendo quando alguém precisa validar contra outro.

    O que não acontece é a ferramenta escolher sozinha: vários pipelines e nenhuma
    escolha é recusa, não queda silenciosa para a política base.

    Scenario: A chave do checkout dispensa o flag
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

    Scenario: Vários pipelines e nenhuma escolha é recusa
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

    Scenario: O flag vence a chave do checkout
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
