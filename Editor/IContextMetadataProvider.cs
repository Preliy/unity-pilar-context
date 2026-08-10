using System.Collections.Generic;
using UnityEngine;

namespace PILAR.Context.Editor
{
    /// <summary>
    /// Optional integration point between this package and a digital twin framework.
    ///
    /// The package itself knows nothing about any specific twin framework: it stores, edits and
    /// exports key/value context, and derives the topology from the <see cref="ContextNode"/>s the
    /// author placed. A provider contributes what is left — which subtrees are semantically
    /// relevant, which objects are devices, and whatever facts the framework knows about an object
    /// that the package cannot compute.
    ///
    /// Those facts travel as free-form key/value metadata under keys the framework itself chooses,
    /// so a framework's vocabulary never has to be spelled into this package or into the export
    /// schema. Nothing outside the provider interprets them.
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
        /// Precedence when several providers are installed; lower runs first, and when two providers
        /// both answer for the same key, the first one wins. Use 0 unless you deliberately need to
        /// override another provider.
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
        /// Everything the framework knows about this Transform, as key/value pairs of its own
        /// choosing — a framework-side path, a structural role, whether the object is simulated.
        /// Return an empty sequence when the Transform means nothing to this provider; omit a key
        /// rather than returning an empty value for it, since an absent fact and a blank one are
        /// not the same statement.
        /// </summary>
        IEnumerable<ContextEntry> ResolveMetadata(Transform t);
    }
}
