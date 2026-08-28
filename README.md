# Preflight

A deterministic build-readiness validation engine for game development pipelines.

One executable. One set of rules, written once. Different limits per project, without a fork.

---

## The problem

A build that fails after forty minutes tells you something that was knowable in twenty
seconds. The SDK was the wrong version. The platform configuration had a hole in it. A
200 MB source texture had been committed by accident. None of that needed a compiler.
It needed somebody to look, in the right order, and to stop looking once the answer was
obvious.

The second problem is worse. When a check does run early and fails, it usually fails
alongside nine others that were only ever going to fail because of it. The developer is
handed ten errors, picks one at random, and spends the afternoon in the wrong file. Ten
symptoms are less useful than one cause.

The third problem is what happens when a team tries to fix the first two. A check that is
right for ProjectA is wrong for ProjectB. The 5 MB file limit that protects one game
blocks the other game's cinematic pipeline. The usual outcome is a fork, or a branch, or
a copy of the script with one number changed — and from then on the two versions drift,
and nobody can say which rule is actually running anywhere.

Preflight exists for the third problem. The first two are what it does on the way.

---

## The idea: a rule is code, a limit is configuration

| | Rule | Policy |
|---|---|---|
| What it is | The check itself | Whether that check runs, with which limit, at which severity, and whether it blocks |
| Lives in | Code — versioned, tested, identical everywhere | JSON |
| Written by | The tools team, once | The tools team, per project |
| Changed by | A release | An edit |

The same compiled assembly runs against ProjectA and ProjectB. What differs is a JSON
file. That is the whole design, and everything below is a consequence of it.

### Two roles, and the line between them

**The pipeline author** — a tools programmer — writes the rules in C# and the policy that
configures them, and publishes both as a versioned package.

**The consumer** — everybody else on ProjectA or ProjectB — installs that package and
runs one command. They do not write rules. They do not edit the project's policy. They
have one escape hatch, an unversioned local overlay for their own machine, which is
suppressed automatically inside CI so that nobody's personal override can make a build
agent pass.

That line shows up in the error messages too. When a check fails on a limit the consumer
cannot change, the remediation says who can:

```text
fix       Move the file out of version control, or ask the pipeline's author to raise
          'maxBytes' for this rule if the size is intended.
```

Telling somebody to edit a policy they have no write access to is worse than telling them
nothing, because they will go looking for the file before they give up.

The line is enforced rather than agreed. A studio baseline can seal a key —
`core.presubmit.large-file:settings.maxBytes` — and no downstream layer may loosen it.
Seals are unioned along the inheritance chain, so a project that declares its own sealed
list cannot quietly drop the studio's.

---

## A worked example

ProjectA ships a packaged Win64 build that streams from a fixed-size bundle. ProjectB is
a cinematic-heavy project on the same engine.

ProjectA's policy:

```jsonc
{
  "schemaVersion": 1,
  "pipeline": "projecta",

  "rules": {
    "core.presubmit.large-file": {
      // The general limit, including editor-only work.
      "settings": { "maxBytes": 5242880 }
    }
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

ProjectB's policy, same rule, same DLL:

```jsonc
{
  "schemaVersion": 1,
  "pipeline": "projectb",

  "rules": {
    "core.presubmit.large-file": {
      // Cinematics ship uncompressed source. 5 MB would block every submit.
      "settings": { "maxBytes": 209715200 }
    }
  }
}
```

No fork. No branch. No second copy of the script.

---

## What a day looks like

For the consumer, one command:

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

Every finding carries the same four fields — where, what was expected, what was found, and
what to do about it. A rule that fails without all four sends somebody to ask a colleague.

That limit is 2,621,440 because the run named `--platform win64`. Drop the flag and the
same command against the same policy reports 5,242,880:

```text
Preflight — pre-submit — projecta@1.4.0 (pinned) — any/Development
policy  projecta                                      local overlay not applied

  ✓  core.presubmit.forbidden-paths                      0.0s
  ✗  core.presubmit.large-file                           0.0s
     Changed file exceeds the size limit.
       at  Art/Characters/hero_diffuse.tga
       expected  at most 5,242,880 bytes
       actual    11,400,000 bytes
       fix       Move the file out of version control, or ask the pipeline's author to raise 'maxBytes' for this rule if the size is intended.

  Blocked — 1 failed, 1 passed in 0.0s
