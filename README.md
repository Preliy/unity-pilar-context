# PILAR Context

Attach an authored key/value context dictionary to any GameObject in a digital twin scene, and export
the annotated hierarchy as structured JSON for downstream AI/LLM pipelines — PLC code generation,
documentation, diagnostics.

A digital twin already encodes *what* a machine is: components, links, transforms. What it never
encodes is *why* — what a station is for, what a sensor guards, what an operator does at this panel.
That knowledge lives in engineers' heads and in documents no tool can read. PILAR Context gives it a
place to live next to the objects it describes, and a way to get it out again in a form a model can
consume.

## Requirements

- Unity **6000.3** or newer. **No required dependencies.**
- *Optional:* [`com.open-commissioning.core`](https://github.com/OpenCommissioning/OC_Unity_Core) —
  when present, the package reads OC's `Hierarchy`, `IDevice` and `Link` to resolve PLC symbol paths,
  device link state and hierarchy roles.
- *Optional:* `com.unity.pipeline` `0.4.0-exp.1`+ — adds the `context_*` CLI commands for driving the
  package headlessly from a terminal or an AI agent.

Each optional integration lives in its own assembly, guarded by a version define. Without them the
corresponding assembly is excluded rather than failing to compile: `ContextNode`, the inspector and
JSON export keep working, and the framework-derived fields export as `""`.

## Install

Package Manager ▸ **Install package from git URL**, or add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.pilar.context": "https://github.com/Preliy/unity-pilar-context.git#upm"
  }
}
```

**Always install from `upm`, never from the default branch.** `upm` is the generated release branch:
the tests are hidden, the CI files and the development host project are gone. `master` is the
development tree, and a UPM git install copies the whole repository into `PackageCache` — installing
from it would compile this package's test assemblies inside your project.

To pin a release, append its version to the ref — `#upm/vX.Y.Z`. Every release is also published as a
`package/`-prefixed tarball on the
[Releases page](https://github.com/Preliy/unity-pilar-context/releases).

For the Open Commissioning integration, add OC and its own prerequisites first — none of them are on a
registry, so UPM cannot fetch them for you:

```json
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
"com.dbrizov.naughtyattributes": "https://github.com/dbrizov/NaughtyAttributes.git#upm",
"com.open-commissioning.core": "https://github.com/OpenCommissioning/OC_Unity_Core.git#upm"
```

> The package id is `com.pilar.context`, lowercase, because UPM ids may not contain uppercase — the
> same reason Open Commissioning ships as `com.open-commissioning.core`. Everything else — display
> name, namespaces, assemblies, menu — is **PILAR**.

## Assemblies

| Assembly | Needs | Excluded when missing |
|---|---|---|
| `PILAR.Context` | — | — |
| `PILAR.Context.Editor` | — | — |
| `PILAR.Context.OpenCommissioning` | `com.open-commissioning.core` | `PILAR_OC` |
| `PILAR.Context.Pipeline` | `com.unity.pipeline` | `PILAR_UNITY_PIPELINE` |

## `ContextNode` API

Attach to any GameObject — a leaf sensor, a sub-assembly, a whole functional group. Entries are an
ordered `List<ContextEntry { string key; string value; }>`; a `List` rather than a `Dictionary`
because Unity cannot serialize the latter, so uniqueness is enforced by the API instead.

| Member | Behaviour |
|---|---|
| `IReadOnlyList<ContextEntry> Entries` | The entries, in author order |
| `bool ContainsKey(string key)` | |
| `bool TryGetValue(string key, out string value)` | `value` is `null` when absent — distinct from a stored empty value |
| `void Add(string key, string value)` | Throws `ArgumentException` on a duplicate, null or empty key |
| `void Set(string key, string value)` | Insert-or-update. Updates in place, so the entry keeps its position |
| `bool Remove(string key)` | `false` when the key was not there |
| `void Clear()` | |

## Quick start

1. Select a GameObject that means something — a station, an assembly, a sensor.
2. **Add Component ▸ PILAR ▸ Context ▸ Context Node**.
3. Add entries, e.g. `Function` → *"Reads the RFID tag on the incoming part to confirm identity."*
4. Run **PILAR ▸ Context ▸ Export Machine Context (JSON)**. With nothing selected the exporter looks for
   a root named `Project`, then falls back to the scene's only root; select a GameObject to export a
   specific subtree.
5. Read the result at `Assets/StreamingAssets/{SceneName}_Context.json`.

One exported node:

```json
{
  "name": "P_Reader",
  "unityPath": "Project/FG_01/P_Reader",
  "plcPath": "FG_01.P_Reader",
  "plcLinked": "true",
  "hierarchyRole": "",
  "components": ["SensorBinary", "TagReader"],
  "context": [
    { "key": "Function", "value": "Reads the RFID tag on the incoming part to confirm identity." }
  ],
  "children": []
}
```

## Agent skills

`Skills~/` ships three [Claude Code](https://claude.com/claude-code) skills that drive the `context_*`
commands against a live Editor — one to locate a group, station or device, one to write and review
entries, one to measure coverage and verify the export. They encode the workflow that annotated a real
machine, with that project's specifics stripped out.

Download `pilar-context-skills-<version>.zip` from the
[Releases page](https://github.com/Preliy/unity-pilar-context/releases), or take them from `Skills~/`
in the installed package, and copy the folders into your project's `.claude/skills/`.

They need a running Editor, the Unity CLI and `com.unity.pipeline`. See
**[Documentation~/AgentSkills.md](Documentation~/AgentSkills.md)**.

## More

**[Documentation~/HowToUse.md](Documentation~/HowToUse.md)** — export semantics and pruning, the full
JSON schema, the CLI command reference, agent-driven authoring, and how to support a twin framework
other than Open Commissioning.

## Contributing

Bug reports, feature requests and pull requests are welcome. Development setup, how to run the test
suite, and the commit conventions that drive releases are in
**[CONTRIBUTING.md](.github/CONTRIBUTING.md)**.

Please report security issues privately — see [SECURITY.md](.github/SECURITY.md).

Licensed under MIT — see [LICENSE.md](LICENSE.md). Changes are recorded in [CHANGELOG.md](CHANGELOG.md).
