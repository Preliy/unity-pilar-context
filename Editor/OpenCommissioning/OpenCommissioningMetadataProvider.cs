using System.Collections.Generic;
using System.Linq;
using OC;
using OC.Communication;
using OC.Interactions;
using PILAR.Context.Editor;
using UnityEngine;

namespace PILAR.Context.OpenCommissioning
{
    /// <summary>
    /// Teaches PILAR Context about Open Commissioning: which GameObjects are devices, and what OC
    /// knows about them that the package cannot work out on its own.
    ///
    /// Everything OC-specific leaves through <see cref="ResolveMetadata"/> as key/value pairs, so
    /// OC's vocabulary lives in this assembly and nowhere else. The package's own structure — the
    /// scene path and the topology path — is computed without asking this provider anything.
    ///
    /// This whole assembly is excluded when com.open-commissioning.core is not installed (see the
    /// PILAR_OC define constraint), so the provider simply does not exist in a project without OC —
    /// no reflection, no soft references, and no compile errors. Everything else in the package
    /// keeps working; nodes just carry no metadata.
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

        /// <summary>
        /// What OC knows about this Transform:
        ///
        /// plcPath          — its position in the PLC symbol tree, e.g. MAIN.FG_01.P_Reader.
        /// hierarchyRole    — "group" for a Hierarchy that opens a level in that path (joined with
        ///                    '.'), "sampler" for one with IsNameSampler set, which opens no level
        ///                    and prefixes its children's names instead (joined with '_').
        /// deviceType       — the IDevice component's type. Present only on devices, so its absence
        ///                    is how a reader tells a device from structure: every Transform resolves
        ///                    a plcPath, only a device carries this.
        /// aggregatedBy     — the PanelSampler that folded this device into its own single symbol.
        /// simulationDevice — the device exchanges no data with the controller and no sampler
        ///                    accounts for that, so it exists for the simulation alone.
        ///
        /// A device with a deviceType and neither of the last two is a real PLC symbol.
        ///
        /// The last two are <b>inferred</b>, not read. OC has no simulation flag: ISimulationBehaviour
        /// has no implementers in OC core and SimulationBehaviourManager is a scene-global runtime
        /// toggle. The only signal is Link.Enable, which OC overloads — a PanelSampler force-disables
        /// its members' links at Start, so a disabled link means "aggregated" for those and
        /// "simulation-only" for everything else. Splitting the two is what makes either claim
        /// truthful, and the split is only as good as the sampler search below.
        /// </summary>
        public IEnumerable<ContextEntry> ResolveMetadata(Transform t)
        {
            if (t == null) yield break;

            var path = ContextPlcPath.Resolve(t);
            if (!string.IsNullOrEmpty(path)) yield return Entry("plcPath", path);

            if (t.TryGetComponent<Hierarchy>(out var hierarchy))
                yield return Entry("hierarchyRole", hierarchy.IsNameSampler ? "sampler" : "group");

            var device = t.GetComponent<IDevice>();
            if (device == null) yield break;

            yield return Entry("deviceType", device.GetType().Name);

            if (device.Link == null || device.Link.Enable) yield break;

            var sampler = FindAggregatingSampler(t);
            yield return sampler != null
                ? Entry("aggregatedBy", sampler.name)
                : Entry("simulationDevice", "true");
        }

        /// <summary>
        /// The PanelSampler that lists this device among its components, searched up the transform
        /// ancestors — where authored panels put their members. A sampler that reaches sideways
        /// across the scene is missed, and its members then read as simulation-only.
        /// </summary>
        private static PanelSampler FindAggregatingSampler(Transform t)
        {
            for (var ancestor = t.parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (!ancestor.TryGetComponent<PanelSampler>(out var sampler)) continue;
                if (sampler.Components == null) continue;
                if (sampler.Components.Any(c => c != null && c.transform == t)) return sampler;
            }

            return null;
        }

        private static ContextEntry Entry(string key, string value)
        {
            return new ContextEntry { key = key, value = value };
        }
    }
}
