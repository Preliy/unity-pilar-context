using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PILAR.Context.Editor
{
    [Serializable]
    public class ContextExportNode
    {
        public string name;
        public string unityPath;
        /// <summary>
        /// Framework-side logical path, supplied by an <see cref="IContextMetadataProvider"/> — the
        /// PLC symbol path under Open Commissioning. Empty when no provider is installed.
        /// </summary>
        public string plcPath;
        /// <summary>
        /// "true" / "false" for devices, "" for everything else. A device whose link is disabled
        /// exchanges no data with the controller — it is aggregated into a parent sampler's symbol,
        /// or exists only for simulation. Downstream code generation must not emit a symbol for
        /// those. String rather than bool? because JsonUtility cannot serialize nullable value types.
        /// </summary>
        public string plcLinked;
        /// <summary>
        /// This node's role in the framework's own tree, which its components define — not Unity
        /// transform parenting. Under Open Commissioning: "group" opens a level in the PLC path
        /// (joined with '.'); "sampler" opens none and prefixes its children's names instead (joined
        /// with '_'); "" means the node is Unity-side grouping the PLC never sees.
        /// </summary>
        public string hierarchyRole;
        public List<string> components;
        public List<ContextEntry> context;
        public List<ContextExportNode> children;
    }

    [Serializable]
    internal class ContextExportRoot
    {
        public string sceneName;
        public string generatedAtUtc;
        public ContextExportNode root;
    }

    /// <summary>
    /// Walks the Unity transform hierarchy and prunes nodes that carry no context and no framework
    /// meaning anywhere in their subtree, so the export stays limited to semantically relevant
    /// machine structure rather than CAD geometry.
    ///
    /// Relevance comes from two independent sources: an authored <see cref="ContextNode"/> always
    /// counts, and any installed <see cref="IContextMetadataProvider"/> may additionally vouch for a
    /// subtree. With no provider installed the export is simply narrower — every node a human
    /// annotated, and nothing else.
    /// </summary>
    public static class ContextTreeFactory
    {
        public static ContextExportNode Build(Transform root)
        {
            return BuildNode(root, null);
        }

        public static string BuildJson(Transform root, bool prettyPrint = true)
        {
            var exportRoot = new ContextExportRoot
            {
                sceneName = SceneManager.GetActiveScene().name,
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                root = Build(root)
            };

            return JsonUtility.ToJson(exportRoot, prettyPrint);
        }

        private static ContextExportNode BuildNode(Transform origin, string parentPath)
        {
            var unityPath = string.IsNullOrEmpty(parentPath) ? origin.name : parentPath + "/" + origin.name;

            var children = new List<ContextExportNode>();
            for (var i = 0; i < origin.childCount; i++)
            {
                var child = origin.GetChild(i);
                if (!HasMeaningfulContent(child)) continue;
                children.Add(BuildNode(child, unityPath));
            }

            var contextNode = origin.GetComponent<ContextNode>();
            var linked = ContextMetadataRegistry.LinkState(origin);

            return new ContextExportNode
            {
                name = origin.name,
                unityPath = unityPath,
                plcPath = ContextMetadataRegistry.Path(origin),
                plcLinked = linked == null
                    ? string.Empty
                    : linked.Value ? "true" : "false",
                hierarchyRole = ContextMetadataRegistry.Role(origin),
                components = ContextComponentFilter.GetRelevantComponentNames(origin.gameObject).ToList(),
                context = contextNode != null ? contextNode.Entries.ToList() : new List<ContextEntry>(),
                children = children
            };
        }

        private static bool HasMeaningfulContent(Transform subtreeRoot)
        {
            if (subtreeRoot.GetComponentsInChildren<ContextNode>(true).Length > 0) return true;
            return ContextMetadataRegistry.AnyRelevant(subtreeRoot);
        }
    }
}
