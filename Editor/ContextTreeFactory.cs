using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public List<string> components;
        /// <summary>
        /// The node's whole dictionary, in the order it is stored: what a human wrote, and whatever
        /// an installed framework contributed under its own namespace ("oc.plcPath"). One list,
        /// because that is how the node itself holds it — this export writes down what the node
        /// knows rather than assembling a view of it.
        /// </summary>
        public List<ContextEntry> entries;
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
    /// Disabled GameObjects are skipped, along with everything under them. A scene keeps disabled
    /// variants of the same station side by side — the same names in the same places — so exporting
    /// them produces two nodes that are indistinguishable by <c>scenePath</c>, and a consumer keyed
    /// by path silently reads one of them. What the machine is right now is what the export states.
    ///
    /// A node is described by two computed paths — where it sits in the scene, where it sits in the
    /// topology the author defined — and by its own entry dictionary, copied out verbatim. Nothing
    /// here consults a provider for content: framework facts reach the node through
    /// <see cref="ContextMetadataSync"/> beforehand, so the export writes down what the scene stores
    /// rather than assembling a fresh view of it, and the schema never names a framework's concepts.
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

            return ToJson(exportRoot, prettyPrint);
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
                components = ContextComponentFilter.GetRelevantComponentNames(origin.gameObject).ToList(),
                entries = contextNode != null ? contextNode.Entries.ToList() : new List<ContextEntry>(),
                children = children
            };
        }

        private static bool HasMeaningfulContent(Transform subtreeRoot)
        {
            if (!subtreeRoot.gameObject.activeSelf) return false;
            if (HasReachableContextNode(subtreeRoot)) return true;

            // Providers answer for a whole subtree by contract and cannot be asked about one
            // Transform, so a branch whose only framework content is disabled still passes here. It
            // then exports as an empty wrapper rather than as its disabled contents, which is the
            // safe way round: over-keeping shows a level that exists, under-keeping hides one.
            return ContextMetadataRegistry.AnyRelevant(subtreeRoot);
        }

        /// <summary>
        /// Whether an authored node sits here or anywhere below, reached without passing through a
        /// disabled object. <c>GetComponentsInChildren</c> answers a different question either way:
        /// including inactive objects finds nodes the export will not emit, and excluding them tests
        /// <c>activeInHierarchy</c>, which would empty the whole export when the chosen root itself
        /// is disabled.
        /// </summary>
        private static bool HasReachableContextNode(Transform subtreeRoot)
        {
            if (subtreeRoot.GetComponent<ContextNode>() != null) return true;

            for (var i = 0; i < subtreeRoot.childCount; i++)
            {
                var child = subtreeRoot.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                if (HasReachableContextNode(child)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------- serialization

        /// <summary>
        /// <c>JsonUtility</c> cannot write this tree. Its serializer stops at ten levels of nesting
        /// and drops everything below, leaving nothing but a console warning behind — and every
        /// exported level costs two of those ten (the node, then its children list), so a machine
        /// hierarchy reaches the ceiling at around five levels and the export quietly goes short.
        /// Writing the JSON here keeps it exact at any depth.
        ///
        /// The shape is unchanged, so <see cref="ContextExportRoot"/> still states it and existing
        /// consumers read the same document. Depth is bounded by the hierarchy alone.
        /// </summary>
        private static string ToJson(ContextExportRoot export, bool prettyPrint)
        {
            var writer = new JsonWriter(prettyPrint);

            writer.BeginObject();
            writer.Value("sceneName", export.sceneName);
            writer.Value("generatedAtUtc", export.generatedAtUtc);
            writer.Name("root");
            WriteNode(writer, export.root);
            writer.EndObject();

            return writer.ToString();
        }

        private static void WriteNode(JsonWriter writer, ContextExportNode node)
        {
            writer.BeginObject();
            writer.Value("name", node.name);
            writer.Value("scenePath", node.scenePath);
            writer.Value("topologyPath", node.topologyPath);

            writer.BeginArray("components");
            foreach (var component in node.components) writer.Value(component);
            writer.EndArray();

            writer.BeginArray("entries");
            foreach (var entry in node.entries)
            {
                writer.BeginObject();
                writer.Value("key", entry.key);
                writer.Value("value", entry.value);
                writer.EndObject();
            }
            writer.EndArray();

            writer.BeginArray("children");
            foreach (var child in node.children) WriteNode(writer, child);
            writer.EndArray();

            writer.EndObject();
        }

        /// <summary>
        /// Just enough JSON for the export shape: objects, arrays and strings, with no depth limit of
        /// its own. Callers must balance their own Begin/End calls — this is a private helper for the
        /// one writer above, not a general-purpose serializer.
        /// </summary>
        private sealed class JsonWriter
        {
            private const int IndentWidth = 4;

            private readonly StringBuilder _text = new StringBuilder();
            private readonly bool _prettyPrint;
            private int _depth;
            // Whether the container being written is still empty, which is what decides both the
            // separating comma and whether a closing bracket needs a line of its own.
            private bool _empty = true;
            // A member name has just been written and its value comes next, so that value must not
            // separate itself from the name with a comma and a line break.
            private bool _named;

            public JsonWriter(bool prettyPrint)
            {
                _prettyPrint = prettyPrint;
            }

            public void BeginObject() => Open('{');

            public void EndObject() => Close('}');

            public void BeginArray(string name)
            {
                Name(name);
                Open('[');
            }

            public void EndArray() => Close(']');

            /// <summary>Writes a named string member, e.g. <c>"scenePath": "Project/FG_01"</c>.</summary>
            public void Value(string name, string value)
            {
                Name(name);
                Value(value);
            }

            /// <summary>Writes a string on its own: an array element, or a named member's value.</summary>
            public void Value(string value)
            {
                Separate();
                AppendString(value);
            }

            /// <summary>Writes a member name, leaving the caller to write its value next.</summary>
            public void Name(string name)
            {
                Separate();
                AppendString(name);
                _text.Append(':');
                if (_prettyPrint) _text.Append(' ');
                _named = true;
            }

            public override string ToString() => _text.ToString();

            private void Open(char bracket)
            {
                Separate();
                _text.Append(bracket);
                _depth++;
                _empty = true;
            }

            private void Close(char bracket)
            {
                _depth--;
                if (!_empty) Break();
                _text.Append(bracket);
                _empty = false;
            }

            /// <summary>Places what comes next inside its container: comma, line break, indent.</summary>
            private void Separate()
            {
                if (_named)
                {
                    _named = false;
                    return;
                }

                if (!_empty) _text.Append(',');
                Break();
                _empty = false;
            }

            private void Break()
            {
                if (!_prettyPrint || _text.Length == 0) return;
                _text.Append('\n').Append(' ', _depth * IndentWidth);
            }

            private void AppendString(string value)
            {
                _text.Append('"');

                foreach (var c in value ?? string.Empty)
                {
                    switch (c)
                    {
                        case '"': _text.Append("\\\""); break;
                        case '\\': _text.Append("\\\\"); break;
                        case '\b': _text.Append("\\b"); break;
                        case '\f': _text.Append("\\f"); break;
                        case '\n': _text.Append("\\n"); break;
                        case '\r': _text.Append("\\r"); break;
                        case '\t': _text.Append("\\t"); break;
                        default:
                            // Everything else goes through as-is; the file is written as UTF-8, so a
                            // name carrying an umlaut stays readable instead of becoming escapes.
                            if (c < ' ') _text.Append("\\u").Append(((int)c).ToString("x4"));
                            else _text.Append(c);
                            break;
                    }
                }

                _text.Append('"');
            }
        }
    }
}
