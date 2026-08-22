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

`total` counts **enabled** targets only. Disabling a branch removes it and everything under it from
every count, so a total that drops between runs usually means someone switched something off, not that
work was lost. A disabled object can still be addressed by name — `context_get` and `context_set` reach
it — it just stays out of the audit and the export until it is enabled.

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
`{name, scenePath, topologyPath, components, entries[], children[]}`, pruned to semantically relevant
structure.

**Disabled objects are not exported**, nor is anything under them — and `context_audit` skips them on
the same rule, so coverage is measured against what the export actually writes. If something you
documented is missing from both, check whether it is switched off before suspecting the tools.

`entries` is each node's whole dictionary. A bare key is authored; a prefixed one (`oc.plcPath`) was
written by `context_sync` from an installed twin framework.

**Sync before you export.** The export dumps what the nodes hold and queries no framework itself, so
an unsynced scene exports no metadata at all and looks complete while doing it:

```bash
unity command context_sync --dry_run true     # what would change
unity command context_sync --dry_run false    # write it
unity command menu --path "PILAR/Context/Export Machine Context (JSON)"
```

The dry run is also the only drift check there is: a rename or a deleted component leaves stored
values wrong until someone syncs again.

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

Record the node count and max depth for your scene, and re-check them after a batch: a count that moves
when you did not expect it to means the scene changed under you — most often something was disabled,
which removes it and its whole subtree from the export.

Read the file with a real JSON parser, as above. **`JsonUtility.FromJson` stops at ten levels of
nesting** and returns a silently truncated tree, so it cannot read a machine export back — the exporter
writes the document itself for the same reason, and no longer has that limit.

## Check for malformed entries too

Coverage counts entries; it does not inspect them. Prefab override reconciliation can leave an entry
with a blank key and value, which counts as coverage but carries nothing:

```bash
python -c "
import json, sys
d = json.load(open(sys.argv[1]))
bad = []
def walk(x):
    for e in x.get('entries', []):
        if '.' in e.get('key',''): continue          # synced, not authored
        if not e.get('key','').strip() or not e.get('value','').strip():
            bad.append((x['scenePath'], e.get('key')))
    for c in x.get('children', []): walk(c)
walk(d['root'])
print('malformed entries:', bad or 'NONE')
" Assets/StreamingAssets/<SceneName>_Context.json
```

Run this after any batch that wrote to prefab assets.

## Cross-check against the controller's own tree

If your framework can export its device tree independently, diff the two: every device that should be
a controller symbol must appear there, and nothing should appear that the export missed. A mismatch
means the twin and the controller project have drifted, and the export must not be trusted for code
generation until they agree.

Under Open Commissioning a node is a controller symbol when its entries carry `oc.deviceType` and
neither `oc.aggregatedBy` nor `oc.simulationDevice`. Do not filter on `oc.plcPath` alone — every node
has one, including pure structure.

`context_audit` deliberately reports **coverage only** — totals, per-tier counts, missing and empty
nodes. It does not group by framework vocabulary, because the pipeline does not own that vocabulary.
Build the controller-side view yourself from the prefixed entries:

```bash
python -c "
import json, sys, collections
d = json.load(open(sys.argv[1]))
facets = collections.defaultdict(collections.Counter)
def walk(x):
    for e in x.get('entries', []):
        if '.' in e['key']: facets[e['key']][e['value']] += 1
    for c in x.get('children', []): walk(c)
walk(d['root'])
for k, v in facets.items(): print(k, dict(v))
" Assets/StreamingAssets/<SceneName>_Context.json
```

Under Open Commissioning that prints the `oc.hierarchyRole` split (`group` / `sampler`) and the count
of devices carrying `oc.aggregatedBy` or `oc.simulationDevice` — the ones that produce no controller
symbol. An empty result means the scene was never synced, not that the framework knows nothing.

## Common mistakes

- **Reporting coverage from `withNode`.** An empty node is not documentation. Report `nonEmpty`.
- **Applying `context_ensure` without reading the dry run.** It is dry-run by default for a reason.
- **Exporting before `save_all`.** The export reads live scene state, so it includes unsaved edits —
  which are then lost if the Editor closes without saving. Save first.
