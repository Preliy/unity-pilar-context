using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace PILAR.Context.Editor
{
    /// <summary>
    /// UI Toolkit inspector for <see cref="ContextNode"/>.
    ///
    /// The entry list is a plain <see cref="PropertyField"/> over the serialized array, so reordering,
    /// add and remove come from Unity's own list control rather than hand-managed binding. Each row is
    /// drawn by <see cref="ContextEntryDrawer"/>, which supplies the multi-line scrollable value field.
    ///
    /// Above the list sits the little the node does not store — its resolved topology path and the
    /// GameObject's other components. Framework metadata is not repeated there: it is in the list,
    /// drawn as compact disabled rows, and showing it twice would only give the two views a chance
    /// to disagree.
    ///
    /// Below the list, a Sync button and the two topology overrides in a collapsed foldout — both
    /// untouched in the ordinary case, and pushing them down keeps the authored entries as the thing
    /// the inspector is about. Duplicate and empty keys surface as warnings rather than being
    /// blocked, matching the ContextNode API's own tolerance.
    /// </summary>
    [CustomEditor(typeof(ContextNode))]
    public class ContextNodeEditor : UnityEditor.Editor
    {
        private const string EntriesField = "_entries";
        private const string TopologySegmentField = "_topologySegment";
        private const string TopologyParentField = "_topologyParent";

        private HelpBox _derivedInfo;
        private VisualElement _warnings;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            _derivedInfo = new HelpBox(string.Empty, HelpBoxMessageType.None);
            root.Add(_derivedInfo);

            var entries = new PropertyField(serializedObject.FindProperty(EntriesField), "Context Entries");
            entries.style.marginTop = 4;
            root.Add(entries);

            root.Add(BuildSyncButton());
            root.Add(BuildTopologyOverrides());

            _warnings = new VisualElement();
            root.Add(_warnings);

            Refresh();
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());

            return root;
        }

        /// <summary>
        /// Pulls this node's framework metadata into the list above. Hidden entirely when nothing is
        /// installed to sync from, rather than sitting there doing nothing.
        /// </summary>
        private VisualElement BuildSyncButton()
        {
            var container = new VisualElement();
            if (ContextMetadataRegistry.Providers.Count == 0) return container;

            var button = new Button(SyncSelected) { text = "Sync Framework Metadata" };
            button.style.marginTop = 2;
            button.tooltip =
                "Rewrites the entries owned by an installed twin framework. Authored entries are " +
                "never touched.";
            container.Add(button);

            return container;
        }

        private void SyncSelected()
        {
            foreach (var node in targets.OfType<ContextNode>())
            {
                Undo.RecordObject(node, "Sync context metadata");
                if (!ContextMetadataSync.Apply(node)) continue;
                EditorUtility.SetDirty(node);
            }

            serializedObject.Update();
            Refresh();
        }

        private VisualElement BuildTopologyOverrides()
        {
            var foldout = new Foldout { text = "Topology", value = false };
            foldout.style.marginTop = 4;

            foldout.Add(new HelpBox(
                "Empty means the GameObject's name and the nearest ancestor Context Node. " +
                "Set these only where the topology should differ from the scene hierarchy.",
                HelpBoxMessageType.None));

            foldout.Add(new PropertyField(
                serializedObject.FindProperty(TopologySegmentField), "Segment"));
            foldout.Add(new PropertyField(
                serializedObject.FindProperty(TopologyParentField), "Parent"));

            return foldout;
        }

        private void Refresh()
        {
            RefreshDerivedInfo();
            RefreshWarnings();
        }

        private void RefreshDerivedInfo()
        {
            if (_derivedInfo == null || target == null) return;

            var node = (ContextNode)target;
            var components = ContextComponentFilter.GetRelevantComponentNames(node.gameObject).ToArray();

            // Only what the node genuinely does not store. Framework metadata used to be listed here;
            // it now lives in the entry list itself, and repeating it would just be two views of the
            // same data disagreeing whenever one is stale.
            var lines = new List<string>
            {
                $"Topology: {ContextTopologyPath.Resolve(node.transform)}",
                $"Components: {(components.Length > 0 ? string.Join(", ", components) : "-")}"
            };

            _derivedInfo.text = string.Join("\n", lines);
        }

        private void RefreshWarnings()
        {
            if (_warnings == null || target == null) return;
            _warnings.Clear();

            var node = (ContextNode)target;

            var duplicateKeys = node.Entries
                .GroupBy(entry => entry.key)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            if (duplicateKeys.Count > 0)
            {
                _warnings.Add(new HelpBox(
                    $"Duplicate keys: {string.Join(", ", duplicateKeys)}. Keys must be unique per node.",
                    HelpBoxMessageType.Warning));
            }

            if (node.Entries.Any(entry => string.IsNullOrEmpty(entry.key)))
            {
                _warnings.Add(new HelpBox(
                    "One or more entries have an empty key.",
                    HelpBoxMessageType.Warning));
            }

            // A separator inside a segment makes the topology path unsplittable downstream. Warned
            // rather than stripped: silently rewriting an authored name is worse than saying so.
            if (node.TopologySegment != null && node.TopologySegment.Contains(ContextTopologyPath.Separator))
            {
                _warnings.Add(new HelpBox(
                    $"The topology segment contains '{ContextTopologyPath.Separator}', which separates " +
                    "path levels. Downstream readers cannot split this path correctly.",
                    HelpBoxMessageType.Warning));
            }
        }
    }
}
