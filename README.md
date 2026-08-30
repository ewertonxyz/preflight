# Preflight

**A deterministic build-readiness validation tool for game development pipelines.**

Preflight runs a project's pre-build checks in dependency order, reports the root cause
instead of the ten symptoms that follow from it, and keeps the *rule* separate from the
*limit* it enforces — so ProjectA and ProjectB run the identical binary and disagree only
in a JSON file.

One executable. One rule set, written once in C#. Different limits per project, no fork.

---

## The problem

**A build that fails after forty minutes tells you something that was knowable in twenty
seconds.** The SDK was the wrong version. A 200 MB source texture was committed by
accident. None of that needed a compiler.

**Worse: ten errors, nine of which are consequences of the first.** The developer picks
one at random and spends the afternoon in the wrong file.

**Worst: the fork.** A limit that is right for ProjectA is wrong for ProjectB, so somebody
copies the script and changes one number. From then on the two drift, and nobody can say
which rule is actually running anywhere.

Preflight exists for the third problem. The first two are what it does on the way.

---

## The idea: a rule is code, a limit is configuration

| | Rule | Policy |
|---|---|---|
| What it is | The check itself | Whether it runs, with which limit, at which severity, and whether it blocks |
| Lives in | C#, compiled — versioned, tested, identical everywhere | A JSON file |
| Changed by | A release of the rule package | An edit |

**The pipeline author** writes the rules and the policy, and
publishes both as one versioned package.

**The consumer** installs that package and runs one command. They do
not write rules or edit the project's policy. Their one escape hatch is an unversioned
local overlay for their own machine, suppressed automatically inside CI so nobody's
override can make a build agent pass.

A company baseline can **seal** a key so no downstream layer may loosen it. Seals are
unioned along the inheritance chain, so a project cannot quietly drop the company.

---

## One tool, two projects

ProjectA ships a packaged Win64 build streaming from a fixed-size bundle. ProjectB is
cinematic-heavy on the same engine. Same rule, same DLL.

```jsonc
// ProjectA
{
  "schemaVersion": 1,
  "pipeline": "projecta",
  "rules": {
    "core.presubmit.large-file": { "settings": { "maxBytes": 5242880 } }
  },
  "targets": {
    // The shipping target streams from a fixed-size bundle. Half the budget.
    "win64": {
      "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 2621440 } } }
    }
  },
  "sealed": ["core.presubmit.large-file:blocking"]
}
```

```jsonc
// ProjectB — cinematics ship uncompressed source; 5 MB would block every submit.
{
  "schemaVersion": 1,
  "pipeline": "projectb",
  "rules": {
    "core.presubmit.large-file": { "settings": { "maxBytes": 209715200 } }
  }
}
```

---

## Getting started

**Build it.** .NET 10 SDK, then:

```bash
git clone https://github.com/ewertonxyz/preflight.git && cd preflight
dotnet publish src/Preflight.Cli/Preflight.Cli.csproj -c Release -o ./dist
```

`./dist/preflight` goes on `PATH`.

**Set the repository up — once, by whoever owns it:**

```bash
preflight pipeline install \\my-server\tools\preflight\projecta-1.4.0.zip
preflight pipeline declare projecta
```

`declare` writes a versioned `preflight.base.json` naming the pipeline and the package
version range this checkout accepts. Commit it.

**Everybody else joining ProjectA — day one, complete:**

```bash
preflight pipeline install \\my-server\tools\preflight\projecta-1.4.0.zip
```

No pin needed: a run takes the newest installed version the checkout's range allows, and
the header says so. `preflight pipeline use projecta@1.4.0` pins, for holding a version
still or rolling back. Installing never moves the pin — one toolchain delivery would
otherwise change what every machine validates against at once.

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

Every finding carries the same four fields — **where**, **expected**, **actual**, **fix**.
A rule that fails without all four sends somebody to ask a colleague. Note who the fix
names: the consumer cannot edit that policy, so telling them to would be a dead end
wearing the clothes of an instruction.

That limit is 2,621,440 because the run named `--platform win64`. Drop the flag and the
same command reports `at most 5,242,880 bytes` — one file, two budgets, no fork. The
platform is never inferred.

A clean run is four lines and exit 0:

```text
  ✓  core.workspace.toolchain                            0.1s
  ✓  core.workspace.dependencies                         0.0s

  Passed — 2 passed in 0.1s
```

