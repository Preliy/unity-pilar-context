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
            var root = new VisualElement();
            root.style.marginTop = 4;
            root.style.marginBottom = 8;
            root.style.marginRight = 4;

            var key = new TextField("Key");
            key.BindProperty(property.FindPropertyRelative("key"));
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
    }
}