```

One file, two budgets, no fork. The platform is never inferred: without the flag the run
is `any`, and a run that forgot to name its target gets the general limit rather than
whichever target happened to be listed first.

Three stages exist. The CI job runs all three; a person usually runs one.

| Stage | Question it answers |
|---|---|
| `workspace` | Is this machine set up to build at all? |
| `pre-submit` | Is this change safe to submit? |
| `build-readiness` | Will a full build succeed? |

A few more commands exist for when somebody wants to know *why*, or wants numbers:

| Command | What it answers |
|---|---|
| `preflight explain <rule-id>` | Where every effective value came from — file, line, and what it overrode |
| `preflight rules` | What is loaded, what the policy resolved to, and the execution levels |
| `preflight measure --label build -- <command>` | Times a command and records it, so cost is measured rather than claimed |
| `preflight report --since 30d` | What the history says: durations, percentiles, failure rates |

`explain` is the one worth showing, because it is the answer to "why is my file being
rejected at that number":

```bash
preflight explain core.presubmit.large-file --platform win64
```

```text
core.presubmit.large-file — Large changed file
  stage        pre-submit
  depends on   —
  dependents   —

Effective policy
  key                  value       origin
  enabled              true        engine default
  blocking             true        RuleDescriptor default
  gating               false       RuleDescriptor default
  severity             error       RuleDescriptor default
  timeoutSeconds       60          RuleDescriptor default
  settings.maxBytes    2621440     projecta@1.4.0/projecta.json:9   (target win64)
                                   overrides projecta@1.4.0/projecta.json:5 (5242880)

Policy chain         projecta
Local overlay        not applied (no local file)
```

Every layer, in order, with the package, the file and the line that produced it —
including which value it replaced and where that one came from. Line 9 is the `win64`
target block; line 5 is the general rule it beat.

### Setting up a machine

Whoever sets the repository up does this once, and commits the result:

```bash
preflight pipeline install \\studio\tools\preflight\projecta-1.4.0.zip
preflight pipeline declare projecta
```

```text
Wrote preflight.base.json.
This checkout is now the 'projecta' pipeline.
```

`declare` writes the file that says which pipeline this checkout is and which version
range it accepts:

```jsonc
{
  "schemaVersion": 1,

  // Which pipeline this checkout is. With this key nobody has to type
  // --pipeline, and a workspace holding several is no longer ambiguous.
  "pipeline": "projecta",

  // The range of 'projecta' package versions this checkout accepts.
  // minimumVersion is inclusive, maximumVersion is exclusive.
  "requiresPipeline": {
    "minimumVersion": "1.4.0",
    "maximumVersion": "2.0.0"
  }
}
```

That file is versioned, so everybody who clones ProjectA afterwards already has it. The
order matters: run `declare` after the install and the range is written from the installed
version to the next major, as above. Run it first and there is no installed version to
read a range from.

For everybody else joining the project, day one is **one command**:

```bash
preflight pipeline install \\studio\tools\preflight\projecta-1.4.0.zip
```

No pin is required. With none, a run takes the newest installed version the checkout's
range allows, and the header says which rule chose it:

```text
Preflight — pre-submit — projecta@1.4.0 (from requiresPipeline) — win64/Development
```

Pinning is for holding a version still, or for rolling back:

```bash
preflight pipeline use projecta@1.4.0
```

```text
Pinned projecta@1.4.0 on this machine.
```

```text
Preflight — pre-submit — projecta@1.4.0 (pinned) — win64/Development
```

Installing never moves the pin, and says so rather than leaving you to find out:

```text
The pin is unchanged. Run 'preflight pipeline use projecta@1.4.0' to switch to it.
```

If installing moved the pin, one toolchain delivery would change what every machine
validates against, at once, and the retained versions would stop being a rollback.

---

## How it decides what to run

Rules declare dependencies on other rules, and the engine runs them in topological order,
in parallel within each level.

That ordering buys two things.

**Cheap checks gate expensive ones.** The 15-second compile probe declares a dependency
on the toolchain check. If the SDK is missing, the probe never starts. Nobody waits
fifteen seconds to be told something that was known in fifty milliseconds.

**Skips point at the cause, not at the parent.** When a rule with gating fails, its
transitive dependents are skipped — and each one reports the *original* failure, walking
back through the intermediate skips:

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

`core.build.compile-probe` depends on `core.build.configuration`, not on the toolchain
directly — and it still names the toolchain. The attribution walks past the intermediate
skip to the thing somebody has to fix. One cause, and a list of consequences labelled as
such.

Two axes govern that, and they are independent on purpose:

| Axis | Question | What it affects |
|---|---|---|
| `blocking` | Should this failure fail the run? | The verdict and the exit code |
| `gating` | Does it still make sense to run what depends on this? | Execution |

All four combinations are useful. A rule can fail the build without stopping its
dependents from producing useful information, and a rule can be a pure warning that still
makes running its dependents a waste of time.

---

## Honesty, which is the actual product

A validation tool that reports success without having checked is worse than no tool,
because its green is counted as evidence. Everything below exists for that reason.

| | |
|---|---|
| **`n/a` is not `passed`** | A rule that ran and found nothing to look at says so. A tick would claim a check that never happened |
| **A missing manifest fails, it does not skip** | Otherwise a typo in a configured path leaves a rule permanently green |
| **A cached result says `(cached)`** | In the console and in the JSON. Always |
| **A rule crashing is not a rule failing** | `Errored` and `Failed` are never aggregated: one blames the workspace, the other blames the tool. A rule cannot claim either status for itself — a rule that tries is recorded as `Errored`, naming the status it claimed |
| **A percentile the sample cannot support is not printed** | A p50 needs five observations and a p95 needs fifty. Below that the report prints a dash and says how many it would need — the maximum dressed as a percentile is the number nobody can defend afterwards |
| **Refusal over assumption** | No stage argument, an unknown format value, an ambiguous project selection, two contradictory flags — all are refusals that name what would have worked, never a default nobody chose |
| **A run with zero rules executed says so** | And distinguishes "policy disabled all of them" from "no rule has this stage" |
| **A remediation names somebody who can act on it** | A consumer cannot edit the project's policy, so a message telling them to is a dead end wearing the clothes of an instruction |

Exit codes carry the same split, because the two call different people:

| Code | Meaning | Who is called |
|---|---|---|
| `0` | Passed, possibly with warnings | Nobody |
| `1` | Blocked | The author of the commit |
| `2` | Broken configuration — invalid policy, a cycle, a plugin that would not load, an invocation the CLI refuses | The owner of the tool |
| `3` | Internal error, or a rule that crashed | The owner of the tool |

Determinism is enforced rather than hoped for: ordered output, invariant culture, no
ambient clock or randomness anywhere on a decision path. Two runs of the same commit on
the same machine produce byte-identical JSON, and the run header always names the exact
package version that produced the verdict — so two runs of one commit against different
rule sets can never be mistaken for each other.

---

## Extending it

A project writes its own rules against a small contract assembly, compiles, and drops the
DLL where the tool looks. No fork of the engine, no recompile of it, no registration step.

```csharp
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

