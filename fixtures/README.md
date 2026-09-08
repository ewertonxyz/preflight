# Workspace fixtures

Real directories on real disk, for the integration layer of the test suite.
The unit layer runs every rule against substituted services and touches none of this;
these exist so that the rules are also known to work against a filesystem, which is
the only thing that proves the substitutes were configured to describe reality.

`workspace-good` satisfies all twelve built-in rules. Each directory under
`workspace-broken` is built to break one of them, and the integration test asserts
that the intended rule reaches the intended status and that no other rule *errored*.
It does not assert "exactly one": `build-config` carries no manifest, so the toolchain
rule fails on it too. Making the stronger claim true would mean every broken fixture
had to be a good workspace in all other respects.

Half the rules check nothing until they are configured, and configuration is policy
rather than a file — so the arrangement in the integration test supplies the
attributes path, the companion pairs, the approved list and the SDK command. Without
it six of the twelve would report that they looked at nothing, on every fixture, and
the suite would be green having exercised none of them.

Two things about these directories are deliberate and easy to undo by accident:

- `.gitattributes` marks `fixtures/**` as `-text`, so nothing here is rewritten on
  checkout. A fixture compared byte for byte cannot survive line-ending normalisation.
- `.gitignore` excludes `[Oo]bj/` everywhere, which would swallow the artefact a real
  .NET restore leaves behind. That is why `restoredMarker` points at a plain directory
  under `packages/` instead: a fixture that is silently absent makes
  `core.workspace.dependencies` warn for the wrong reason, and the test would still
  be green about the wrong thing.

Two directories are deliberately absent, and neither should be added:

- **`workspace-broken/free-space/`.** A floor that a real disk fails is a fixture whose
  verdict depends on the size of the disk running it: 10 TB fails everywhere today and
  passes in silence on the first machine with a 20 TB array. The failing path is covered
  by unit tests with a substituted probe; the integration layer proves only that the
  shipped probe reaches a real volume.
- **`workspace-broken/path-portability/`.** `< > : " | ? *`, control characters, `CON`
  and `NUL`, and a pair of files differing only in case **cannot exist** as file names in
  a Windows working tree, and the pipelines run on Windows. The rule reads the change set
  rather than the tree, so the break lives in the change set, where the unit tests hold it.

One more thing is easy to undo by accident: the attributes file in the LFS fixtures is
**not** called `.gitattributes`. Git obeys a real one in its own subtree, so `*.psd
filter=lfs` would make git run the clean filter on `add` and the smudge filter on
checkout — which either errors without the extension installed, or, with it installed,
quietly converts the committed blob into a pointer and leaves the broken fixture no
longer broken. The rule is pointed at the file by the `attributesPath` policy key, which
is the reason that key exists.

Every broken fixture is meant to be derivable from a real case. See
the provenance notes, which record which of them are and which are not.
