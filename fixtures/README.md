# Workspace fixtures

Real directories on real disk, for the integration layer of the test suite.
The unit layer runs every rule against substituted services and touches none of this;
these exist so that the rules are also known to work against a filesystem, which is
the only thing that proves the substitutes were configured to describe reality.

`workspace-good` satisfies all six built-in rules. Each directory under
`workspace-broken` breaks exactly one, and the integration tests assert
**exactly** that one — the "exactly" is what stops a fixture from failing for an
accidental second reason and still looking correct.

Two things about these directories are deliberate and easy to undo by accident:

- `.gitattributes` marks `fixtures/**` as `-text`, so nothing here is rewritten on
  checkout. A fixture compared byte for byte cannot survive line-ending normalisation.
- `.gitignore` excludes `[Oo]bj/` everywhere, which would swallow the artefact a real
  .NET restore leaves behind. That is why `restoredMarker` points at a plain directory
  under `packages/` instead: a fixture that is silently absent makes
  `core.workspace.dependencies` warn for the wrong reason, and the test would still
  be green about the wrong thing.

Every broken fixture is meant to be derivable from a real case. See
the provenance notes, which record which of them are and which are not.