| Stage | Question it answers |
|---|---|
| `workspace` | Is this machine set up to build at all? |
| `pre-submit` | Is this change safe to submit? |
| `build-readiness` | Will a full build succeed? |

CI runs all three; a person usually runs one.

---

## Root cause, not symptoms

Rules declare dependencies. The tool runs them in topological order, in parallel within
each level. The 15-second compile probe depends on the toolchain check, so a missing SDK
means the probe never starts.

When a gating rule fails, its transitive dependents are skipped — and each reports the
*original* failure, walking back through the intermediate skips:

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

`compile-probe` depends on `configuration`, not on the toolchain — and it still names the
toolchain. The attribution walks past the intermediate skip to the thing somebody has to
fix.

Two independent axes govern this: **`blocking`** decides the verdict and the exit code;
**`gating`** decides whether dependents still run. All four combinations are useful.

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

Every layer, in order, with the package, file and line — including which value it
replaced. That is the answer to "why is my asset rejected at *that* number", in one
command instead of a conversation.

| Also | |
|---|---|
| `preflight measure --label build -- <cmd>` | Times a command and records it, so cost is measured rather than claimed |
| `preflight report --since 30d` | Durations, percentiles, failure rates |
| `preflight rules` · `preflight graph` | What is loaded, and the dependency graph (also Graphviz DOT) |

---

## Honesty, which is the actual product

A validation tool that reports success without having checked is worse than no tool,
because its green is counted as evidence.

| | |
|---|---|
| **`n/a` is not `passed`** | A rule that found nothing to look at says so. A tick would claim a check that never happened |
| **A missing manifest fails, it does not skip** | Otherwise a typo in a path leaves a rule permanently green |
| **A cached result says `(cached)`** | In the console and in the JSON. Always |
| **A crash is not a failure** | `Errored` and `Failed` are never aggregated: one blames the tool, the other blames the workspace |
| **Unsupportable percentiles are not printed** | A p50 needs 5 observations, a p95 needs 50. Below that: a dash, and what it would need |
| **Refusal over assumption** | Every ambiguity is a refusal naming what would have worked, never a default nobody chose |

The percentile rule is visible rather than claimed:

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
randomness on any decision path; two runs of one commit produce byte-identical JSON. The
tool downloads nothing — a run whose result depends on what time it is stops being
reproducible, and an automatic update executes new assemblies on a hundred machines
without anybody approving them.

---

## Extending it

A project writes rules against a small contract assembly, compiles, and drops the DLL
where the tool looks. No fork, no recompile of the tool, no registration step.

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

The rule knows nothing about which project it runs for. ProjectB sets
`maxDimension: 8192` and uses the identical binary.
`preflight create rule <id>` scaffolds the project correctly.

Everything a rule may touch arrives through `RuleContext`, which is what makes rules
unit-testable without a disk. Each plugin loads in its own collectible assembly context,
so plugins with conflicting dependencies coexist. A rule id colliding between two sources
is refused with both paths named and neither winning — load order decides nothing. The
contract is versioned and the loader refuses an incompatible plugin by name and number,
rather than failing later at a type load far from the cause.

---

## Distribution

A pipeline ships as a deterministic archive holding the policy, the rule assemblies, and a
manifest with a SHA-256 per file plus the contract range those assemblies need.

```bash
preflight pipeline validate ./projecta-pipeline   # every error at once, before publishing
preflight pipeline pack ./projecta-pipeline -o projecta-1.4.0.zip
```

Ordinal entry order, fixed timestamps, no filesystem metadata — the same tree produces the
same bytes on any machine, which is what makes a published checksum mean something.
Installation verifies every digest and the contract range *before* writing a byte, and
keeps the last ten versions for rollback.

Because the tool fetches nothing itself, the channel is yours — all of these work today,
unchanged:

| Channel | How |
|---|---|
| **Company toolchain** | The mechanism that updates the environment code drops the package; CI installs it |
| **Internal artifact feed / NuGet** | Publish the archive; a one-line install step fetches it |
| **GitHub / GitLab releases** | Attach the archive to a tag; CI downloads and installs |
| **A network share** | `preflight pipeline install \\my-server\tools\...` |

---

## Where it can go

The tool is a library. The command line is one host over it, built first because it is
the host that proves the tool works.

