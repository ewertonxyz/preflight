# Preflight

[![Build](https://github.com/ewertonxyz/preflight/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/ewertonxyz/preflight/actions/workflows/build.yml)
[![Tests](https://github.com/ewertonxyz/preflight/actions/workflows/tests.yml/badge.svg?branch=main)](https://github.com/ewertonxyz/preflight/actions/workflows/tests.yml)
[![Coverage](https://codecov.io/gh/ewertonxyz/preflight/branch/main/graph/badge.svg)](https://codecov.io/gh/ewertonxyz/preflight)

A deterministic build-readiness validation tool for game development pipelines.

Preflight runs a project's pre-build checks in dependency order, attributes a failure to its
root cause rather than listing its symptoms, and keeps the *rule* — the check, written in C#
— separate from the *limit* it enforces, which is configuration. Two projects can run the
identical binary and disagree only in a JSON file.

---

## Why it exists

Three failure modes, in increasing order of cost:

1. **Late feedback.** A build fails after forty minutes on something knowable in twenty
   seconds — the wrong SDK version, a 200 MB source texture committed by accident. Neither
   needed a compiler to detect.
2. **Symptom lists.** Ten errors are reported, nine of which follow from the first. The
   developer picks one and spends the afternoon in the wrong file.
3. **Divergence.** A limit correct for one project is wrong for the next, so somebody copies
   the validation script and edits a number. The two copies drift, and nobody can say which
   version of which rule is running where.

The third is what shapes the design; the first two follow from solving it.

---

## Where it sits

Preflight has two distinct users, and the split between rule and policy exists to separate
them.

**Whoever owns build infrastructure** decides what "ready to build" means for a project:
which checks run, at which limits, on which platforms, and which of them stop a submit. They
write the rules in C#, write the policy in JSON, and publish both as one versioned package.
This is the same level as a project's build scripts, cook settings and CI definitions.

**Everybody else — including a gameplay programmer who has never opened the pipeline
repository** — installs that package and runs one command. They write no rules and cannot
edit the project's policy. What they get back names a file, a number and an action.

The same binary serves both. A rule knows nothing about which project it runs for, and a
project's policy cannot change what a rule does — only whether it runs, with which limit, at
which severity, and whether it blocks.

---

## Concepts

| | |
|---|---|
| **Rule** | The check itself. C#, compiled, versioned, tested, identical everywhere |
| **Policy** | JSON. Whether a rule runs, its limit, its severity, whether it blocks |
| **Pipeline** | Rules and policy published together as one versioned, checksummed package |
| **Stage** | `workspace`, `pre-submit`, `build-readiness` — which question a run is asking |
| **Target** | A platform, optionally with a configuration, that selects a policy layer |
| **Overlay** | An unversioned local file for one machine, ignored inside CI |

A policy layer can **seal** a key so no downstream layer may loosen it. Seals are unioned
along the inheritance chain, so a project cannot silently drop an organisation-wide one.

### One rule, two projects

```jsonc
// projecta.json — ships a packaged Win64 build streaming from a fixed-size bundle
{
  "schemaVersion": 1,
  "pipeline": "projecta",
  "rules": {
    "core.presubmit.large-file": { "settings": { "maxBytes": 5242880 } }
  },
  "targets": {
    "win64": {
      "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 2621440 } } }
    }
  },
  "sealed": ["core.presubmit.large-file:blocking"]
}
```

A second project whose cinematics ship uncompressed source sets the same key to
`209715200` in its own file and states no target at all. Same rule, same assembly, no fork.

---

## Installing

Requires the .NET 10 SDK.

```bash
git clone https://github.com/ewertonxyz/preflight.git && cd preflight
dotnet publish src/Preflight.Cli/Preflight.Cli.csproj -c Release -o ./dist
```

Put `./dist/preflight` on `PATH`.

**Once per repository, by whoever owns it:**

```bash
preflight pipeline install \\my-server\tools\preflight\projecta-1.4.0.zip
preflight pipeline declare projecta
```

`declare` writes a versioned `preflight.base.json` naming the pipeline and the package
version range this checkout accepts. Commit it.

**Once per machine, by everybody else:**

```bash
preflight pipeline install \\my-server\tools\preflight\projecta-1.4.0.zip
```

No pin needed: a run takes the newest installed version the checkout's range allows, and the
run header says which. `preflight pipeline use projecta@1.4.0` pins, for holding a version
still or rolling back. Installing never moves a pin, so one toolchain delivery cannot change
what every machine validates against at once.

---

## The daily command

```bash
preflight run --stage pre-submit --changed-from origin/main --platform win64
```

```text
Preflight — pre-submit — projecta@1.4.0 (pinned) — win64/Development
policy  projecta                                      local overlay not applied

  ✓  core.presubmit.forbidden-paths                      0.0s
  ✗  core.presubmit.large-file                           0.0s
     Changed file exceeds the size limit.
       at  Art/Characters/hero_diffuse.tga
       expected  at most 2,621,440 bytes
       actual    11,400,000 bytes
       fix       Move the file out of version control, or ask the pipeline's author to raise 'maxBytes' for this rule if the size is intended.

  Blocked — 1 failed, 1 passed in 0.0s
```

Every finding carries the same four fields: **where**, **expected**, **actual**, **fix**. The
`fix` is addressed to whoever can act on it — the consumer cannot edit that policy, so
telling them to would be an instruction with no action behind it.

The limit is 2,621,440 because the run named `--platform win64`. Without the flag the same
command reports `at most 5,242,880 bytes`. The platform is never inferred.

| Stage | Question |
|---|---|
| `workspace` | Is this machine set up to build at all? |
| `pre-submit` | Is this change safe to submit? |
| `build-readiness` | Will a full build succeed? |

CI runs all three; a person usually runs one.

---

## Dependency order and root cause

Rules declare dependencies. They run in topological order, in parallel within each level, so
a 15-second compile probe that depends on the toolchain check never starts when the SDK is
missing.

When a gating rule fails, its transitive dependents are skipped, and each reports the
*original* failure rather than the skip in front of it:

```text
  ✗  core.workspace.toolchain                            0.0s
     'ProjectA SDK' is not available.
       expected  'projecta-sdk' on PATH
       fix       Install 'ProjectA SDK' and make sure 'projecta-sdk' is on PATH.
  ⊘  core.build.configuration                         skipped
     blocked by  core.workspace.toolchain   (failed, gating)
  ⊘  core.build.compile-probe                         skipped
     blocked by  core.workspace.toolchain   (failed, gating)

  Blocked — 1 failed, 2 skipped in 0.1s
```

`compile-probe` depends on `configuration`, not on the toolchain, and still names the
toolchain: attribution walks past intermediate skips to the thing somebody has to fix.

Two independent axes govern this. **`blocking`** decides the verdict and the exit code;
**`gating`** decides whether dependents still run. All four combinations are meaningful.

---

## Where a limit came from

```bash
preflight explain core.presubmit.large-file --platform win64
```

```text
Effective policy
  key                  value       origin
  enabled              true        tool default
  blocking             true        RuleDescriptor default
  severity             error       RuleDescriptor default
  settings.maxBytes    2621440     projecta@1.4.0/projecta.json:9   (target win64)
                                   overrides projecta@1.4.0/projecta.json:5 (5242880)
```

Every layer in order, with package, file and line, including the value it replaced.

| Also | |
|---|---|
| `preflight rules` | What is loaded, with the effective policy |
| `preflight graph` | The dependency graph, optionally as Graphviz DOT |
| `preflight measure --label build -- <cmd>` | Times a command and records it, so cost is measured rather than claimed |
| `preflight report --since 30d` | Durations, percentiles, failure rates |
| `preflight pipeline list` | Which pipelines and versions this machine has installed, and which is pinned |
| `preflight cache clear` | Empties the incremental cache |

---

## Authoring a pipeline

A pipeline is a directory holding a policy and, optionally, rule assemblies. Three commands
scaffold what is missing:

```bash
preflight create workspace              # a preflight.workspace.json skeleton
preflight create policy projecta        # a named pipeline's policy skeleton
preflight create rule acme.textures.dimension   # a plugin project for one rule
```

### Writing a rule

A rule is a class implementing `IValidationRule` against `Preflight.Abstractions`, which
depends on the BCL and nothing else. Compile it, and drop the DLL where the tool looks — no
fork, no recompile of Preflight, no registration step.

```csharp
public sealed class TextureDimensionRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = new RuleId("projecta.content.texture-dimension"),
        DisplayName = "Texture dimension",
        Stage = ValidationStage.PreSubmit,
        DefaultSeverity = Severity.Error,
        DefaultBlocking = true,
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var maxDimension = context.Policy.GetValue("maxDimension", 4096);
        // …
    }
}
```

The descriptor carries the rule's *defaults*; the policy overrides them. Everything a rule
may touch — the file system, the change set, the process runner, its own policy — arrives
through `RuleContext`, which is what makes a rule unit-testable without a disk.

Each plugin loads in its own collectible assembly context, so plugins with conflicting
dependencies coexist. A rule id present in two sources is refused with both paths named and
neither winning — load order decides nothing. The contract is versioned, and an incompatible
plugin is refused by name and number rather than failing later at a type load.

### Writing a policy

```jsonc
{
  "schemaVersion": 1,
  "pipeline": "projecta",
  "rules": {
    "projecta.content.texture-dimension": {
      "enabled": true,
      "blocking": true,
      "severity": "error",
      "settings": { "maxDimension": 4096 }
    }
  },
  "targets": {
    "win64|Shipping": {
      "rules": { "projecta.content.texture-dimension": { "settings": { "maxDimension": 2048 } } }
    }
  },
  "sealed": ["projecta.content.texture-dimension:blocking"]
}
```

A target key is `platform` or `platform|configuration`, spelled out — there is no pattern
language, and an axis the run did not state never matches. A `win64|Shipping` block does not
fire on a run that named only `--platform win64`, because the configuration defaulted rather
than being chosen.

### Packing and distributing

```bash
preflight pipeline validate ./projecta-pipeline   # every error at once, before publishing
preflight pipeline pack ./projecta-pipeline -o projecta-1.4.0.zip
```

The archive holds the policy, the rule assemblies, and a manifest with a SHA-256 per file
plus the contract range those assemblies need. Ordinal entry order, fixed timestamps, no
filesystem metadata — the same tree produces the same bytes on any machine, which is what
makes a published checksum meaningful. Installation verifies every digest and the contract
range *before* writing a byte, and keeps the last ten versions for rollback.

Preflight fetches nothing itself, so the delivery channel is yours: an existing toolchain
mechanism, an internal artifact feed, a NuGet package, a release attachment, or a network
share. All of them work today, unchanged.

---

## Reporting rules

A validation tool that reports success without having checked is worse than no tool, because
its green is counted as evidence. The following are enforced rather than intended:

| | |
|---|---|
| `n/a` is not `passed` | A rule that found nothing to look at says so. A tick would claim a check that never happened |
| A missing manifest fails, it does not skip | Otherwise a typo in a path leaves a rule permanently green |
| A cached result says `(cached)` | In the console and in the JSON, always |
| A crash is not a failure | `Errored` and `Failed` are never aggregated: one blames the tool, the other blames the workspace |
| Unsupportable percentiles are not printed | A p50 needs 5 observations, a p95 needs 50. Below that: a dash, and what it would need |
| Refusal over assumption | Every ambiguity is a refusal naming what would have worked, never a default nobody chose |

```text
Preflight duration     p50  0.0s    p95  —       (n=8; p95 needs n>=50)
Build duration         p50  —       p95  —       (n=1, measured; p50 needs n>=5)
```

| Exit | Meaning | Who is called |
|---|---|---|
| `0` | Passed, possibly with warnings | Nobody |
| `1` | Blocked | The author of the commit |
| `2` | Broken configuration — invalid policy, a cycle, a plugin that would not load | The owner of the tool |
| `3` | Internal error, or a rule that crashed | The owner of the tool |

**Deterministic and local.** Ordered output, invariant culture, no ambient clock or
randomness on any decision path; two runs of one commit produce byte-identical JSON. Nothing
is downloaded: a run whose result depends on what time it is stops being reproducible, and an
automatic update executes new assemblies on a hundred machines without anybody approving
them.

Output formats: console, JSON, and SARIF 2.1.0 — `run --format sarif` feeds a code review
pipeline with no integration work.

---

## Cost

Per-run overhead is budgeted at 500 ms — runtime startup, plugin discovery, policy load — and
plugin discovery is the only part that grows with rule count, at roughly 10–30 ms per
assembly. Rules run in parallel within a graph level, bounded by policy and defaulting to
processor count. History is append-only NDJSON with a per-process mode for build farms.

Caching is off unless asked for, and a check is cached only if it can describe its inputs
exactly. The cache key includes a digest of the rule's effective policy, so changing a limit
invalidates the result rather than serving a pass from the old one, and the identity of the
rule's own assembly, so a rebuilt plugin is never handed its predecessor's verdict.

---

## Building and verifying

```powershell
.\scripts\verify.ps1
```

Restore, format check, build under warnings-as-errors, the whole suite, then coverage,
stopping at the first failure.

The suite is **1769 tests**: unit, contract, exact-console-bytes, and Gherkin scenarios
driving the published executable.

Coverage is collected per test project with `coverlet.console` and rendered with
`ReportGenerator`, both pinned in `.config/dotnet-tools.json`. The four test projects are
measured separately and the reports merged, because no single one exercises the whole tree:
the CLI tests reach the command surface, the Gherkin specs drive the real executable, and the
rule tests see only what an external plugin sees. Test assemblies, the test support library
and the sample plugin are excluded at collection.

The script fails if it measured zero assemblies. A coverage run reporting success having
measured nothing is the same defect this tool exists to prevent, aimed at its own
instrumentation.

The suite holds **100% line, branch and method coverage** — 5,404 lines, 1,784 branches and
616 methods, all covered. That is a floor the reports are checked against, not an aspiration:
where a branch is genuinely unreachable it is either removed, folded into a real case, or
excluded with the reason written at the exclusion. It is never covered by a fabricated test.

The badge at the top is the number Codecov computes from those same four reports.

---

## Layout

```text
src/
  Preflight.Abstractions   The plugin contract — Rules/, Services/, Model/. BCL only.
  Preflight.Core           Graph, execution, policy, history, cache, plugin loading.
  Preflight.Rules          The six built-in rules.
  Preflight.Cli            Argument parsing, reporters, exit codes, packaging.
tests/                     Core, Rules, Cli, Specs (Gherkin), TestSupport.
samples/                   A worked plugin: one rule, one project reference.
fixtures/                  Workspaces, good and broken, the tests run against.
```

`Preflight.Rules` referencing `Preflight.Core` would make the plugin model fiction, so a test
enforces the boundary. The built-in rules see exactly what an external plugin sees.

In both `Preflight.Core` and `Preflight.Cli`, a folder answers *what a file is* rather than which
command or subsystem it serves, because a type is reached from the outside by what it does and not
by who asked for it. Every folder is a namespace, and every type has a file named after it.

```text
Preflight.Core/
  Policy/       Parsing, validating and merging policy into one effective answer.
  Graph/        The rule dependency graph, and which rules a stage actually runs.
  Execution/    Running the selected rules, and surviving whatever they do.
  Caching/      The incremental cache, from the core's side.
  History/      The append-only run record, and the report computed over it.
  Plugins/      Turning assemblies on disk into rules, or refusing to.
  Changes/      The changed-file list, asked of git.
```

One file stays at the root: the base every load-time refusal derives from, which is what lets the
exit-code mapping have a single `catch`.

```text
Preflight.Cli/
  Model/        The vocabulary — exit codes, run options, output formats.
  Parsing/      Strings to values: --set overrides, --since windows, stages.
  Policy/       Resolving the effective policy, and the local overlay decision.
  Pipelines/    Package identity — names, versions, manifests, selection.
  Storage/      What touches the outside world — disk, archives, environment, child processes.
  Services/     The interfaces Storage/ implements, so a command is testable without any of it.
  Commands/     A handler per command, plus the work it orchestrates and the options it takes.
  Reporting/    The renderers — console, JSON, SARIF and Graphviz DOT.
  Interactive/  The pipeline picker, and the refusal when there is no terminal to ask.
```

Four files stay at the root: the entry point, the command surface, the dispatcher that reads
it back, and the one place their shared option names are spelled.

| Built-in rule | Stage | Depends on |
|---|---|---|
| `core.workspace.toolchain` | workspace | — |
| `core.workspace.dependencies` | workspace | toolchain |
| `core.presubmit.forbidden-paths` | pre-submit | — |
| `core.presubmit.large-file` | pre-submit | — |
| `core.build.configuration` | build-readiness | toolchain |
| `core.build.compile-probe` | build-readiness | configuration |

Six is deliberate. The set demonstrates the model rather than trying to be exhaustive — the
interesting rules for any given project are the ones that project writes.

---

## Scope

| Does not | Why |
|---|---|
| Download anything | It validates what is on disk, which is what keeps two runs of one commit in agreement |
| Fix anything | It reports what to do; it does not edit the workspace |
| Reimplement static analysis | It emits SARIF into the pipeline that already reads it |
| Replace the build system | Compiling remains the build system's job |
| Guess | Every ambiguity is a refusal that names what would have worked |

The command line is one host over a library, and the report is computed as data before it is
rendered. An IDE extension or an in-process build-farm host would be another renderer rather
than a screen scrape; a test enforces that the library never reaches back into the command
line.

---

## Status

| | |
|---|---|
| Version | 0.1.1 — below 1.0 deliberately; nothing is published under a stability promise |
| Target | .NET 10, nullable enabled, warnings as errors, analysers at `latest-recommended` |
| Platform | Windows today; nothing in the design is Windows-specific |

The tool works end to end. What is happening now is a review, subsystem by subsystem, each on
its own branch with the full suite green before it lands. Every subsystem has been through it:
the plugin contract, the built-in rules, the command line, and the core — graph, execution,
policy, history and cache — along with the test suites that hold them.

---

## Licence

MIT.
