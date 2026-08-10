using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CtxEditor = PILAR.Context.Editor;

namespace PILAR.Context.Pipeline
{
    /// <summary>
    /// Classifies GameObjects in a twin into the four context tiers, and resolves the addressing
    /// schemes (<c>scenePath</c>, <c>topologyPath</c>, a framework path from metadata, bare name)
    /// the CLI commands accept.
    ///
    /// Tier predicates are evaluated in order — self-is-device is checked before has-device-in-subtree,
    /// because devices nest (e.g. Y_CapsSource contains M_Conveyor).
    ///
    /// Device-ness and metadata come from <see cref="CtxEditor.ContextMetadataRegistry"/> rather than
    /// any specific twin framework, so these commands work — with a flatter tier structure — in a
    /// project with no provider installed.
    /// </summary>
    public static class ContextTargets
    {
        /// <summary>Root GameObject name assumed when a command is not told otherwise.</summary>
        public const string DefaultRoot = "Project";

        public const int TierMachine = 0;
        public const int TierGroup = 1;
        public const int TierAssembly = 2;
        public const int TierDevice = 3;
        public const int TierNone = -1;

        public static string TierName(int tier) => tier switch
        {
            TierMachine => "machine",
            TierGroup => "group",
            TierAssembly => "assembly",
            TierDevice => "device",
            _ => "none"
        };

        public static Transform ResolveRoot(string root)
        {
            var name = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
            var go = GameObject.Find(name);
            if (go == null)
                throw new ArgumentException($"Root GameObject '{name}' not found in the active scene.");
            return go.transform;
        }

        /// <summary>
        /// Which context tier this transform belongs to, or <see cref="TierNone"/> when it is not a
        /// context target at all (CAD geometry, interaction colliders, wrappers with no device below).
        /// </summary>
        public static int GetTier(Transform t, Transform root)
        {
            if (t == root) return TierMachine;
            if (t.parent == root) return TierGroup;
            if (CtxEditor.ContextMetadataRegistry.AnyDevice(t)) return TierDevice;
            if (HasDeviceInSubtree(t)) return TierAssembly;
            return TierNone;
        }

        private static bool HasDeviceInSubtree(Transform t)
        {
            return t.GetComponentsInChildren<Transform>(true)
                .Any(CtxEditor.ContextMetadataRegistry.AnyDevice);
        }

        public static IEnumerable<Transform> Enumerate(Transform root)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Where(t => GetTier(t, root) != TierNone);
        }

        /// <summary>Targets matching a scope filter: all | structural | devices | missing.</summary>
        public static IEnumerable<Transform> Enumerate(Transform root, string scope)
        {
            var s = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim().ToLowerInvariant();
            var all = Enumerate(root);

            return s switch
            {
                "all" => all,
                "structural" => all.Where(t => GetTier(t, root) != TierDevice),
                "devices" => all.Where(t => GetTier(t, root) == TierDevice),
                // A node holding only synced metadata is still undocumented, so "missing" counts
                // authored entries rather than the list length.
                "missing" => all.Where(t => !AuthoredEntries(t.GetComponent<ContextNode>()).Any()),
                _ => throw new ArgumentException(
                    $"Unknown scope '{scope}'. Expected: all | structural | devices | missing.")
            };
        }

