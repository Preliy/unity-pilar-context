# How to use PILAR Context

The [README](../README.md) covers installation, the `ContextNode` API and a quick start. This document
covers everything else.

- [Authoring in the Inspector](#authoring-in-the-inspector)
- [Export](#export)
- [The JSON schema](#the-json-schema)
- [CLI commands](#cli-commands)
- [Agent integration](#agent-integration)
- [Supporting another twin framework](#supporting-another-twin-framework)
- [Troubleshooting](#troubleshooting)

## Authoring in the Inspector

The `ContextNode` inspector shows three things:

1. **A derived-information panel** — the GameObject's other components, filtered so `Transform`,
   `Collider`, `Renderer` and `MeshFilter` noise does not drown out the meaningful ones, plus anything
   an installed integration contributes. Under Open Commissioning that is the resolved PLC path, the
   link state and the hierarchy role. This information is *shown, not stored*, and the PLC row is
   hidden entirely when no integration supplies one.
2. **The entry list** — Unity's own reorderable list, so drag-to-reorder, add and remove behave as you
   expect. Each row has a single-line key field over a word-wrapping, scrolling value field, since good
   context is usually a sentence or two rather than a word.
3. **Warnings** — duplicate and empty keys surface as warnings rather than being blocked, which matches
   the API's own tolerance and keeps you from losing half-typed work.

The component filter is shared with the exporter, so the component list you see while writing context
is exactly the one that gets exported.

### What to write

Keys are free-form; consistency across a project matters more than any particular vocabulary. Ones
that earn their place in practice: `Function` (what this does in the process), `Purpose` (why it
exists), `Interlocks` (what must be true before it may act), `Operator` (what a human does here),
`Failure` (what going wrong looks like).

Write what the scene cannot already tell a reader. "A conveyor motor" is visible from the components;
"runs continuously while the upstream buffer holds at least one part" is not.

## Export

**PILAR Context ▸ Export Machine Context (JSON)** walks the transform hierarchy and writes
`Assets/StreamingAssets/{SceneName}_Context.json`.

**Choosing the root.** An explicit selection wins. With nothing selected the exporter looks for a root
GameObject named `Project` (the Open Commissioning convention), then falls back to the scene's only
root. If the scene has several roots and none is named `Project`, it reports the roots and asks you to
select one rather than guessing. It logs instead of opening a dialog, because the menu item is also
driven headlessly through the Unity CLI, where a modal would block the caller.

**Pruning.** A subtree is kept when it contains an authored `ContextNode` anywhere within it, or when
an installed integration vouches for it — under Open Commissioning, when it contains any OC component
or `Hierarchy`. Everything else is dropped. This matters more than it sounds: the raw hierarchy of a
real machine is overwhelmingly CAD mesh geometry — around 7,300 GameObjects in the reference project
`Demo_1` — and an unpruned export is almost entirely noise.

Note the asymmetry: pruning applies to *children*. The root you export is always emitted, even if it
is empty.

**Walking transforms, not the framework's flat grouping.** The export follows Unity transform parenting
rather than Open Commissioning's flat `Link.ScenePath`, which would flatten past the structural station
and assembly wrappers that carry a `Hierarchy` but no `IDevice` — exactly the nodes where a human
writes the most useful context.

## The JSON schema

```json
{
  "sceneName": "Demo_1",
  "generatedAtUtc": "2026-08-08T09:12:44.1234567Z",
  "root": { "...": "ContextExportNode" }
}
```

Each node:

| Field | Meaning |
|---|---|
| `name` | GameObject name |
| `unityPath` | Slash path from the export root, e.g. `Project/FG_01/P_Reader` |
| `plcPath` | Framework-side logical path. `""` when no integration supplies one |
| `plcLinked` | `"true"` / `"false"` for devices, `""` for everything else |
| `hierarchyRole` | `"group"`, `"sampler"`, or `""` |
| `components` | Filtered component type names |
| `context` | The authored key/value entries, in author order |
| `children` | Nested nodes, in sibling order |

`plcLinked` is a string rather than a boolean because `JsonUtility` cannot serialize `bool?`, and the
three-way distinction is load-bearing.

### `plcLinked` — why `"false"` and `""` are different

`""` means *not a device*. `"false"` means *a device that exchanges no data with the PLC*: it is either
aggregated into a parent sampler's single symbol, or exists only for simulation.
**Code generation must skip `"false"` nodes.** In the reference project `Demo_1`, 80 of 93 devices are
PLC linked — a figure that matches Open Commissioning's own `Demo_1_Project_Tree.xml` exactly.

### `hierarchyRole` — the project tree is not the transform tree

Under Open Commissioning the PLC-visible tree is defined by `Hierarchy` components, **not** by
transform parenting:

- `"group"` — opens a level in the PLC path, joined with `.`
- `"sampler"` — opens no level; prefixes its children's names instead, joined with `_`, so a group like
  `FG_Transport` stays flat
- `""` — no `Hierarchy` at all: Unity-side grouping the PLC never sees

In `Demo_1`: 7 groups, 13 name samplers, 18 Unity-only wrappers.

## CLI commands

`Editor/Pipeline/` registers `[CliCommand]` tools so the annotated hierarchy can be read and written
from a terminal against a live Editor, via the Unity CLI and `com.unity.pipeline`.

| Command | Purpose |
|---|---|
| `context_tree` | List targets (nested or flat) with tier, PLC path, components, node state |
| `context_get` | Read one target's full entries |
| `context_set` | Upsert entries — a single `key`/`value`, or an `entries` JSON array |
| `context_remove` | Remove one entry by key |
| `context_ensure` | Add empty `ContextNode`s in bulk (dry-run by default) |
| `context_audit` | Coverage per tier, plus lists of missing and empty nodes |

**Addressing.** Targets are named by `unityPath` (`Project/FG_01/Barcode Reader/P_Reader`), by
framework `plcPath` (`MAIN.FG_01.P_Reader`, case-insensitive), or by a bare GameObject name when that
name is unique under the root. An ambiguous bare name is an error that tells you the full path to use
instead.

**Prefabs.** `context_set` and `context_remove` accept `--prefab true` to write to the backing prefab
asset rather than the scene instance. The backing slot is resolved through
`GetCorrespondingObjectFromSource`, not by matching names down from the instance root — an instance may
rename or reorder its children, and a name-matched path silently fails to resolve inside the asset.

**Upsert semantics.** All mutations go through the public `ContextNode` API, so `context_set` is a true
upsert: keys it does not mention are preserved.

**Tiers.** Targets are classified as `machine` (the export root), `group` (its direct children),
`device` (recognised as a device by an installed integration), and `assembly` (anything else with a
device somewhere below it). Without an integration installed there are no devices, so the tiering
collapses to the structural tiers.

## Agent integration

The package supplies the data model, authoring UI and export pipeline. It does not author content —
that is a job for a human or an agent working with one.

The pattern that works: an agent reads the scene, prefabs and behaviour scripts to establish what it
can derive mechanically, asks the engineer for the business and functional facts that code cannot
supply, then writes entries **live** into the running Editor through `context_set` — no scene file
editing, no reimport, and the result is visible in the Inspector immediately. `context_audit` reports
what is still uncovered, so the work can be resumed across sessions.

The `OC_Community_Demo_1` project carries a worked example as a set of Claude Code skills that drive
exactly these commands.

## Supporting another twin framework

The package core knows nothing about Open Commissioning. Everything framework-specific arrives through
one interface:

```csharp
namespace PILAR.Context.Editor
{
    public interface IContextMetadataProvider
    {
        int Order { get; }
        bool IsRelevant(Transform subtreeRoot);   // should this subtree survive pruning?
        bool IsDevice(Transform t);               // is this the leaf tier?
        string ResolvePath(Transform t);          // -> plcPath
        bool? ResolveLinkState(Transform t);      // -> plcLinked; null means "not a device"
        string ResolveRole(Transform t);          // -> hierarchyRole
        IEnumerable<string> InspectorNotes(Transform t);
    }
}
```

Implement it in an Editor assembly, give the class a public parameterless constructor, and it is picked
up automatically through `TypeCache` — there is no registration call. `ContextMetadataRegistry` merges
the installed providers, taking the first non-empty answer in `Order`, so several can coexist.

Every method receives arbitrary Transforms from anywhere in the scene. Return the neutral value (`""`,
`null`, `false`) rather than throwing when a Transform means nothing to your framework.

`PILAR.Context.OpenCommissioning` is the reference implementation and worth reading: one small file,
in an assembly guarded by a `com.open-commissioning.core` version define so it vanishes cleanly when OC
is absent.

## Troubleshooting

**The `context_*` commands are missing.** `com.unity.pipeline` is not installed. It is an experimental
Unity package; add `"com.unity.pipeline": "0.4.0-exp.1"` to your manifest. The rest of the package works
without it.

**`plcPath`, `plcLinked` and `hierarchyRole` all export as `""`.** No metadata provider is installed.
Add `com.open-commissioning.core`, or implement `IContextMetadataProvider` for your own framework.

**`CS0246: The type or namespace name 'OC' could not be found`.** The OC integration is compiling
without OC present, which should be impossible — the assembly is guarded. Check that
`PILAR.Context.OpenCommissioning.asmdef` still carries both its `PILAR_OC` define constraint and the
matching `versionDefines` entry, and that the entry's `expression` is empty.

**The exporter reports it cannot decide what to export.** The scene has several root GameObjects and
none is named `Project`. Select the root you want and run the menu item again.

**Duplicate key warnings after a bulk import.** `Add` refuses duplicates, but the serialized list can
still hold them if it was edited outside the API. Use `Set` for upserts, and fix the flagged rows in the
Inspector.
