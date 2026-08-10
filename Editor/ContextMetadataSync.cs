using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PILAR.Context.Editor
{
    /// <summary>
    /// What a sync would do to one node: the keys it would add, the ones whose value moved on, and
    /// the ones whose underlying fact is gone. Produced without writing anything, which is what makes
    /// it usable as a drift report.
    /// </summary>
    public sealed class ContextSyncPlan
    {
        public ContextNode Node { get; }
        public IReadOnlyList<string> Added { get; }
        public IReadOnlyList<string> Updated { get; }
        public IReadOnlyList<string> Removed { get; }

        public bool IsEmpty => Added.Count == 0 && Updated.Count == 0 && Removed.Count == 0;

        internal ContextSyncPlan(
            ContextNode node, List<string> added, List<string> updated, List<string> removed)
        {
            Node = node;
            Added = added;
            Updated = updated;
            Removed = removed;
        }
    }

    /// <summary>
    /// Writes what an <see cref="IContextMetadataProvider"/> knows into the node's own entry list, so
    /// a <see cref="ContextNode"/> holds its whole record rather than half of it. The export then
    /// dumps that list instead of querying providers, and the data survives on a machine where the
    /// twin framework is not installed at all.
    ///
    /// The price of persisting a derived fact is that it can go stale, and everything here is about
    /// containing that: sync runs only when someone asks, <see cref="Plan"/> reports drift without
    /// writing, and each entry is reconciled rather than rewritten.
    ///
    /// Ownership is by key namespace. An entry whose key <see cref="ContextMetadataRegistry.IsDerivedKey"/>
    /// recognises belongs to an installed provider and may be overwritten or deleted; everything else
    /// is the author's and is never read, moved or touched.
    /// </summary>
    public static class ContextMetadataSync
    {
        /// <summary>What <see cref="Apply"/> would change on this node, without changing it.</summary>
        public static ContextSyncPlan Plan(ContextNode node)
        {
            var added = new List<string>();
            var updated = new List<string>();
            var removed = new List<string>();

            if (node == null) return new ContextSyncPlan(null, added, updated, removed);

            var desired = ContextMetadataRegistry.Metadata(node.transform);
            var desiredKeys = new HashSet<string>(desired.Select(e => e.key));

            foreach (var entry in desired)
            {
                if (!node.TryGetValue(entry.key, out var current)) added.Add(entry.key);
                else if (current != entry.value) updated.Add(entry.key);
            }

            // Owned keys the providers no longer answer for: the Hierarchy component was deleted, the
            // device stopped being a device. Their entries have to go, or the node keeps asserting a
            // fact the scene has stopped making.
            removed.AddRange(node.Entries
                .Select(e => e.key)
                .Where(key => ContextMetadataRegistry.IsDerivedKey(key) && !desiredKeys.Contains(key)));

            return new ContextSyncPlan(node, added, updated, removed);
        }

        /// <summary>
        /// Reconciles the node's derived entries with what the providers currently say. Returns true
        /// when anything actually changed, so a caller can skip dirtying a scene that is already in
        /// step.
        ///
        /// Updates land in place rather than as a remove-then-append, so a synced node keeps its entry
        /// order and a re-sync produces no scene diff at all.
        /// </summary>
        public static bool Apply(ContextNode node)
        {
            if (node == null) return false;

            var plan = Plan(node);
            if (plan.IsEmpty) return false;

            foreach (var key in plan.Removed) node.Remove(key);

            // Set is an upsert that keeps an existing entry's position, which is exactly the
            // in-place update this wants, and appends the genuinely new keys in provider order.
            foreach (var entry in ContextMetadataRegistry.Metadata(node.transform))
                node.Set(entry.key, entry.value);

            return true;
        }

        /// <summary>
        /// Every node in this subtree that a sync would change, in hierarchy order. The scan is
        /// read-only, so it is safe to run for a report.
        /// </summary>
        public static IEnumerable<ContextSyncPlan> PlanSubtree(Transform root)
        {
            if (root == null) yield break;

            foreach (var node in root.GetComponentsInChildren<ContextNode>(true))
            {
                var plan = Plan(node);
                if (!plan.IsEmpty) yield return plan;
            }
        }

        /// <summary>
        /// Whether this subtree holds any entry an installed provider owns — the question the export
        /// asks to work out whether a scene has ever been synced.
        /// </summary>
        public static bool HasDerivedEntries(Transform root)
        {
            if (root == null) return false;

            return root.GetComponentsInChildren<ContextNode>(true)
                .SelectMany(node => node.Entries)
                .Any(entry => ContextMetadataRegistry.IsDerivedKey(entry.key));
        }
    }
}
