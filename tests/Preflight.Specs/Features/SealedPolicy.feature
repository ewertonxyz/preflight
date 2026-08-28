Feature: Travas que um projeto não pode afrouxar
    Um estúdio com três jogos precisa de regras que valem para todos. Sem trava,
    nada impedia um projeto de escrever `"blocking": false` numa regra exigida
    pelo estúdio: a run ficava verde tendo verificado menos do que a política
    pedia, e ninguém era avisado.

    O selo fecha isso. E fecha só o que foi nomeado — desabilitar uma regra que
    ninguém selou continua sendo uso previsto.

    Scenario: Um selo do baseline recusa o afrouxamento local
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

    Scenario: O selo vale para a linha de comando
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

    Scenario: Desabilitar uma regra não selada continua sendo uso previsto
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
