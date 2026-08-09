---
name: auditing-context-coverage
description: Use when checking how much of a digital twin has been documented, finding which parts still lack context, or exporting the annotated machine hierarchy for downstream use.
---

# Auditing context coverage

## Overview

Coverage is the gate between authoring and export. The exported JSON is only as useful as it is
complete, and a gap is invisible in the export itself — a node with no entries just looks like a
node. Measure before you ship it downstream.

Keys are free-form, so the audit checks **presence and non-emptiness only**. It never validates key
names.

## Coverage

```bash
unity status --format json                       # need state "ready"
unity command context_audit --format json
```

Returns per-tier `total` / `withNode` / `nonEmpty`, plus `missingNode[]` (no `ContextNode` at all)
and `emptyNode[]` (has one, zero entries). Scope it with
`--scope all | structural | devices | missing`.

Two numbers matter, and they are easy to confuse:

- **`withNode`** should reach `total` once provisioning has run. That is mechanical, and reaching it
  proves nothing about documentation.
- **`nonEmpty`** is the real progress measure, and it only moves through `authoring-context-nodes`.

Establish the project's own baseline by running the audit before you start. There are no universal
numbers to compare against.

## Provisioning the gaps

`context_ensure` adds empty `ContextNode` components. It is **dry-run by default** — always inspect
the report before applying.

```bash
unity command context_ensure --scope all --mode scene                      # preview
unity command context_ensure --scope all --mode scene   --dry_run false    # apply
unity command context_ensure --scope all --mode prefabs --dry_run false    # backing assets
unity command save_all
```

`--mode scene` covers plain scene objects, `--mode prefabs` writes to the prefab assets behind
prefab-instance members (one write per distinct asset + child, not per instance), `--mode all` does
both. **Run `prefabs` first** so scene objects do not pick up redundant overrides.

## Exporting

The export is an existing Editor menu item; drive it rather than reimplementing it.

```bash
unity command menu --path "PILAR/Context/Export Machine Context (JSON)"
```

Writes `Assets/StreamingAssets/<SceneName>_Context.json` — a nested tree of
`{name, unityPath, plcPath, plcLinked, hierarchyRole, components, context[], children[]}`, pruned to
semantically relevant structure.

## Verify the export, do not assume it

```bash
python -c "
import json, sys
path = sys.argv[1]
d = json.load(open(path))
n = [0]; depth = [0]
def walk(x, k=0):
    n[0] += 1; depth[0] = max(depth[0], k)
    for c in x.get('children', []): walk(c, k+1)
walk(d['root'])
print('nodes', n[0], 'maxDepth', depth[0], 'scene', d['sceneName'])
" Assets/StreamingAssets/<SceneName>_Context.json
```

The exporter uses `JsonUtility`, which **silently drops data nested past roughly 7–10 levels**. Record
the node count and max depth for your scene, and re-check them whenever the hierarchy grows. If either
drops unexpectedly, the serializer is truncating and the export can no longer be trusted.

## Check for malformed entries too

Coverage counts entries; it does not inspect them. Prefab override reconciliation can leave an entry
with a blank key and value, which counts as coverage but carries nothing:

```bash
python -c "
import json, sys
d = json.load(open(sys.argv[1]))
bad = []
def walk(x):
    for e in x.get('context', []):
        if not e.get('key','').strip() or not e.get('value','').strip():
            bad.append((x['unityPath'], e.get('key')))
    for c in x.get('children', []): walk(c)
walk(d['root'])
print('malformed entries:', bad or 'NONE')
" Assets/StreamingAssets/<SceneName>_Context.json
```

Run this after any batch that wrote to prefab assets.

## Cross-check against the controller's own tree

If your framework can export its device tree independently, diff the two: every device the export
marks `plcLinked: "true"` should appear there, and nothing should appear that the export missed. A
mismatch means the twin and the controller project have drifted, and the export must not be trusted
for code generation until they agree.

`context_audit` reports the same structure under `projectTree` (`groups`, `nameSamplers`,
`unityOnlyGrouping`) and lists the exceptions under `devicesWithoutPlcLink`.

## Common mistakes

- **Reporting coverage from `withNode`.** An empty node is not documentation. Report `nonEmpty`.
- **Applying `context_ensure` without reading the dry run.** It is dry-run by default for a reason.
- **Exporting before `save_all`.** The export reads live scene state, so it includes unsaved edits —
  which are then lost if the Editor closes without saving. Save first.