        /// <summary>Slash-delimited scene path from <paramref name="root"/> inclusive, e.g. Project/FG_01/P_Reader.</summary>
        public static string ScenePath(Transform t, Transform root)
        {
            var parts = new List<string>();
            var cur = t;
            while (cur != null)
            {
                parts.Add(cur.name);
                if (cur == root) break;
                cur = cur.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Path relative to an ancestor, empty when they are the same transform.</summary>
        public static string RelativePath(Transform t, Transform ancestor)
        {
            if (t == ancestor) return string.Empty;
            var parts = new List<string>();
            var cur = t;
            while (cur != null && cur != ancestor)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            if (cur == null)
                throw new ArgumentException($"'{t.name}' is not a descendant of '{ancestor.name}'.");
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Accepts a scenePath (Project/FG_01/P_Reader), a topologyPath, a framework path stored on
        /// the node as namespaced metadata (under Open Commissioning, MAIN.FG_01.P_Reader), or a bare
        /// GameObject name — the last two only when they identify exactly one object under the root.
        ///
        /// The metadata step is what keeps a framework's own addressing usable without this file
        /// knowing which key carries it: any namespaced value may be a handle, so long as it is
        /// unique. It reads the node's stored entries rather than asking a provider, so it still
        /// resolves in a project where that framework is not installed.
        /// </summary>
        public static Transform Resolve(string target, Transform root)
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("target is required.");

            var needle = target.Trim();
            var all = root.GetComponentsInChildren<Transform>(true);

            foreach (var t in all)
                if (ScenePath(t, root) == needle) return t;

            foreach (var t in all)
                if (CtxEditor.ContextTopologyPath.Resolve(t) == needle) return t;

            var byMetadata = all.Where(t => HasMetadataValue(t, needle)).ToList();
            if (byMetadata.Count == 1) return byMetadata[0];
            if (byMetadata.Count > 1)
                throw new ArgumentException(
                    $"Target '{needle}' is ambiguous — {byMetadata.Count} GameObjects publish that " +
                    $"metadata value. Use a full scenePath instead, e.g. '{ScenePath(byMetadata[0], root)}'.");

            var byName = all.Where(t => t.name == needle).ToList();
            if (byName.Count == 1) return byName[0];
            if (byName.Count > 1)
                throw new ArgumentException(
                    $"Target '{needle}' is ambiguous — {byName.Count} GameObjects share that name. " +
                    $"Use a full scenePath instead, e.g. '{ScenePath(byName[0], root)}'.");

            throw new ArgumentException(
                $"No GameObject found for target '{needle}' under '{root.name}'. " +
                "Expected a scenePath (Project/FG_01/P_Reader), a topologyPath, a framework path from " +
                "metadata, or a unique name.");
        }

        private static bool HasMetadataValue(Transform t, string needle)
        {
            var node = t.GetComponent<ContextNode>();
            if (node == null) return false;

            return node.Entries.Any(entry =>
                CtxEditor.ContextMetadataRegistry.IsNamespacedKey(entry.key) &&
                string.Equals(entry.value, needle, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Prefab asset path backing this object, or null when it is a plain scene object.</summary>
        public static string PrefabAssetPath(Transform t)
        {
            return PrefabUtility.IsPartOfPrefabInstance(t.gameObject)
                ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)
                : null;
        }

        /// <summary>
        /// Locates the object inside the backing prefab asset that this scene object instantiates,
        /// as (asset path, child path within that asset).
        ///
        /// Resolved through <c>GetCorrespondingObjectFromSource</c> rather than by matching names
        /// down from the instance root: an instance may rename or reorder its children (Camera Typ 2
        /// ships a child named "Light" that the scene instance renames to "H_CameraLight"), and a
        /// name-matched path silently fails to resolve inside the asset.
        /// </summary>
        public static bool TryGetPrefabSlot(Transform t, out string assetPath, out string childPath)
        {
            assetPath = null;
            childPath = null;

            if (!PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) return false;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (source == null) return false;

            assetPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(assetPath)) return false;

            var assetRoot = source.transform;
            while (assetRoot.parent != null) assetRoot = assetRoot.parent;

            childPath = RelativePath(source.transform, assetRoot);
            return true;
        }

        public static string[] Components(Transform t)
        {
            return CtxEditor.ContextComponentFilter.GetRelevantComponentNames(t.gameObject).ToArray();
        }

        /// <summary>
        /// The entries a human wrote, which is what coverage means. A node's list also holds whatever
        /// a sync wrote under a provider's namespace, and counting those as documentation would report
        /// a fully synced, entirely undocumented scene as complete.
        /// </summary>
        public static IEnumerable<ContextEntry> AuthoredEntries(ContextNode node)
        {
            if (node == null) return Enumerable.Empty<ContextEntry>();
            return node.Entries.Where(e => !CtxEditor.ContextMetadataRegistry.IsDerivedKey(e.key));
        }

        public static ContextTargetInfo Describe(Transform t, Transform root)
        {
            var node = t.GetComponent<ContextNode>();
            var tier = GetTier(t, root);
            var authored = AuthoredEntries(node).ToArray();

            return new ContextTargetInfo
            {
                name = t.name,
                scenePath = ScenePath(t, root),
                topologyPath = CtxEditor.ContextTopologyPath.Resolve(t),
                tier = tier,
                tierName = TierName(tier),
                components = Components(t),
                hasNode = node != null,
                entryCount = authored.Length,
                entryKeys = authored.Select(e => e.key).ToArray(),
                derivedCount = node != null ? node.Entries.Count - authored.Length : 0,
                prefabAsset = PrefabAssetPath(t)
            };
        }
    }

    /// <summary>Flat description of one context target. Serialized straight into command responses.</summary>
    public class ContextTargetInfo
    {
        public string name;
        /// <summary>Position in the Unity scene hierarchy, from the walk root inclusive.</summary>
        public string scenePath;
        /// <summary>Position in the ContextNode topology; empty when this target carries no node.</summary>
        public string topologyPath;
        public int tier;
        public string tierName;
        public string[] components;
        public bool hasNode;
        /// <summary>Authored entries only — derived ones are not documentation and never count here.</summary>
        public int entryCount;
        /// <summary>Authored keys only, in node order.</summary>
        public string[] entryKeys;
        /// <summary>How many entries a sync wrote under an installed provider's namespace.</summary>
        public int derivedCount;
        public string prefabAsset;
        public List<ContextTargetInfo> children;
    }
}
