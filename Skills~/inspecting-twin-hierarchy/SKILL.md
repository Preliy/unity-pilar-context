---
name: inspecting-twin-hierarchy
description: Use when you need to find or understand a part of a digital twin scene from the terminal - locating a functional group, station, or device, resolving its controller path, or checking which parts of the machine already carry context.
---

# Inspecting the twin hierarchy

## Overview

A production twin scene is mostly CAD mesh noise — tens of thousands of GameObjects, of which only a
few hundred mean anything. **Never read the scene YAML to answer a structural question.** Those files
run to hundreds of thousands of lines, and prefab instances are not expanded in them, so the real
hierarchy is not even visible there.

Query the **live Unity Editor** instead: a round-trip costs about a second and returns the semantic
machine structure with controller paths already resolved.

## Always gate on a live Editor first

```bash
unity status --format json     # need an instance with state "ready"
```

No ready Editor means no inspection. Ask the user to open the project — do not fall back to grepping
`.unity` or `.prefab` files, and do not guess.

## The four tiers

Every query classifies targets into tiers. Learn this vocabulary; the commands use it.

| Tier | What | How it is identified |
|---|---|---|
| `machine` | Export root | the root GameObject you walk from, `Project` by default |
| `group` | Functional group | direct child of the root |
| `assembly` | Station / sub-assembly | not a device, but has at least one device below it |
| `device` | Controller-linked device | recognised as a device by the installed integration |

Anything else — CAD geometry, interaction colliders, kinematic joints — is **not** a context target
and will not appear in any listing.

**Device-ness comes from an installed integration**, not from this package. With no
`IContextMetadataProvider` present there are no devices at all, so `assembly` collapses too and you
are left with `machine` and `group`. If a scene you expect to be full of devices reports none, that
is the first thing to check.

## Three axes, not one

Every target reports three independent things. Do not read one for another.

| Field | What it is |
|---|---|
| `tier` / `tierName` | Documentation granularity: `machine`, `group`, `assembly`, `device` |
| `scenePath` | Where the object sits in the Unity hierarchy |
| `topologyPath` | Where it sits in the authored `ContextNode` tree — un-annotated levels are absent, so this is shorter than `scenePath` and empty for a target with no node |
| `entryCount` / `entryKeys` | **Authored** entries only. This is coverage |
| `derivedCount` | How many entries a sync wrote under a framework's namespace |

A node keeps one dictionary holding both kinds. `context_get` returns all of it as `entries`; a key
with no prefix is a human's, a prefixed key belongs to an installed integration and was written by
`context_sync`. **Never write a prefixed key** — `context_set` refuses them, because the next sync
would revert the edit.

The vocabulary is open: nothing in the package defines or interprets those keys, and an absent key
means the framework did not state that fact, not that it is false. Under Open Commissioning you will
see:

| Key | Value | Means |
|---|---|---|
| `oc.plcPath` | `MAIN.FG_01.P_Reader` | Position in the controller symbol tree |
| `oc.hierarchyRole` | `group` | Opens a level in the controller path, joined with `.` |
| | `sampler` | Opens **no** level; prefixes its children instead, joined with `_` |
| `oc.deviceType` | `SensorBinary` | The device component's type. Present on devices only |
| `oc.aggregatedBy` | a panel name | A panel sampler folded this device into its own single symbol |
| `oc.simulationDevice` | `true` | The device talks to no controller and no sampler explains why |

So a tier-2 `assembly` may be either real controller structure or pure Unity grouping. Check whether
it carries an `oc.hierarchyRole` before treating it as a controller level. A target is a real
controller symbol when it has an `oc.deviceType` and **neither** `oc.aggregatedBy` nor
`oc.simulationDevice` — every target resolves an `oc.plcPath`, including pure structure, so the path
alone proves nothing.

**A target with `derivedCount: 0` was never synced**, which is not the same as a framework having
nothing to say about it. Run `context_sync --dry_run true` before concluding anything from an absence.

## Quick reference

```bash
# Overview: nested tree, structure only, 2 levels deep
unity command context_tree --scope structural --depth 2

# Every device as a flat list (name, both paths, components, coverage counts)
unity command context_tree --scope devices --flat --format json

# What still has no context?
unity command context_tree --scope missing --flat --format json

# One target in full, addressed three different ways — all equivalent
unity command context_get --target "Project/FG_01/Barcode Reader/P_Reader"
unity command context_get --target "MAIN.FG_01.P_Reader"
unity command context_get --target "P_Reader"

# Coverage numbers per tier
unity command context_audit --format json
```

`--target` accepts a **scenePath**, a **topologyPath**, the value of any **prefixed entry** that is
unique under the root (case-insensitive — this is how a framework path like `MAIN.FG_01.P_Reader`
still resolves, and it reads the node rather than the framework, so it works even where that framework
is not installed), or a **bare name** when it is unique. An ambiguous value or name returns an error
listing a full path to disambiguate — read it and retry with the path.

`--scope` takes `all | structural | devices | missing`, where `missing` means *no `ContextNode` at
all, or one with zero entries* — an empty node is not coverage.

## Naming conventions

Names are usually load-bearing in industrial projects: the prefix tells you what a thing is before
you read any component list. Many projects follow DIN/EN 81346 or a house variant of it, for example:

| Prefix | Typically means |
|---|---|
| `FG_` | Functional group |
| `Y_` | Valve / pneumatic actuator |
| `B_` | Binary sensor |
| `M_` | Motor / drive |
| `P_` | Optical inspection device |
| `H_` | HMI / signalling |
| `SS_` | Safety switch |

Learn the project's actual convention from its own naming before relying on any table. And watch for
names carrying trailing spaces — match them exactly.

## When the commands do not cover it

For structural questions with no dedicated command, run C# in the Editor:

```bash
unity command eval --code 'return UnityEngine.GameObject.Find("Project").transform.childCount;'
```

Prefer the `context_*` commands — they already resolve paths and tiers. Reach for `eval` only for
one-off queries, and keep it read-only; use `authoring-context-nodes` to write.

## Common mistakes

- **Reading the scene file directly.** Enormous, and prefab instances are not expanded, so you will
  not even see the real hierarchy. Query the Editor.
- **Assuming a short path.** A device is often nested deeper than its name suggests. Let
  `context_get` resolve the name for you rather than constructing a path.
- **Treating every framework component as a device.** Kinematic joints and interaction helpers are
  not devices; only the types the integration recognises are.
