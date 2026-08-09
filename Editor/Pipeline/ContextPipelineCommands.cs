using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UndoScope = Unity.Pipeline.Editor.Authoring.AuthoringUndoScope;

namespace PILAR.Context.Pipeline
{
    /// <summary>
    /// Unity CLI command surface for authoring <see cref="ContextNode"/> data on a live Editor.
    ///
    /// Every mutation goes through the public ContextNode API (Set/Remove) rather than the serialized
    /// backing list, so writes are genuine upserts — a partial write can never clobber unrelated keys.
    /// </summary>
    public static class ContextPipelineCommands
    {
        // ---------------------------------------------------------------- read

        [CliCommand("context_tree", "List the digital twin's context targets (machine/group/assembly/device) with their PLC paths and current ContextNode state.")]
        public static object ContextTree(
            [CliArg("scope", "Which targets to include: all | structural | devices | missing.")] string scope = "all",
            [CliArg("root", "Root GameObject name to walk from.")] string root = ContextTargets.DefaultRoot,
            [CliArg("flat", "Return a flat list instead of a nested tree.")] bool flat = false,
            [CliArg("depth", "Maximum depth to return, 0 for unlimited.")] int depth = 0)
        {
            var rootTf = ContextTargets.ResolveRoot(root);
            var matched = ContextTargets.Enumerate(rootTf, scope).ToList();

            if (flat)
            {
                var list = matched
                    .Select(t => ContextTargets.Describe(t, rootTf))
                    .OrderBy(i => i.unityPath, StringComparer.Ordinal)
                    .ToList();
                return new { root = rootTf.name, scope, count = list.Count, targets = list };
            }

            var keep = new HashSet<Transform>(matched);
            var tree = BuildTree(rootTf, rootTf, keep, depth, 0);
            return new { root = rootTf.name, scope, count = matched.Count, tree };
        }

        [CliCommand("context_get", "Read the full ContextNode entries of one target, addressed by unityPath, plcPath, or unique name.")]
        public static object ContextGet(
            [CliArg("target", "unityPath (Project/FG_01/P_Reader), plcPath (MAIN.FG_01.P_Reader), or a unique GameObject name.", Required = true)] string target,
            [CliArg("root", "Root GameObject name to resolve against.")] string root = ContextTargets.DefaultRoot)
        {
            var rootTf = ContextTargets.ResolveRoot(root);
            var t = ContextTargets.Resolve(target, rootTf);
            var info = ContextTargets.Describe(t, rootTf);
            var node = t.GetComponent<ContextNode>();

            return new
            {
                info.name,
                info.unityPath,
                info.plcPath,
                info.tier,
                info.tierName,
                info.plcLinked,
                info.hierarchyRole,
                info.components,
                info.hasNode,
                info.prefabAsset,
                entries = node != null
                    ? node.Entries.Select(e => new { e.key, e.value }).ToArray()
                    : Array.Empty<object>()
            };
        }

        [CliCommand("context_audit", "Report context coverage per tier: how many targets exist, carry a ContextNode, and have at least one entry.")]
        public static object ContextAudit(
            [CliArg("scope", "Which targets to audit: all | structural | devices | missing.")] string scope = "all",
            [CliArg("root", "Root GameObject name to walk from.")] string root = ContextTargets.DefaultRoot)
        {
            var rootTf = ContextTargets.ResolveRoot(root);
            var infos = ContextTargets.Enumerate(rootTf, scope)
                .Select(t => ContextTargets.Describe(t, rootTf))
                .ToList();

            var byTier = infos
                .GroupBy(i => i.tier)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    tier = g.Key,
                    tierName = ContextTargets.TierName(g.Key),
                    total = g.Count(),
                    withNode = g.Count(i => i.hasNode),
                    nonEmpty = g.Count(i => i.entryCount > 0),
                    plcLinked = g.Count(i => i.plcLinked == true)
                })
                .ToList();

