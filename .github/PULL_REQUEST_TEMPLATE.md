<!--
Thanks for the pull request. The checklist below is short on purpose - it only
covers the things CI cannot tell you about, or that are easy to miss.
-->

## What this changes

<!-- One or two sentences. Link the issue it closes, if there is one. -->

## Why

<!-- What was wrong, or what became possible. Skip if the "what" already says it. -->

## Checklist

- [ ] The commit subject follows [Conventional Commits](https://www.conventionalcommits.org/) —
      the type decides the released version, so `feat:` for a feature, `fix:` for a fix, `chore:`/`ci:`/
      `test:` to release nothing. See [CONTRIBUTING.md](CONTRIBUTING.md#commit-conventions).
- [ ] EditMode tests pass locally, and behaviour changes come with a test.
- [ ] Documentation updated in this PR — `README.md` for end-user surface,
      `Documentation~/HowToUse.md` for everything else.
- [ ] `package.json`'s `version` and `CHANGELOG.md` are **untouched** — the release bot owns both.
- [ ] If this is a breaking change, the body carries a `BREAKING CHANGE:` footer describing what
      callers must do.
