# PILAR Context — agent skills

Three [Claude Code](https://claude.com/claude-code) skills that drive this package's `context_*` CLI
commands to document a digital twin. They encode the workflow that produced a fully annotated machine
on a real Open Commissioning project — the reasoning and the traps, not that project's data.

| Skill | Use it to |
|---|---|
| `inspecting-twin-hierarchy` | Find a group, station or device and resolve its controller path |
| `authoring-context-nodes` | Write and review `ContextNode` entries |
| `auditing-context-coverage` | Measure coverage, provision gaps, export and verify |

They are designed to be used together: inspect to locate, author to write, audit to check.

## Install

Copy the three skill folders into your Unity project's `.claude/skills/` directory — the project
root, beside `Assets/`, not inside it:

```
<your-unity-project>/
├── Assets/
├── Packages/
└── .claude/
    └── skills/
        ├── authoring-context-nodes/
        ├── auditing-context-coverage/
        └── inspecting-twin-hierarchy/
```

```bash
mkdir -p .claude/skills
cp -r authoring-context-nodes auditing-context-coverage inspecting-twin-hierarchy .claude/skills/
```

Claude Code picks them up on the next session; no registration step.

## Prerequisites

- **A running Unity Editor with the project open.** Every skill gates on `unity status` reporting
  `ready` and refuses to fall back to reading scene files.
- **The Unity CLI**, for `unity command …`.
- **`com.unity.pipeline`**, which is what registers the `context_*` commands. Without it the export
  menu item still works but the skills have nothing to drive.
- **Optionally a twin framework integration** such as `com.open-commissioning.core`. Without one
  there are no devices, `metadata` comes back empty on every target, and the tiering collapses to
  `machine` and `group`. `scenePath` and `topologyPath` are unaffected — they do not come from an
  integration. The skills still work — they just have less to say.

## Adapting them

These are a starting point, not a specification. They deliberately carry no counts, scene names or
component inventories, because those belong to your project rather than to the package. Once you have
run `context_audit` against your own scene, it is worth recording your baseline numbers, your naming
convention and your framework's component semantics directly in your copy — that is exactly the
knowledge that makes an agent useful rather than merely fluent.

Licensed under MIT, same as the package.
