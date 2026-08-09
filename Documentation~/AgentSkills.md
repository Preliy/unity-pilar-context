# Agent skills

The package supplies the data model, the authoring UI and the export. It does not author content —
that is a job for an engineer, or for an agent working with one.

`Skills~/` ships three [Claude Code](https://claude.com/claude-code) skills that do the second half:
they drive the `context_*` commands against a live Editor to inspect a twin, write `ContextNode`
entries, and check what is still uncovered.

They are not a demo. They encode the workflow that annotated a real Open Commissioning machine —
including the mistakes that cost time the first time round — with that project's specifics removed.

## The three skills

| Skill | Job |
|---|---|
| `inspecting-twin-hierarchy` | Locate a group, station or device; resolve its controller path; see what already carries context |
| `authoring-context-nodes` | Write and review entries, including the prefab asset-vs-instance split |
| `auditing-context-coverage` | Coverage per tier, bulk provisioning, export, and verifying the export |

Used together: **inspect** to locate, **author** to write, **audit** to check.

## Getting them

Either download `pilar-context-skills-<version>.zip` from the
[Releases page](https://github.com/Preliy/unity-pilar-context/releases), or take them from `Skills~/`
in the installed package — a UPM git install copies the whole repository into `PackageCache`, so they
are already on disk.

Copy the three folders into your project's `.claude/skills/`, at the project root beside `Assets/`:

```
<your-unity-project>/
├── Assets/
├── Packages/
└── .claude/skills/
    ├── authoring-context-nodes/
    ├── auditing-context-coverage/
    └── inspecting-twin-hierarchy/
```

## Prerequisites

- A **running Editor** with the project open. Every skill gates on `unity status` reporting `ready`.
- The **Unity CLI**, and **`com.unity.pipeline`** — that package is what registers the `context_*`
  commands.
- Optionally a **twin framework integration**. Without one there are no devices, the framework fields
  export as `""`, and the tiering collapses to `machine` and `group`.

## The loop they implement

1. **Derive everything the project already encodes.** Component types, controller paths, sequence
   logic — all readable, so read them.
2. **Ask the engineer for the rest.** Process intent, operating constraints, what a failure means.
   These are not in the scene and cannot be inferred from it.
3. **Write live** through `context_set`, straight into the running Editor. No scene file editing, no
   reimport; the result is visible in the Inspector immediately.
4. **Audit** with `context_audit` to find what is still uncovered, so the work resumes cleanly across
   sessions.

Step 1 before step 2 is the whole point. An agent that skips it asks the engineer things the code
already answers, and — worse — fills gaps with confident invention. The rule the authoring skill
leads with is **never invent the "why"**: a plausible-sounding `Function` entry is worse than an
empty one, because it will be believed and then generated into PLC code.

## Adapting them

They deliberately carry no counts, scene names or component inventories — those belong to a project,
not to the package. After running `context_audit` on your own scene it is worth recording your
baseline numbers, your naming convention and your framework's component semantics in your copy.

That project-specific knowledge is what makes an agent genuinely useful here rather than merely
fluent, and it is exactly what the shipped copies cannot contain.
