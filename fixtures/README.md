# Workspace fixtures

Real directories on real disk, for the integration layer of the test suite.
The unit layer runs every rule against substituted services and touches none of this;
these exist so that the rules are also known to work against a filesystem, which is
the only thing that proves the substitutes were configured to describe reality.

`workspace-good` satisfies every built-in rule that a directory can satisfy. Each
directory under `workspace-broken` is built to break one of them, and the integration test asserts
that the intended rule reaches the intended status and that no other rule *errored*.
It does not assert "exactly one": `build-config` carries no manifest, so the toolchain
rule fails on it too. Making the stronger claim true would mean every broken fixture
had to be a good workspace in all other respects.

Half the rules check nothing until they are configured, and configuration is policy
rather than a file — so the arrangement in the integration test supplies the
attributes path, the companion pairs, the approved list, the SDK command, the
line-ending list and the pinned-reference entries. Without it half of them would
report that they looked at nothing, on every fixture, and the suite would be green
having exercised none of them.

One rule is exempt from the test that demands a positive path against
`workspace-good`, by name and with the reason recorded beside the name rather than by
relaxing the test to accept "not applicable" in general — a general tolerance would
hand the same escape to every rule, including the ones the test exists to catch:

- **`core.workspace.submodule-pin`.** This repository has no submodule, and adding one
  to the fixture changes what every pipeline has to clone in order to run the suite at
  all. Its warning and failure paths are covered by unit tests with a substituted
  process runner, and the end-to-end behaviour — a warning that exits 0, and exits 1
  under `--fail-on-warning` — was confirmed by hand against a real repository with an
  uninitialised submodule.

Two things about these directories are deliberate and easy to undo by accident:

- `.gitattributes` marks `fixtures/**` as `-text`, so nothing here is rewritten on
  checkout. A fixture compared byte for byte cannot survive line-ending normalisation.
- `.gitignore` excludes `[Oo]bj/` everywhere, which would swallow the artefact a real
  .NET restore leaves behind. That is why `restoredMarker` points at a plain directory
  under `packages/` instead: a fixture that is silently absent makes
  `core.workspace.dependencies` warn for the wrong reason, and the test would still
  be green about the wrong thing.

Four directories are deliberately absent, and none should be added:

- **`workspace-broken/free-space/`.** A floor that a real disk fails is a fixture whose
  verdict depends on the size of the disk running it: 10 TB fails everywhere today and
  passes in silence on the first machine with a 20 TB array. The failing path is covered
  by unit tests with a substituted probe; the integration layer proves only that the
  shipped probe reaches a real volume.
- **`workspace-broken/path-portability/`.** `< > : " | ? *`, control characters, `CON`
  and `NUL`, and a pair of files differing only in case **cannot exist** as file names in
  a Windows working tree, and the pipelines run on Windows. The rule reads the change set
  rather than the tree, so the break lives in the change set, where the unit tests hold it.
- **`workspace-broken/vcs-configuration/`** and **`workspace-broken/submodule-pin/`.** The
  defect of neither fits in a directory: one lives in the configuration of the client on the
  machine running the suite, and the other in whether a submodule exists at all. A fixture
  that appeared to express either would in fact be asserting the machine.

One more thing is easy to undo by accident: the attributes file in the LFS fixtures is
**not** called `.gitattributes`. Git obeys a real one in its own subtree, so `*.psd
filter=lfs` would make git run the clean filter on `add` and the smudge filter on
checkout — which either errors without the extension installed, or, with it installed,
quietly converts the committed blob into a pointer and leaves the broken fixture no
longer broken. The rule is pointed at the file by the `attributesPath` policy key, which
is the reason that key exists.

The same trap has a second edge now that an attributes file also answers a question about
line endings. A real one declaring `eol=` would have git rewrite these directories on
checkout, and the line-endings fixture is broken precisely because of the bytes it holds.
The repository-wide `fixtures/** -text` is what keeps those bytes: it is load-bearing,
not tidiness.

Every broken fixture is meant to be derivable from a real case. See
the provenance notes, which record which of them are and which are not.