public sealed class TextureDimensionRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = new RuleId("projecta.content.texture-dimension"),
        DisplayName = "Texture dimension",
        Stage = ValidationStage.PreSubmit,
        DefaultSeverity = Severity.Error,
        DefaultBlocking = true,
        DefaultGating = false,
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var maxDimension = context.Policy.GetValue("maxDimension", 4096);
        // …
    }
}
```

The rule reads its limit from the policy and knows nothing about which project it is
running for. ProjectB sets `maxDimension: 8192` and uses the identical binary.

The contract assembly is organised by who implements what, so the first question a rule
author has — what do I have to write — is answered by the namespace:

| Namespace | What is in it |
|---|---|
| `Preflight.Abstractions.Rules` | What the author writes and what the engine hands back: `IValidationRule`, `RuleContext`, `RuleDescriptor`, `RuleOutcome`, `RuleId` |
| `Preflight.Abstractions.Services` | What the engine implements for the rule: the file system, the process runner, the logger, the policy reader |
| `Preflight.Abstractions.Model` | The data the two exchange: the stage and status enums, `Finding`, `ChangedFile` |

Each of the four obligations a rule owes the engine is documented on `ExecuteAsync`
itself rather than in a guide somebody has to find: report a wrong workspace as `Failed`
rather than by throwing, never claim `Skipped` or `Errored`, check the cancellation token
in any loop over workspace-sized input, and return an outcome.

Each plugin loads in its own collectible context, so two plugins with conflicting
dependencies coexist. The contract is versioned, and the rules are boring on purpose:

| Situation | Loads? |
|---|---|
| A different generation of the contract | No |
| Minor above the host, on a 1.x line | No |
| Minor equal or below on a 1.x line, any patch | Yes |
| Same 0.x minor, any patch | Yes |
| A different 0.x minor, either direction | No |

The project is below 1.0, and SemVer puts the breaking axis on the minor while it stays
there: `0.1` and `0.2` are as unrelated as `1.x` and `2.x`, and the asymmetry that lets a
plugin built against 1.2.0 load on a 1.4.0 host does not carry over. The loader and the
incremental cache read that rule from one function, so they cannot disagree about which
contracts are the same — the two answering differently is a cached pass served under a
contract that changed.

The counter-intuitive line is that adding a member to an existing interface is a
*breaking* change, not an additive one: for the plugin *implementing* it, a new member
breaks the build. New capability therefore arrives as a new optional interface. Moving a
type to a different namespace breaks for the same reason — a plugin references its types
by full name in metadata, so a move is indistinguishable from a deletion.

A rule id colliding between two sources is refused with both assembly paths named and
neither winning — load order decides nothing, ever.

`preflight create rule projecta.content.texture-dimension` scaffolds the project with the
one line the whole plugin model rests on already correct.

---

## Distribution

A pipeline is delivered as a package: a deterministic archive holding the policy, the
rule assemblies, and a manifest carrying a SHA-256 for every file plus the contract range
those assemblies need.

```bash
preflight pipeline validate ./projecta-pipeline    # every error at once, before publishing
```

```text
6 rules visible, policy 'preflight.projecta.json' loads clean.
```

```bash
preflight pipeline pack ./projecta-pipeline -o projecta-1.4.0.zip
```

The archive is deterministic — ordinal entry order, fixed timestamps, no filesystem
metadata — so the same tree produces the same bytes on any machine, which is what makes a
published checksum mean something. `pack` lists every file with its digest as it writes.

Installation verifies every digest and the contract range *before* writing a byte, keeps
the last ten versions per project for rollback, and prints anything it removes.

The tool downloads nothing. It reads what is on disk. That is deliberate — a run whose
result depends on what time it is stops being reproducible, and an automatic update
executes new assemblies on a hundred machines without anybody approving them.

Which leaves the channel open, and any of these work today:

| Channel | How |
|---|---|
| Existing studio toolchain | The same mechanism that updates the engine and the IDE drops the package; the CI job installs it |
| Internal artifact feed | Publish the archive; a one-line install step fetches and installs it |
| Source control release assets | Attach the archive to a tag; CI downloads and installs |
| A network share | `preflight pipeline install \\studio\tools\...` |

And the repository states which version range it accepts, so a machine carrying a stale
package fails loudly instead of validating against it.

---

## Where it can go from here

The engine is a library. The command line is one host over it, and it was built first
because it is the host that proves the engine works. Nothing in the design ties it there.

| Surface | What it would take |
|---|---|
| **A button in Visual Studio or Rider** | An extension that runs the same executable and renders the JSON. The report is computed as data and rendered separately, so a second renderer is a renderer and not a scrape of a screen |
| **A local web UI** | A page that detects the installed executable, runs it, and shows the graph and the findings. The dependency graph already renders to Graphviz DOT |
| **Code review integration** | Already there: `run --format sarif` emits SARIF 2.1.0, which review pipelines read without any integration work |
| **A build-farm service** | The engine is hostable in-process. If invocation volume ever made a process per validation untenable, that path is open — and a test enforces that the engine never reaches back into the command line, which is what keeps it open |

---

## Scale

Honest numbers, and the limits are written down rather than assumed.

| | |
|---|---|
| Per-run overhead | Budgeted at 500 ms: runtime startup, plugin discovery, policy load |
| Parallelism | Bounded by policy, defaulting to processor count, one level of the graph at a time |
| History | NDJSON, append-only, with a per-process mode for build farms where many machines write at once. The read path is what breaks first, at roughly 10⁵ runs per day |
| Caching | Off unless a project asks. A check is cached only if it can describe its own inputs exactly; anything that cannot declines, and nothing is stored |
| Growth | Plugin discovery is the only component of startup that grows with the number of rules, at roughly 10–30 ms per assembly |

The incremental cache key includes the digest of that rule's effective policy, so changing
a limit invalidates the cached result rather than serving a pass from the old one. It also
includes the generation of the contract — the part of the version that changes when the
contract breaks — so a result serialised under an older shape is never read back under a
new one.

---

## State of the code

| | |
|---|---|
| Version | 0.1.1. Below 1.0 on purpose: nothing here is published under a stability promise yet, and SemVer reserves the leading zero for exactly that |
| Target | .NET 10, nullable enabled, warnings as errors, analysers at `latest-recommended` |
| Tests | 1769, across unit, contract, exact-console-bytes and Gherkin scenarios driving the real executable |
| Coverage | 100% of lines, branches and methods |
| Platform | Windows today; nothing in the design is Windows-specific, and the examples above use the platform that actually exists rather than one that would be nice to claim |

The coverage number is not the interesting part — what it cost is. Closing the last few
branches forced three changes that made the code better rather than merely more measured:
a switch over a closed two-case hierarchy became a direct test with a guard asserting the
hierarchy is still two; a collection-expression spread became an explicit list, because
the spread compiles to a probe no test can steer; and a refusal that Windows filesystems
make unreachable became a pure public function, testable here and still protecting the
filesystems where it can happen. Writing the tests for one subsystem also found a real
defect: an error message was printing an absolute installation path — which carries the
account name of whoever ran the tool — into a message that reaches CI logs.

**The review pass, in progress.** The goal until recently was a tool that works end to
end, and it does. What is happening now is a review, subsystem by subsystem, each on its
own branch with the full suite green before it lands. The contract assembly has been
through it, and the shape of what such a pass finds is worth stating, because it is not
what a linter finds:

- Fourteen files sat loose in the contract's project root, one of them grouping four
  unrelated enums under a name that described their C# category rather than their meaning.
  They are now three folders — what the author writes, what the engine provides, and the
  data between them — and the namespaces follow. A plugin references its types by full
  name in metadata, so moving them breaks every compiled plugin, and the loader refuses an
  older one by name and number rather than failing later at the first type load.
- The interface every plugin implements documented none of its obligations. All four were
  written as comments inside one built-in rule, where a plugin author never looks.
- A comment asserted that nothing enforced a rule of the contract. The engine had always
  enforced it. A comment that is merely incomplete costs a reader time; one that is wrong
  costs them a wrong decision.
- Two factory methods stored the caller's array by reference behind a read-only list type,
  so the type promised immutability it did not deliver.
- Nine test fixtures spelled the host's own contract version by hand, and all nine went
  stale at once on the first bump. They now derive it — and paid for themselves the next
  time the number moved, which needed no test edit at all.
- The contract had been declared at 1.0.0 and then raised to 2.0.0, both of which announce
  the rupture of a compatibility promise that was never made to anybody. Reading the range
  honestly as 0.x surfaced two latent defects that the old numbering had been hiding: the
  loader would have accepted a plugin built against 0.1 running on 0.2, and the cache key
  would not have changed between them. Below 1.0 the breaking axis is the minor, and both
  now read that rule from the same function.

The remaining subsystems have not been through it yet, and their comments still cite
internal design documents that this repository does not carry.

---

## Building it

```bash
dotnet build
```

```powershell
.\scripts\verify.ps1
```

Restore, format check, build under warnings-as-errors, the whole suite, then coverage,
stopping at the first failure. The coverage step fails explicitly if it measures zero
assemblies — every coverage failure this project has had was silent, and a coverage run
that reports success having measured nothing is the same defect the tool exists to
prevent, aimed at its own instrumentation.

---

## Layout

```text
src/
  Preflight.Abstractions   The plugin contract. Depends on nothing beyond the BCL.
    Rules/                 What a rule author writes, and what the engine hands back.
    Services/              What the engine implements for a rule.
    Model/                 The data the two exchange.
  Preflight.Core           The engine: graph, execution, policy, history, cache, plugins.
  Preflight.Rules          The six built-in rules.
  Preflight.Cli            Argument parsing, reporters, exit codes, packaging.