            return new
            {
                root = rootTf.name,
                scope,
                total = infos.Count,
                withNode = infos.Count(i => i.hasNode),
                nonEmpty = infos.Count(i => i.entryCount > 0),
                plcLinked = infos.Count(i => i.plcLinked == true),
                byTier,
                // The project tree is defined by the twin framework's own components, not by transform
                // parenting. Groups open a PLC path level; samplers flatten into a name prefix instead.
                projectTree = new
                {
                    groups = infos.Where(i => i.hierarchyRole == "group")
                        .Select(i => i.plcPath).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                    nameSamplers = infos.Where(i => i.hierarchyRole == "sampler")
                        .Select(i => i.plcPath).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                    // Structural nodes with no framework role: meaningful in Unity, invisible to the PLC.
                    unityOnlyGrouping = infos
                        .Where(i => string.IsNullOrEmpty(i.hierarchyRole) && i.plcLinked == null && i.tier > 0)
                        .Select(i => i.unityPath).OrderBy(p => p, StringComparer.Ordinal).ToArray()
                },
                // Devices that exist but do not talk to the PLC — aggregated by a sampler, or
                // simulation-only. Code generation must skip these.
                devicesWithoutPlcLink = infos.Where(i => i.plcLinked == false)
                    .Select(i => i.unityPath).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                missingNode = infos.Where(i => !i.hasNode)
                    .Select(i => i.unityPath).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                emptyNode = infos.Where(i => i.hasNode && i.entryCount == 0)
                    .Select(i => i.unityPath).OrderBy(p => p, StringComparer.Ordinal).ToArray()
            };
        }

        // --------------------------------------------------------------- write

        [CliCommand("context_set", "Upsert context entries on a target. Pass key+value for a single entry, or entries for a JSON array. Existing keys not mentioned are preserved.")]
        public static object ContextSet(
            [CliArg("target", "unityPath, plcPath, or a unique GameObject name.", Required = true)] string target,
            [CliArg("key", "Single entry key to upsert.")] string key = null,
            [CliArg("value", "Single entry value to upsert.")] string value = null,
            [CliArg("entries", "JSON array of {\"key\":...,\"value\":...} to upsert in one call.")] string entries = null,
            [CliArg("prefab", "Write to the backing prefab asset instead of the scene instance.")] bool prefab = false,
            [CliArg("root", "Root GameObject name to resolve against.")] string root = ContextTargets.DefaultRoot)
        {
            var pending = BuildPending(key, value, entries);
            if (pending.Count == 0)
                throw new ArgumentException("Nothing to write — supply key and value, or an entries JSON array.");

            var rootTf = ContextTargets.ResolveRoot(root);
            var t = ContextTargets.Resolve(target, rootTf);
            var info = ContextTargets.Describe(t, rootTf);

            if (prefab)
                return WriteToPrefab(t, info, pending, remove: false);

            using (new UndoScope($"Set context on {t.name}"))
            {
                var node = EnsureNode(t.gameObject);
                Undo.RecordObject(node, "Set context entries");
                foreach (var e in pending) node.Set(e.key, e.value);
                EditorUtility.SetDirty(node);
                MarkDirty(t);
            }

            return new
            {
                target = info.unityPath,
                info.plcPath,
                info.tierName,
                wroteTo = "scene",
                applied = pending.Select(e => e.key).ToArray(),
                totalEntries = t.GetComponent<ContextNode>().Entries.Count
            };
        }

        [CliCommand("context_remove", "Remove one context entry by key from a target.")]
        public static object ContextRemove(
            [CliArg("target", "unityPath, plcPath, or a unique GameObject name.", Required = true)] string target,
            [CliArg("key", "Entry key to remove.", Required = true)] string key,
            [CliArg("prefab", "Write to the backing prefab asset instead of the scene instance.")] bool prefab = false,
            [CliArg("root", "Root GameObject name to resolve against.")] string root = ContextTargets.DefaultRoot)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("key is required.");

            var rootTf = ContextTargets.ResolveRoot(root);
            var t = ContextTargets.Resolve(target, rootTf);
            var info = ContextTargets.Describe(t, rootTf);
            var pending = new List<ContextEntry> { new ContextEntry { key = key } };

            if (prefab)
                return WriteToPrefab(t, info, pending, remove: true);

            var node = t.GetComponent<ContextNode>();
            if (node == null)
                return new { target = info.unityPath, removed = false, reason = "No ContextNode on this target." };

            bool removed;
            using (new UndoScope($"Remove context key on {t.name}"))
            {
                Undo.RecordObject(node, "Remove context entry");
                removed = node.Remove(key);
                EditorUtility.SetDirty(node);
                MarkDirty(t);
            }

            return new { target = info.unityPath, wroteTo = "scene", key, removed, totalEntries = node.Entries.Count };
        }

        [CliCommand("context_ensure", "Add an empty ContextNode to every target that lacks one. Use dry_run to preview, and mode to choose scene objects or backing prefab assets.")]
        public static object ContextEnsure(
            [CliArg("scope", "Which targets to cover: all | structural | devices | missing.")] string scope = "all",
            [CliArg("mode", "Where to add: scene (non-prefab objects) | prefabs (backing assets) | all.")] string mode = "scene",
            [CliArg("dry_run", "Report what would change without writing.")] bool dryRun = true,
            [CliArg("root", "Root GameObject name to walk from.")] string root = ContextTargets.DefaultRoot)
        {
            var m = string.IsNullOrWhiteSpace(mode) ? "scene" : mode.Trim().ToLowerInvariant();
            if (m != "scene" && m != "prefabs" && m != "all")
                throw new ArgumentException($"Unknown mode '{mode}'. Expected: scene | prefabs | all.");

            var rootTf = ContextTargets.ResolveRoot(root);
            var targets = ContextTargets.Enumerate(rootTf, scope)
                .Where(t => t.GetComponent<ContextNode>() == null)
                .ToList();

            var sceneTargets = targets.Where(t => ContextTargets.PrefabAssetPath(t) == null).ToList();
            var prefabTargets = targets.Where(t => ContextTargets.PrefabAssetPath(t) != null).ToList();

            var addedScene = new List<string>();
            var addedPrefab = new List<string>();
            var skipped = new List<string>();

            if (m == "scene" || m == "all")
            {
                foreach (var t in sceneTargets)
                {
                    addedScene.Add(ContextTargets.UnityPath(t, rootTf));
                    if (dryRun) continue;
                    using (new UndoScope($"Add ContextNode to {t.name}"))
                    {
                        EnsureNode(t.gameObject);
                        MarkDirty(t);
                    }
                }
            }

            if (m == "prefabs" || m == "all")
            {
                // Many scene instances share one prefab asset — collapse to distinct (asset, childPath).
                var slots = prefabTargets
                    .Select(t =>
                    {
                        var ok = ContextTargets.TryGetPrefabSlot(t, out var asset, out var rel);
                        return new { ok, asset, rel };
                    })
                    .Where(x => x.ok)
                    .GroupBy(x => x.asset + "|" + x.rel)
                    .Select(g => g.First())
                    .OrderBy(x => x.asset, StringComparer.Ordinal)
                    .ToList();

                foreach (var slot in slots)
                {
                    var label = string.IsNullOrEmpty(slot.rel) ? slot.asset : slot.asset + " :: " + slot.rel;
                    if (dryRun)
                    {
                        addedPrefab.Add(label);
                        continue;
                    }

                    var contents = PrefabUtility.LoadPrefabContents(slot.asset);
                    try
                    {
                        var inner = string.IsNullOrEmpty(slot.rel)
                            ? contents.transform
                            : contents.transform.Find(slot.rel);
                        if (inner == null)
                        {
                            // Never silent: a slot we cannot resolve is a bug, not a no-op.
                            skipped.Add(label + " (child not found in asset)");
                            continue;
                        }
                        if (inner.GetComponent<ContextNode>() != null)
                        {
                            skipped.Add(label + " (already has a ContextNode)");
                            continue;
                        }

                        inner.gameObject.AddComponent<ContextNode>();
                        PrefabUtility.SaveAsPrefabAsset(contents, slot.asset);
                        addedPrefab.Add(label);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                }
            }

            return new
            {
                root = rootTf.name,
                scope,
                mode = m,
                dryRun,
                missingTotal = targets.Count,
                sceneCandidates = sceneTargets.Count,
                prefabSlots = prefabTargets.Count,
                addedScene = addedScene.ToArray(),
                addedPrefab = addedPrefab.ToArray(),
                skipped = skipped.ToArray()
            };
        }

        // -------------------------------------------------------------- helpers

        private static ContextTargetInfo BuildTree(
            Transform t, Transform root, HashSet<Transform> keep, int maxDepth, int depth)
        {
            var info = ContextTargets.Describe(t, root);
            info.children = new List<ContextTargetInfo>();

            if (maxDepth > 0 && depth >= maxDepth) return info;

            for (var i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                // Keep a branch when the child itself matched, or when anything below it did.
                var childMatched = keep.Contains(child);
                var subtreeMatched = childMatched ||
                    child.GetComponentsInChildren<Transform>(true).Any(keep.Contains);
                if (!subtreeMatched) continue;
                info.children.Add(BuildTree(child, root, keep, maxDepth, depth + 1));
            }

            return info;
        }

        private static ContextNode EnsureNode(GameObject go)
        {
            var node = go.GetComponent<ContextNode>();
            if (node != null) return node;
            return Undo.AddComponent<ContextNode>(go);
        }

        private static void MarkDirty(Transform t)
        {
            if (t.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }

        [Serializable]
        private class EntryListWrapper
        {
            public List<ContextEntry> entries = new List<ContextEntry>();
        }

        private static List<ContextEntry> BuildPending(string key, string value, string entriesJson)
        {
            var pending = new List<ContextEntry>();

            if (!string.IsNullOrWhiteSpace(key))
                pending.Add(new ContextEntry { key = key, value = value ?? string.Empty });

            if (string.IsNullOrWhiteSpace(entriesJson)) return pending;

            var trimmed = entriesJson.Trim();
            var wrapped = trimmed.StartsWith("[") ? "{\"entries\":" + trimmed + "}" : trimmed;

            EntryListWrapper parsed;
            try
            {
                parsed = JsonUtility.FromJson<EntryListWrapper>(wrapped);
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    "Could not parse 'entries'. Expected a JSON array like " +
                    "[{\"key\":\"Function\",\"value\":\"...\"}]. " + ex.Message);
            }

            if (parsed?.entries == null) return pending;

            foreach (var e in parsed.entries)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.key)) continue;
                pending.RemoveAll(p => p.key == e.key);
                pending.Add(new ContextEntry { key = e.key, value = e.value ?? string.Empty });
            }

            return pending;
        }

        private static object WriteToPrefab(
            Transform t, ContextTargetInfo info, List<ContextEntry> pending, bool remove)
        {
            if (!ContextTargets.TryGetPrefabSlot(t, out var assetPath, out var rel))
                throw new ArgumentException(
                    $"'{info.unityPath}' is not part of a prefab instance — omit prefab to write to the scene.");

            var contents = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var inner = string.IsNullOrEmpty(rel) ? contents.transform : contents.transform.Find(rel);
                if (inner == null)
                    throw new ArgumentException(
                        $"Could not locate '{rel}' inside prefab asset '{assetPath}'.");

                var applied = new List<string>();

                if (remove)
                {
                    var node = inner.GetComponent<ContextNode>();
                    foreach (var e in pending)
                        if (node != null && node.Remove(e.key)) applied.Add(e.key);
                }
                else
                {
                    var node = inner.GetComponent<ContextNode>() ?? inner.gameObject.AddComponent<ContextNode>();
                    foreach (var e in pending)
                    {
                        node.Set(e.key, e.value);
                        applied.Add(e.key);
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(contents, assetPath);

                var total = inner.GetComponent<ContextNode>() != null
                    ? inner.GetComponent<ContextNode>().Entries.Count
                    : 0;

                return new
                {
                    target = info.unityPath,
                    info.plcPath,
                    info.tierName,
                    wroteTo = "prefab",
                    prefabAsset = assetPath,
                    prefabChild = rel,
                    applied = applied.ToArray(),
                    totalEntries = total
                };
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