| Surface | What it would take |
|---|---|
| **A button in Visual Studio or Rider** | An extension running the same executable and rendering the JSON. The report is computed as data and rendered separately, so a second renderer is a renderer — not a scrape of a screen |
| **A local web UI** | A page that detects whether Preflight is installed on the machine, runs it, and shows the graph and findings live |
| **Code review integration** | Already there: `run --format sarif` emits SARIF 2.1.0, which review pipelines read with no integration work |
| **A build-farm service** | The tool is hostable in-process. A test enforces that it never reaches back into the command line, which is what keeps that path open |

---

## Scale

| | |
|---|---|
| Per-run overhead | Budgeted at 500 ms: runtime startup, plugin discovery, policy load |
| Parallelism | Bounded by policy, defaulting to processor count, one graph level at a time |
| History | NDJSON, append-only, with a per-process mode for build farms. The read path breaks first, near 10⁵ runs/day |
| Caching | Off unless asked for. A check is cached only if it can describe its inputs exactly; anything that cannot declines |
| Growth | Plugin discovery is the only part of startup that grows with rule count, ~10–30 ms per assembly |

The cache key includes a digest of the rule's effective policy, so changing a limit
invalidates the result rather than serving a pass from the old one — and the identity of
the rule's own assembly, so a rebuilt plugin is never handed its predecessor's verdict.

---

## State of the code

| | |
|---|---|
| Version | 0.1.1 — below 1.0 deliberately; nothing is published under a stability promise yet |
| Target | .NET 10, nullable enabled, warnings as errors, analysers at `latest-recommended` |
| Tests | **1769** — unit, contract, exact-console-bytes, and Gherkin scenarios driving the real executable |
| Coverage | **100% of lines, branches and methods** |
| Platform | Windows today; nothing in the design is Windows-specific |

**The review phase, which is where the project is now.** The goal until recently was a
tool that works end to end, and it does. What is happening now is a deliberate review,
subsystem by subsystem, each on its own branch with the full suite green before it lands.

The code is not badly implemented — most of it holds up. But it was written to reach
working software, and parts will be better under a pass aimed at single responsibility
where a type changes for two reasons, composition where a hierarchy exists only to share
code, files grouped by what they are rather than left at a project root, and names that
need no comment. The plugin contract has been through it; the remaining subsystems have
not.

---

## Building and verifying

```powershell
.\scripts\verify.ps1
```

Restore, format check, build under warnings-as-errors, the whole suite, then coverage —
stopping at the first failure. The coverage step fails explicitly if it measures zero
assemblies: a coverage run reporting success having measured nothing is the same defect
this tool exists to prevent, aimed at its own instrumentation.

---

## Layout

```text
src/
  Preflight.Abstractions   The plugin contract — Rules/, Services/, Model/. BCL only.
  Preflight.Core           The engine: graph, execution, policy, history, cache, plugins.
  Preflight.Rules          The six built-in rules.
  Preflight.Cli            Argument parsing, reporters, exit codes, packaging.
tests/                     Core, Rules, Cli, Specs (Gherkin), TestSupport.
samples/                   A worked plugin: one rule, one project reference.
fixtures/                  Workspaces, good and broken, the tests run against.
```

`Preflight.Rules` referencing `Preflight.Core` would make the plugin model fiction, so a
test enforces the boundary. The built-in rules see exactly what an external plugin sees.

| Built-in rule | Stage | Depends on |
|---|---|---|
| `core.workspace.toolchain` | workspace | — |
| `core.workspace.dependencies` | workspace | toolchain |
| `core.presubmit.forbidden-paths` | pre-submit | — |
| `core.presubmit.large-file` | pre-submit | — |
| `core.build.configuration` | build-readiness | toolchain |
| `core.build.compile-probe` | build-readiness | configuration |

Six is deliberate: a rule ships only if it came from a real incident. The set demonstrates
the model rather than trying to be exhaustive — the interesting rules for any company are
the ones that company writes.

---

## What it deliberately does not do

| | |
|---|---|
| Download anything | It validates what is on disk. That is what keeps two runs of one commit in agreement |
| Fix anything | It reports what to do; it does not edit the workspace |
| Reimplement static analysis | It emits SARIF into the pipeline that already reads it |
| Replace the build system | Compiling is still the build system's job |
| Guess | Every ambiguity is a refusal that names what would have worked |

---

## Licence

MIT.
