using System.Collections.Generic;
using OC;
using OC.Communication;
using PILAR.Context.Editor;
using UnityEngine;

namespace PILAR.Context.OpenCommissioning
{
    /// <summary>
    /// Teaches PILAR Context about Open Commissioning: which GameObjects are devices, where they sit
    /// in the PLC symbol tree, and whether they actually exchange data with the controller.
    ///
    /// This whole assembly is excluded when com.open-commissioning.core is not installed (see the
    /// PILAR_OC define constraint), so the provider simply does not exist in a project without OC —
    /// no reflection, no soft references, and no compile errors. Everything else in the package
    /// keeps working; the derived fields just come back empty.
    ///
    /// Discovered automatically by <see cref="ContextMetadataRegistry"/>; nothing registers it.
    /// </summary>
    public class OpenCommissioningMetadataProvider : IContextMetadataProvider
    {
        public int Order => 0;

        /// <summary>
        /// A subtree matters when it contains any OC component or any Hierarchy node. The raw CAD
        /// hierarchy of a real machine is overwhelmingly mesh geometry — in the reference project
        /// roughly 7,300 GameObjects — so without this test an export is almost entirely noise.
        /// </summary>
        public bool IsRelevant(Transform subtreeRoot)
        {
            if (subtreeRoot == null) return false;
            if (subtreeRoot.GetComponentsInChildren<IComponent>(true).Length > 0) return true;
            if (subtreeRoot.GetComponentsInChildren<Hierarchy>(true).Length > 0) return true;
            return false;
        }

        public bool IsDevice(Transform t)
        {
            return t != null && t.GetComponent<IDevice>() != null;
        }

        public string ResolvePath(Transform t)
        {
            return t == null ? string.Empty : ContextPlcPath.Resolve(t);
        }

        /// <summary>
        /// Carrying an IDevice is not the same as being a PLC symbol: a device whose Link.Enable is
        /// false is internal only — either simulation-side feedback, or a component a PanelSampler
        /// aggregates into its parent's single symbol. Downstream code generation must not emit
        /// symbols for those. Null means "not a device", which is a different claim from "false".
        /// </summary>
        public bool? ResolveLinkState(Transform t)
        {
            if (t == null) return null;
            var device = t.GetComponent<IDevice>();
            return device?.Link?.Enable;
        }

        /// <summary>
        /// "group"   — a Hierarchy that opens a new level in the PLC path (joined with '.').
        /// "sampler" — a Hierarchy with IsNameSampler set: it opens no level and prefixes its
        ///             children's names instead (joined with '_'), so FG_Transport stays flat.
        /// ""        — no Hierarchy. Unity transform grouping only; invisible to the PLC tree.
        /// </summary>
        public string ResolveRole(Transform t)
        {
            if (t == null) return string.Empty;

            var hierarchy = t.GetComponent<Hierarchy>();
            if (hierarchy == null) return string.Empty;

            return hierarchy.IsNameSampler ? "sampler" : "group";
        }

        public IEnumerable<string> InspectorNotes(Transform t)
        {
            if (t == null) yield break;

            var linked = ResolveLinkState(t);
            if (linked.HasValue)
            {
                yield return linked.Value
                    ? "PLC linked: yes"
                    : "PLC linked: no (internal device — no PLC symbol is generated)";
            }

            var role = ResolveRole(t);
            if (!string.IsNullOrEmpty(role)) yield return $"Hierarchy role: {role}";
        }
    }
}
