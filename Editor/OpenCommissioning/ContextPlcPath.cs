using OC.Communication;
using UnityEngine;

namespace PILAR.Context.OpenCommissioning
{
    /// <summary>
    /// Resolves the same logical PLC/OC.Communication.Hierarchy dot-path that OC's own
    /// Link.GetHierarchyPath() computes for IDevice components, but for an arbitrary Transform
    /// that may not carry an IDevice/Link at all (e.g. a plain assembly wrapper).
    ///
    /// Internal: this is an Open Commissioning implementation detail. Callers outside this assembly
    /// go through <see cref="PILAR.Context.Editor.ContextMetadataRegistry.Path"/>, which works
    /// whether or not OC is installed.
    /// </summary>
    internal static class ContextPlcPath
    {
        public static string Resolve(Transform origin)
        {
            string path;
            Transform parent;

            if (origin.TryGetComponent<Hierarchy>(out var selfHierarchy))
            {
                path = selfHierarchy.Name;
                parent = selfHierarchy.GetParent();
            }
            else
            {
                path = origin.name;
                parent = origin.parent;
            }

            while (parent != null)
            {
                if (parent.TryGetComponent<Client>(out var client))
                {
                    return client.RootName + "." + path;
                }

                if (parent.TryGetComponent<Hierarchy>(out var hierarchy))
                {
                    path = hierarchy.IsNameSampler ? hierarchy.Name + "_" + path : hierarchy.Name + "." + path;
                    parent = hierarchy.GetParent();
                    continue;
                }

                parent = parent.parent;
            }

            return path;
        }
    }
}
