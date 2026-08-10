using System.Collections.Generic;
using UnityEngine;

namespace PILAR.Context.Editor
{
    /// <summary>
    /// Builds a node's path through the topology — the logical machine structure the author defines
    /// by placing <see cref="ContextNode"/>s, as opposed to the transform hierarchy, which is
    /// arranged for the scene.
    ///
    /// The topology tree is exactly the ContextNode tree: a Transform without a node has no topology
    /// path at all, and un-annotated levels between two nodes collapse away. This is the package's
    /// own structure, computed without asking any <see cref="IContextMetadataProvider"/>, so it is
    /// available in every project rather than only where a twin framework is installed.
    ///
    /// A node normally hangs under the nearest ancestor that carries a node, but either half of that
    /// can be overridden per node: <see cref="ContextNode.TopologySegment"/> replaces the name, and
    /// <see cref="ContextNode.TopologyParent"/> replaces the parent, which is what lets the topology
    /// diverge from transform parenting entirely.
    /// </summary>
    public static class ContextTopologyPath
    {
        public const string Separator = "/";

        /// <summary>
        /// Root-first topology path, e.g. Project/FG_01/P_Reader. Empty when this Transform carries
        /// no <see cref="ContextNode"/>.
        /// </summary>
        public static string Resolve(Transform t)
        {
            if (t == null) return string.Empty;

            var node = t.GetComponent<ContextNode>();
            if (node == null) return string.Empty;

            var segments = new List<string>();
            // Authoring a TopologyParent can close a loop, and a loop here would hang the Editor
            // rather than produce a wrong string. Truncate at the repeat and say where it is.
            var visited = new HashSet<ContextNode>();

            for (var current = node; current != null; current = ParentOf(current))
            {
                if (!visited.Add(current))
                {
                    Debug.LogWarning(
                        $"PILAR Context: topology parent cycle through '{current.name}' — path truncated. " +
                        "Check the Topology Parent references on this branch.",
                        current);
                    break;
                }

                segments.Add(current.ResolvedSegment);
            }

            segments.Reverse();
            return string.Join(Separator, segments);
        }

        /// <summary>
        /// The node this one hangs under in the topology: its explicit parent when one is set,
        /// otherwise the nearest ancestor carrying a node. A parent reference that was destroyed
        /// reads as null and falls back to the ancestor walk, so a deleted object cannot silently
        /// truncate a path.
        /// </summary>
        public static ContextNode ParentOf(ContextNode node)
        {
            if (node == null) return null;
            if (node.TopologyParent != null) return node.TopologyParent;

            for (var t = node.transform.parent; t != null; t = t.parent)
            {
                var parent = t.GetComponent<ContextNode>();
                if (parent != null) return parent;
            }

            return null;
        }
    }
}
