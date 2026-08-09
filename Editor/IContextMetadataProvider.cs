using System.Collections.Generic;
using UnityEngine;

namespace PILAR.Context.Editor
{
    /// <summary>
    /// Optional integration point between this package and a digital twin framework.
    ///
    /// The package itself knows nothing about any specific twin framework: it stores, edits and
    /// exports key/value context. A provider contributes the derived facts the package cannot
    /// compute on its own — which subtrees are semantically relevant, which objects are devices,
    /// and what their framework-side path and link state are.
    ///
    /// Implementations are discovered automatically by <see cref="ContextMetadataRegistry"/> via
    /// <c>TypeCache</c>, so they need no registration call. They must be concrete, non-generic and
    /// expose a public parameterless constructor.
    ///
    /// Every method receives arbitrary Transforms from anywhere in the scene and must tolerate all
    /// of them — return the neutral value rather than throwing when a Transform means nothing to
    /// this provider.
    /// </summary>
    public interface IContextMetadataProvider
    {
        /// <summary>
        /// Precedence when several providers are installed; lower runs first, and the first
        /// non-empty answer wins. Use 0 unless you deliberately need to override another provider.
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Whether this subtree carries framework meaning anywhere within it and should therefore
        /// survive export pruning. Checked against the subtree, not just the Transform itself.
        /// </summary>
        bool IsRelevant(Transform subtreeRoot);

        /// <summary>Whether this Transform is a device — the leaf tier of the twin.</summary>
        bool IsDevice(Transform t);

        /// <summary>
        /// The framework-side logical path of this Transform (for Open Commissioning, the PLC
        /// symbol path). Empty when the framework has no path for it.
        /// </summary>
        string ResolvePath(Transform t);

        /// <summary>
        /// Whether this device actually exchanges data with the controller. Null when the Transform
        /// is not a device at all, which is a different statement from "a device that is disabled".
        /// </summary>
        bool? ResolveLinkState(Transform t);

        /// <summary>
        /// This Transform's structural role in the framework's own tree, which need not follow Unity
        /// transform parenting. Open Commissioning uses "group" and "sampler". Empty when the
        /// Transform opens no level in that tree.
        /// </summary>
        string ResolveRole(Transform t);

        /// <summary>
        /// Extra read-only lines for the ContextNode inspector's derived-information panel. Return
        /// an empty sequence when there is nothing useful to show.
        /// </summary>
        IEnumerable<string> InspectorNotes(Transform t);
    }
}
