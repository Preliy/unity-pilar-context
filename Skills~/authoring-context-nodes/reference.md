# Reference — authoring ContextNode entries

## Suggested key vocabulary

Keys are **free-form**. Nothing validates them. This table is a convention so the exported tree stays
consistent enough for a downstream consumer to rely on — follow it unless the machine gives you a
reason not to, and record any new key you introduce in your own copy of this file.

| Tier | Suggested keys | Notes |
|---|---|---|
| `machine` | `Purpose`, `ProcessFlow`, `Safety` | What the line produces, the order stations act in |
| `group` | `Function`, `Process`, `Interfaces`, `Safety` | `Interfaces` = what it hands off to / receives from |
| `assembly` | `Function`, `Logic`, `SubComponents` | `Logic` grounded in the actual sequence script |
| `device` | `Function`, `Signal` | Keep short. `Signal` = what the controller reads/writes here |

Optional anywhere: `Role` (instance-specific, see the prefab rule), `Maintenance`, `Vendor`, `Notes`.

## Three trees, not one

Tiers describe *documentation granularity*. Two paths describe position, and they are different trees:

- **`scenePath`** — the Unity transform hierarchy, arranged for the scene and the CAD import.
- **`topologyPath`** — the `ContextNode` tree, the structure a human authored. Levels you never
  annotated are simply absent from it, and a target with no node has no topology path at all.

A structural target may therefore sit deep in `scenePath` and shallow in `topologyPath`. That gap is
information: it says the intervening levels carry no meaning worth documenting.

The controller's own tree is a **third** axis, and it arrives as `metadata` — an open key/value list
whose keys belong to whichever `IContextMetadataProvider` is installed. Nothing in the package
interprets them. Without a provider, `metadata` is empty everywhere and the tiering collapses to the
structural tiers with no devices at all.

## Reading Open Commissioning's metadata

| Key | Value | Meaning |
|---|---|---|
| `plcPath` | `MAIN.FG_01.P_Reader` | Position in the controller symbol tree |
| `hierarchyRole` | `group` | Opens a level in the controller path, joined with `.` |
| | `sampler` | Opens **no** level; prefixes its children instead, joined with `_`, so the branch stays flat |
| `deviceType` | `SensorBinary` | The device component's type. Present on devices only |
| `aggregatedBy` | a panel name | A panel sampler folded this device into its own single symbol |
| `simulationDevice` | `true` | The device exchanges nothing with the controller and no sampler explains why |

Check whether a structural target carries a `hierarchyRole` before treating it as a controller level,
and when it has none, **say so in the entry** — otherwise a reader will assume it is one. A `plcPath`
proves nothing on its own: every target has one, including pure structure.

Carrying a device component is not the same as being a controller symbol. **Neither `aggregatedBy` nor
`simulationDevice` produces one**, and downstream code generation must skip both — say so in the
entry. Note that such devices may share a resolved `plcPath` with one another, which is harmless
precisely because neither becomes a symbol.

## Aggregating components

Some frameworks let one component gather several children into a single controller symbol — an
operator panel exposing its buttons and lamps through one word, for example. Two consequences worth
writing down when you meet one:

- The children carry `aggregatedBy` naming the parent, and are real devices all the same.
- The **order** of the aggregated list determines the bit assignment, so reordering it silently
  changes what the controller reads. If the project has such a component, record the order in the
  parent's entry.

## Worked example

The point of this example is not the machine — it is the *shape* of the reasoning.

Target `Project/FG_01/Barcode Reader/P_Reader` — tier `device`, components `Rigidbody`, `TagReader`.

What the project already tells you: it is a tag reader, it sits inside the `Barcode Reader` assembly
in `FG_01`, and a sequence script drives that group. Reading that script gives the actual order of
events:

```
gripper extends -> laser ON (1 s) -> laser OFF -> reader window opens
-> read (1 s) -> window closes -> gripper retracts -> OperationComplete
```

So the read happens **after** marking, not before — it verifies the mark rather than gating it. This
is exactly why step 3 of the skill is mandatory: the plausible-sounding assumption ("read the tag,
then mark accordingly") is the opposite of what the code does, and an agent that skipped the source
would have written it backwards with total confidence.

What only the user can tell you: what is actually encoded in the mark, and what happens when the
verification read fails.

Resulting entries:

```bash
unity command context_set --target "Project/FG_01/Barcode Reader/P_Reader" \
  --key "Function" \
  --value "Verification read after laser marking. The reader window opens once the mark is complete and the code is read back to confirm it is legible before the pallet is released."

unity command context_set --target "Project/FG_01/Barcode Reader/P_Reader" \
  --key "Signal" \
  --value "Controller reads the decoded string. In simulation the window is held open for a fixed time and the result is not branched on."
```

Note what the `Function` value does **not** say: "This is a tag reader component." The component list
already carries that. Note also that `Signal` is explicit about simulation not branching on the read
— do not describe intent the code does not implement.

## Verifying a write

```bash
unity command context_get --target "Project/FG_01/Barcode Reader/P_Reader"   # read back through a different command
unity command save_all
unity command context_audit --format json                                    # updated coverage
```
