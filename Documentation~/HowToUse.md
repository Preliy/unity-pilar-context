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

The `ContextNode` inspector shows four things:

1. **A derived-information panel** — the resolved topology path, the GameObject's other components
   (filtered so `Transform`, `Collider`, `Renderer` and `MeshFilter` noise does not drown out the
   meaningful ones), and one row per metadata entry an installed integration contributes, under that
   integration's own key. This information is *shown, not stored*, and the metadata rows are absent
   entirely when no integration supplies any.
2. **The entry list** — Unity's own reorderable list, so drag-to-reorder, add and remove behave as you
   expect. Each row has a single-line key field over a word-wrapping, scrolling value field, since good
   context is usually a sentence or two rather than a word.
3. **The Topology foldout** — the two optional overrides, `Segment` and `Parent`, collapsed by default
   because the common case leaves both empty. See [Topology](#topologypath--the-tree-you-author).
4. **Warnings** — duplicate keys, empty keys, and a topology segment containing `/` surface as warnings
   rather than being blocked, which matches the API's own tolerance and keeps you from losing
   half-typed work.

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

**PILAR ▸ Context ▸ Export Machine Context (JSON)** walks the transform hierarchy and writes
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
| `scenePath` | Slash path through the Unity scene from the export root, e.g. `Project/Geometry/FG_01/P_Reader` |
| `topologyPath` | Slash path through the `ContextNode` tree, e.g. `Project/FG_01/P_Reader`. `""` for a node-less object |
| `metadata` | Key/value facts an installed integration contributes, under its own keys. `[]` when none does |
| `components` | Filtered component type names |
| `context` | The authored key/value entries, in author order |
| `children` | Nested nodes, in sibling order |

A node describes itself three ways: where it sits in the scene, where it sits in the structure you
authored, and what a framework knows about it. Only the last is framework-specific, and it stays as
opaque key/value pairs so the schema itself never names a framework's concepts.

```json
{
  "name": "P_Reader",
  "scenePath": "Project/Geometry/FG_01/P_Reader",
  "topologyPath": "Project/FG_01/P_Reader",
  "metadata": [
    { "key": "plcPath", "value": "MAIN.FG_01.P_Reader" },
    { "key": "hierarchyRole", "value": "group" }
  ],
  "components": ["Sensor"],
  "context": [{ "key": "Function", "value": "Reads the DMC on the carrier." }],
  "children": []
}
```

### `topologyPath` — the tree you author

The topology is the logical machine structure, and it is exactly the `ContextNode` tree: a node hangs
under the nearest ancestor that also carries a node, and objects without a node are not in it at all.
A CAD wrapper you never annotated therefore appears in `scenePath` and vanishes from `topologyPath`.

Two optional overrides on `ContextNode` shape it, both in the inspector's **Topology** foldout:

- **Segment** — the name this node contributes. Empty means the GameObject's name.
- **Parent** — the node it hangs under. Empty means the nearest ancestor carrying a node. Setting it
  lets the topology diverge from transform parenting entirely, which is the point: transform parenting
  is arranged for the scene, and the machine's structure often is not the same shape.

A segment containing `/` is flagged in the inspector — it would make the path unsplittable downstream.
A `Parent` chain that loops back on itself is truncated with a console warning rather than hanging.

### `metadata` — an open vocabulary

Keys are chosen by whichever integration is installed; nothing in the package interprets them. An
absent key means the framework did not state that fact, which is not the same as stating it false.

Under Open Commissioning:

| Key | Value | Meaning |
|---|---|---|
| `plcPath` | `MAIN.FG_01.P_Reader` | Position in the PLC symbol tree |
| `hierarchyRole` | `group` | A `Hierarchy` that opens a level in that path, joined with `.` |
| `hierarchyRole` | `sampler` | A `Hierarchy` with `IsNameSampler`: opens no level, prefixes its children's names with `_` instead, so a group like `FG_Transport` stays flat |
| `deviceType` | `SensorBinary` | The `IDevice` component's type. Present on devices only |
| `aggregatedBy` | a panel's name | A `PanelSampler` folded this device into its own single symbol |
| `simulationDevice` | `true` | The device exchanges no data with the controller and no sampler accounts for that |

**A node is a real PLC symbol when it has a `deviceType` and neither of the last two keys.** Every
Transform resolves a `plcPath`, including pure structure, so `deviceType` is what separates a device
from a wrapper in the JSON export, which carries no tier field.

`aggregatedBy` and `simulationDevice` are **inferred**, not read: OC has no simulation flag, and
`Link.Enable == false` carries both meanings at once. Separating them is what makes either claim
truthful — a `PanelSampler` force-disables its members' links at `Start`, so without the split every
panel member would read as simulation-only. Code generation must skip both.

## CLI commands

`Editor/Pipeline/` registers `[CliCommand]` tools so the annotated hierarchy can be read and written
from a terminal against a live Editor, via the Unity CLI and `com.unity.pipeline`.

| Command | Purpose |
|---|---|
| `context_tree` | List targets (nested or flat) with tier, both paths, metadata, components, node state |
| `context_get` | Read one target's full entries |
| `context_set` | Upsert entries — a single `key`/`value`, or an `entries` JSON array |
| `context_remove` | Remove one entry by key |
| `context_ensure` | Add empty `ContextNode`s in bulk (dry-run by default) |
| `context_audit` | Coverage per tier, plus lists of missing and empty nodes |

**Addressing.** Targets are named, in this order, by `scenePath`
(`Project/FG_01/Barcode Reader/P_Reader`), by `topologyPath`, by any metadata value that is unique
under the root (case-insensitive — this is what keeps an OC `plcPath` like `MAIN.FG_01.P_Reader`
working as a handle), or by a bare GameObject name when that name is unique. An ambiguous value or
name is an error that tells you the full path to use instead.

**What `context_audit` does not report.** Coverage only: totals, per-tier counts, and the lists of
missing and empty nodes. It deliberately does not aggregate over framework vocabulary — that would
mean the neutral pipeline hard-coding one framework's key names. Read `context_tree`'s `metadata` and
group it yourself.

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

`Skills~/` ships three Claude Code skills that implement exactly that loop — inspect, author, audit.
See **[AgentSkills.md](AgentSkills.md)**, or download `pilar-context-skills-<version>.zip` from the
[Releases page](https://github.com/Preliy/unity-pilar-context/releases).

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
        IEnumerable<ContextEntry> ResolveMetadata(Transform t);   // -> metadata, under your own keys
    }
}
```

Only three questions, and only the first two are structural. Everything else your framework knows
leaves through `ResolveMetadata` as key/value pairs you name yourself — the package neither defines
nor interprets the vocabulary, which is why adding a framework never means editing the schema.

Implement it in an Editor assembly, give the class a public parameterless constructor, and it is picked
up automatically through `TypeCache` — there is no registration call. `ContextMetadataRegistry` merges
the installed providers in `Order`; the first provider to answer for a key owns it, and a later one can
only add keys nobody claimed, so several can coexist.

Every method receives arbitrary Transforms from anywhere in the scene. Return an empty sequence
(or `false`) rather than throwing when a Transform means nothing to your framework, and omit a key
rather than emitting a blank value for it — the registry drops blanks, because an absent fact and a
blank one are different statements.

Note what you do **not** implement: `topologyPath` is computed by the package from the `ContextNode`
tree, so it works with no provider installed at all.

`PILAR.Context.OpenCommissioning` is the reference implementation and worth reading: one small file,
in an assembly guarded by a `com.open-commissioning.core` version define so it vanishes cleanly when OC
is absent.

## Troubleshooting

**The `context_*` commands are missing.** `com.unity.pipeline` is not installed. It is an experimental
Unity package; add `"com.unity.pipeline": "0.4.0-exp.1"` to your manifest. The rest of the package works
without it.

**`metadata` is empty on every node.** No metadata provider is installed. Add
`com.open-commissioning.core`, or implement `IContextMetadataProvider` for your own framework. The
`topologyPath` is unaffected — it does not come from a provider.

**`topologyPath` is empty, or shorter than expected.** The topology is exactly the `ContextNode` tree,
so an object without a node has no topology path and an un-annotated level between two nodes collapses
away. Run `context_ensure` to add nodes in bulk, or set the **Parent** override where the structure
should not follow transform parenting.

**`CS0246: The type or namespace name 'OC' could not be found`.** The OC integration is compiling
without OC present, which should be impossible — the assembly is guarded. Check that
`PILAR.Context.OpenCommissioning.asmdef` still carries both its `PILAR_OC` define constraint and the
matching `versionDefines` entry, and that the entry's `expression` is empty.

**The exporter reports it cannot decide what to export.** The scene has several root GameObjects and
none is named `Project`. Select the root you want and run the menu item again.

**Duplicate key warnings after a bulk import.** `Add` refuses duplicates, but the serialized list can
still hold them if it was edited outside the API. Use `Set` for upserts, and fix the flagged rows in the
Inspector.
