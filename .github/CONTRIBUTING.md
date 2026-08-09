# Contributing to PILAR Context

Thanks for taking the time. Bug reports, feature requests and pull requests are all welcome.

By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). Security issues go through
[SECURITY.md](SECURITY.md) rather than a public issue.

## Getting set up

This repository **is** the package — `package.json` sits at the root, which is what a UPM git install
expects. Unity cannot open a bare package, so a host project lives in `TestProject~/` and references
the package back through `"com.pilar.context": "file:../../"`.

Open **`TestProject~/`** in Unity 6000.3.18f1 — not the repository root — and edit the sources in
place. Changes compile straight into the host project.

Two things about `TestProject~` that are easy to trip over:

- **The trailing `~` is required.** A UPM git install copies the whole repository into `PackageCache`
  and Unity imports everything not hidden, so an untilded `TestProject/` would compile inside every
  consumer's project. `.npmignore` and package.json `"files"` do not apply to git installs.
- **`Assets/` must exist**, even empty — Unity refuses a project folder without one and reports a
  confusing "couldn't set project path" instead. Hence the tracked `TestProject~/Assets/.gitkeep`.

`TestProject~/Packages/packages-lock.json` is committed on purpose: Open Commissioning is tracked by
branch (`#upm`), so without the lock an upstream push would change what CI resolves and turn an
unrelated pull request red. When you deliberately move to a newer Open Commissioning, update the
lock's `hash` in the same commit and say why.

## Running the tests

From the Editor: **Window ▸ General ▸ Test Runner ▸ EditMode**. Headlessly:

```bash
Unity -batchmode -nographics -projectPath 'TestProject~' \
      -runTests -testPlatform EditMode -testResults results.xml
```

The suite is EditMode only. Please keep it green and add tests for behaviour you change — most of the
package is pure logic behind `ContextTreeFactory`, `ContextMetadataRegistry` and the `ContextNode`
API, all of which are straightforward to test.

CI runs the same suite three times: with both optional dependencies, without
`com.open-commissioning.core`, and without `com.unity.pipeline`. That is what proves an optional
integration is *excluded* rather than broken when its package is absent, so if you touch an
assembly definition, expect those legs to be the ones that catch you.

Two cases where the matrix does not run at all, and CI is still green:

- **Pull requests from forks.** They cannot read repository secrets, so there is no Unity licence.
- **When `UNITY_LICENSE` is unset or has expired.** A guard job publishes the licence's availability
  and the matrix is skipped with a warning rather than failing.

In both cases the licence-free `Validate package` job still runs, and a maintainer will confirm the
full matrix before merging.

## Commit conventions

Commit subjects on `master` follow [Conventional Commits](https://www.conventionalcommits.org/)
(angular preset). This is not a style preference — semantic-release reads them to decide the next
version, so the type you choose *is* the release decision.

| Type | Release |
|---|---|
| `feat:` | minor |
| `fix:` | patch |
| `perf:` | patch |
| `revert:` | patch |
| `refactor:` | patch |
| `docs(README):` | patch |
| `BREAKING CHANGE:` in the body | major |
| `docs:` (any other scope), `style:`, `test:`, `build:`, `ci:`, `chore:` | none |

A user-visible entry point counts as public surface. The export menu path, for example, is documented
as drivable through the Unity CLI, so renaming it affects callers outside this repository.

**Never edit `version` in `package.json`, and never write `CHANGELOG.md` entries by hand.** The
release bot owns both files and will overwrite anything you put there.

## Pull requests

- One logical change per pull request.
- Tests pass locally, and any behaviour change comes with a test.
- Documentation updated in the same PR. Note that the export menu path appears in
  `Editor/ContextMenuItems.cs`, `README.md` and `Documentation~/HowToUse.md` — CI checks that these
  agree, so change them together.
- The public surface is spelled **PILAR**, in full caps — namespaces, assemblies, menu paths. CI greps
  for title-case spellings and fails the build, including in documentation, so this rule cannot even
  quote its own counter-example. The package id is the one exception and stays lowercase
  (`com.pilar.context`), because UPM ids may not contain uppercase.

---

# Maintainer tasks

Everything below needs repository secret or settings access, so it applies to maintainers only.

## Renewing the CI Unity licence

The EditMode matrix needs an activated Unity Personal licence in the `UNITY_LICENSE` secret, and a
Personal activation expires. When it does, the matrix starts skipping — CI stays green and only warns,
so watch for the warning rather than a red build.

Note that `game-ci/unity-request-activation-file` is deprecated and now fails outright, and Unity 6's
licensing client keeps its entitlement in an access token rather than the
`C:\ProgramData\Unity\Unity_lic.ulf` that game-ci's documentation still points at. Neither route
works. Generate the activation file directly instead:

```bash
Unity -batchmode -nographics -quit -createManualActivationFile -logFile alf.log
```

That writes `Unity_v<version>.alf`. Upload it at <https://license.unity3d.com/manual>, download the
`.ulf` that comes back, and store its **entire contents**:

```bash
gh secret set UNITY_LICENSE < Unity_v6000.3.18f1.ulf
```

`UNITY_EMAIL` and `UNITY_PASSWORD` hold the Unity account credentials and only need setting once.

## Releasing

Releases are automatic — there is no manual step and no version to bump by hand.

When a push to `master` earns a release and the test matrix is green,
[`release.yml`](workflows/release.yml) runs semantic-release, which bumps `package.json`, prepends the
generated section to `CHANGELOG.md`, and commits both back to `master` as `chore(release): X.Y.Z
[skip ci]`, tagging `vX.Y.Z`.

It then rebuilds the `upm` branch from that commit — hiding `Tests/` as `Tests~/` and deleting
`.github/`, `.gitignore`, `.releaserc.json` and `TestProject~/`, while deliberately keeping
`.gitattributes` for its `eol=lf` normalisation — tags it `upm/vX.Y.Z`, and publishes a GitHub release
with a `package/`-prefixed tarball built by `git archive`.

**`upm` is what consumers install.** `master` is the development tree and must never be installed
from: a UPM git install copies the whole repository, so installing from `master` would compile this
package's test assemblies inside the consumer's project.

Two mechanisms worth knowing before changing any of this:

- **The release commit is pushed with a GitHub App token**, because the `master` ruleset requires
  status checks and only an App or an admin can be put on a ruleset bypass list. Unlike
  `GITHUB_TOKEN`, an App-token push *does* retrigger workflows — the `[skip ci]` in the release commit
  subject is what stops an infinite release loop.
- **Required status checks must name the `test / …` job contexts**, not game-ci's identically-named
  extra check runs, which always conclude `neutral` and would never satisfy the rule.
