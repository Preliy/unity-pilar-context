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

## Tiers are not the controller project tree

Tiers describe documentation granularity. The controller's project tree is a **separate axis**,
reported as `hierarchyRole` and `plcLinked` on every query.

| Field | Value | Means |
|---|---|---|
| `hierarchyRole` | `group` | Opens a level in the controller path, joined with `.` |
| | `sampler` | Opens **no** level; prefixes its children instead, joined with `_` |
| | *(empty)* | No framework role — Unity grouping only, invisible to the controller |
| `plcLinked` | `true` | A real controller symbol |
| | `false` | A device whose link is off: aggregated into a parent's symbol, or simulation-only |
| | *(empty)* | Not a device |

So a tier-2 `assembly` may be either real controller structure or pure Unity grouping. Check
`hierarchyRole` before treating it as a controller level.

## Quick reference

```bash
# Overview: nested tree, structure only, 2 levels deep
unity command context_tree --scope structural --depth 2

# Every device as a flat list (name, unityPath, plcPath, components, node state)
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

`--target` accepts a **unityPath**, a framework **plcPath** (case-insensitive), or a **bare name**
when it is unique under the root. A name matching several GameObjects returns an error listing a full
path to disambiguate — read it and retry with the path.

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
