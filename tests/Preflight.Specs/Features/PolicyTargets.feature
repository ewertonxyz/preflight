Feature: Limites por plataforma no mesmo arquivo de política
    Um jogo que sai para PS5 e para Switch 2 não tem o mesmo orçamento nas duas.
    Antes disso a única saída era um arquivo de política por combinação de
    plataforma e configuração — sete arquivos para três projetos, repetindo 90%
    um do outro.

    O bloco `targets` põe a diferença ao lado do que é comum, e uma plataforma
    que nenhum bloco menciona continua sendo o caso normal, não um erro.

    Scenario: A mesma regra reprova numa plataforma e passa noutra
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

    Scenario: Uma plataforma que nenhum bloco menciona não é erro
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

    Scenario: Uma chave de alvo que ninguém consegue parsear é recusa no carregamento
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
