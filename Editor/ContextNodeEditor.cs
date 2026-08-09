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
    /// Above the list sits derived information the author needs but the node does not store: the
    /// GameObject's other components, plus whatever an installed <see cref="IContextMetadataProvider"/>
    /// contributes (under Open Commissioning, the resolved PLC path and link state). Below it,
    /// duplicate and empty keys surface as warnings rather than being blocked, matching the
    /// ContextNode API's own tolerance.
    /// </summary>
    [CustomEditor(typeof(ContextNode))]
    public class ContextNodeEditor : UnityEditor.Editor
    {
        private const string EntriesField = "_entries";

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

            _warnings = new VisualElement();
            root.Add(_warnings);

            Refresh();
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());

            return root;
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

            var lines = new List<string>
            {
                $"Components: {(components.Length > 0 ? string.Join(", ", components) : "-")}"
            };

            // Only shown when a metadata provider supplies them, so a project without a twin
            // framework installed gets a clean panel instead of a row of dashes.
            var path = ContextMetadataRegistry.Path(node.transform);
            if (!string.IsNullOrEmpty(path)) lines.Insert(0, $"PLC path: {path}");

            lines.AddRange(ContextMetadataRegistry.Notes(node.transform));

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
        }
    }
}
