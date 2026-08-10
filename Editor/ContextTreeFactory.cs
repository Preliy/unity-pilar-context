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
        /// <summary>Where the object sits in the Unity scene, e.g. Project/Geometry/FG_01/P_Reader.</summary>
        public string scenePath;
        /// <summary>
        /// Where the object sits in the topology — the logical structure the author defines by
        /// placing <see cref="ContextNode"/>s, which skips scene levels that carry no node. Empty
        /// for a node-less object. See <see cref="ContextTopologyPath"/>.
        /// </summary>
        public string topologyPath;
        /// <summary>
        /// Facts an installed <see cref="IContextMetadataProvider"/> knows about this object, under
        /// keys the framework chooses — a framework-side path, a structural role, whether the object
        /// is simulated. Empty when no provider is installed or none has anything to say. An absent
        /// key means the framework did not state that fact, not that the fact is false.
        /// </summary>
        public List<ContextEntry> metadata;
        public List<string> components;
        /// <summary>The authored key/value context — the point of the whole export.</summary>
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
    ///
    /// Each node is described three ways: where it sits in the scene, where it sits in the topology
    /// the author defined, and whatever a framework knows about it. Only the last of those is
    /// framework-specific, and it stays as opaque key/value metadata so the schema itself never
    /// names a framework's concepts.
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
            var scenePath = string.IsNullOrEmpty(parentPath) ? origin.name : parentPath + "/" + origin.name;

            var children = new List<ContextExportNode>();
            for (var i = 0; i < origin.childCount; i++)
            {
                var child = origin.GetChild(i);
                if (!HasMeaningfulContent(child)) continue;
                children.Add(BuildNode(child, scenePath));
            }

            var contextNode = origin.GetComponent<ContextNode>();

            return new ContextExportNode
            {
                name = origin.name,
                scenePath = scenePath,
                topologyPath = ContextTopologyPath.Resolve(origin),
                metadata = ContextMetadataRegistry.Metadata(origin).ToList(),
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