tests/
  Preflight.Core.Tests
  Preflight.Rules.Tests
  Preflight.Cli.Tests
  Preflight.Specs          Gherkin scenarios driving the real executable.
  Preflight.TestSupport    Shared fixtures.
samples/
  Sample.Production.Rules  A worked plugin: one rule, one project reference.
fixtures/                  Workspaces, good and broken, the tests run against.
scripts/verify.ps1         Format, build, test, coverage.
```

`Preflight.Rules` referencing `Preflight.Core` would make the plugin model fiction, so it
is enforced by a test rather than left as a convention. The built-in rules see exactly
what an external plugin sees.

---

## The six built-in rules

| Id | Stage | Depends on |
|---|---|---|
| `core.workspace.toolchain` | workspace | — |
| `core.workspace.dependencies` | workspace | toolchain |
| `core.presubmit.forbidden-paths` | pre-submit | — |
| `core.presubmit.large-file` | pre-submit | — |
| `core.build.configuration` | build-readiness | toolchain |
| `core.build.compile-probe` | build-readiness | configuration |

Six is deliberate. A rule ships only if it came from a real incident, and the built-in set
is meant to demonstrate the model rather than to be exhaustive — the interesting rules for
any given studio are the ones that studio writes.

---

## What it deliberately does not do

| | |
|---|---|
| Download anything | It validates what is on disk. That is what keeps two runs of one commit in agreement |
| Fix anything | It reports what to do; it does not edit the workspace on its own initiative |
| Reimplement static analysis | It emits SARIF into the review pipeline that already reads it |
| Replace the build system | Compiling is still the build system's job |
| Guess | Every ambiguity is a refusal that names what would have worked |

---

## Licence

MIT.
