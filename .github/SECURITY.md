# Security Policy

## Supported versions

Only the latest release is supported. Fixes ship in a new version rather than as patches to older
ones — install from `upm` (or pin `upm/vX.Y.Z` and update) to stay current.

| Version | Supported |
|---|---|
| Latest release on `upm` | ✅ |
| Anything older | ❌ |

## Reporting a vulnerability

**Please do not open a public issue.** Use GitHub's private reporting:

**[Report a vulnerability](https://github.com/Preliy/unity-pilar-context/security/advisories/new)**

If that is unavailable to you, email <vikt.gaponenko@gmail.com> with `SECURITY` in the subject.

Please include what you can: the package version, the Unity version, which optional dependencies are
installed, and the steps to reproduce. You will get an acknowledgement, and a fix or an explanation of
why the report falls outside the scope below.

## Scope

Being honest about what this package is, so you can judge whether something is worth reporting: it is
an Editor-only authoring and export tool. It opens no network connections, ships no binaries or native
plugins, has no runtime component beyond a `MonoBehaviour` holding serialized strings, and its only
required dependency set is empty.

The realistic surface is therefore narrow, and these are the parts worth looking at:

- **File writing.** The export writes to `Assets/StreamingAssets/{SceneName}_Context.json`. A scene
  name that escapes that directory would be a real finding.
- **Exported content.** `ContextNode` entries are author-supplied strings that end up in a JSON
  document, typically fed onward to an LLM or code generator. Injection into that downstream pipeline
  is a legitimate concern, though the trust boundary largely belongs to whoever consumes the export.
- **The `context_*` CLI commands**, which mutate scenes and prefab assets when
  `com.unity.pipeline` is installed.

Out of scope: vulnerabilities in Unity itself, in Open Commissioning, or in `com.unity.pipeline` —
please report those to their respective projects.
