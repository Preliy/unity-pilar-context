using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace PILAR.Context.Editor
{
    /// <summary>
    /// Draws one <see cref="ContextEntry"/> as a single-line key over a multi-line value.
    ///
    /// Context values are prose and routinely run to several hundred characters, so the value field
    /// wraps, keeps a comfortable default height, and grows only to a cap — past that it scrolls
    /// internally rather than pushing the rest of the inspector off-screen.
    ///
    /// A synced entry gets the opposite treatment: one compact disabled line. Its value is a short
    /// machine string that never needed the prose field, and editing it would only survive until the
    /// next <see cref="ContextMetadataSync"/> reverted it — so the row says "read this, do not type
    /// here" by being visibly inert.
    /// </summary>
    [CustomPropertyDrawer(typeof(ContextEntry))]
    public class ContextEntryDrawer : PropertyDrawer
    {
        // Roughly four lines visible before the value field starts scrolling, and about fourteen
        // before it stops growing.
        private const float ValueMinHeight = 72f;
        private const float ValueMaxHeight = 260f;

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var keyProperty = property.FindPropertyRelative("key");
            if (ContextMetadataRegistry.IsDerivedKey(keyProperty.stringValue))
                return CreateDerivedGUI(property, keyProperty);

            var root = new VisualElement();
            root.style.marginTop = 4;
            root.style.marginBottom = 8;
            root.style.marginRight = 4;

            var key = new TextField("Key");
            key.BindProperty(keyProperty);
            root.Add(key);

            var value = new TextField()
            {
                multiline = true,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            value.BindProperty(property.FindPropertyRelative("value"));
            value.style.minHeight = ValueMinHeight;
            value.style.maxHeight = ValueMaxHeight;

            // Wrap long prose instead of running off to the right on a single line.
            var input = value.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.whiteSpace = WhiteSpace.Normal;
                input.style.flexGrow = 1;
            }

            // Keep the "Value" label pinned to the top of a tall field.
            value.labelElement.style.alignSelf = Align.FlexStart;
            root.Add(value);

            return root;
        }

        /// <summary>
        /// A synced entry: the key as the label, the value beside it, the whole row disabled. Bound
        /// rather than plain text so it still follows an undo or a fresh sync without a repaint.
        /// </summary>
        private static VisualElement CreateDerivedGUI(
            SerializedProperty property, SerializedProperty keyProperty)
        {
            var row = new TextField(keyProperty.stringValue);
            row.BindProperty(property.FindPropertyRelative("value"));
            row.SetEnabled(false);
            row.style.marginTop = 1;
            row.style.marginBottom = 1;
            row.style.marginRight = 4;

            row.tooltip =
                "Written by a twin framework integration. Change it through the scene or re-sync - " +
                "editing it here would be reverted by the next sync.";

            return row;
        }
    }
}
