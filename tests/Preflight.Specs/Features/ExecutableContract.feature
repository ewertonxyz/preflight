Feature: Executable contract
    The exit code of the preflight executable is the only thing a CI pipeline
    actually reads. Each code has a distinct meaning, and the distinction is
    what lets a pipeline call the author of a commit for one failure and the
    owner of the tool for another. Which code stands for which meaning is
    fixed in ExitCodes; what is fixed here is the surface a caller reaches
    before any rule runs.

    The two scenarios below belong together and neither is worth much alone.
    The first says an incomplete invocation is met with the command surface
    rather than an error; on its own it is satisfied by a binary that prints
    something and exits 0 no matter what it was asked. The second is what
    stops that: an unknown command is a configuration error, so a run that
    succeeds is a run that was understood.

    Scenario: Invoked with no arguments, it prints the command surface
        Given a workspace
        When preflight is invoked with ""
        Then it exits with code 0
        And the report names exactly these commands
            | command  |
            | run      |
            | rules    |
            | graph    |
            | create   |
            | pipeline |
            | measure  |
            | report   |
            | cache    |
            | explain  |

    Scenario: An unknown command is a configuration error
        Given a workspace
        When preflight is invoked with "nonsense"
        Then it exits with code 2
