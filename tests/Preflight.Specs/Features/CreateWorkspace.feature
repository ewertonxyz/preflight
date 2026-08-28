Feature: Ligar o Preflight num projeto que nunca o teve
    Um projeto novo não tem manifesto, e a regra de toolchain reprova quando ele
    falta — por decisão, porque um NotApplicable ali seria um alçapão que deixaria
    a regra verde para sempre diante de um caminho digitado errado.

    O comando `create workspace` é o que fecha essa lacuna: ele escreve o
    esqueleto que o projeto ainda não tem, sem descobrir nada sozinho, e recusa
    tocar num manifesto que já exista.

    Scenario: Um workspace novo passa a validar em vez de bloquear
        Given a workspace
        When preflight is invoked with "create workspace"
        Then it exits with code 0
        When preflight is invoked with "run --stage workspace"
        Then it exits with code 0
        And the report says "n/a"
        And the report does not say "FAIL"

    Scenario: O comando recusa sobrescrever um manifesto existente
        Given a workspace
        And the workspace needs git "2.0.0" or newer
        And the file "preflight.workspace.json" is remembered
        When preflight is invoked with "create workspace"
        Then it exits with code 2
        And the file "preflight.workspace.json" is unchanged
