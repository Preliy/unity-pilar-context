using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PILAR.Context.Editor
{
    /// <summary>
    /// Discovers the <see cref="IContextMetadataProvider"/> implementations present in the project
    /// and merges their answers, so the rest of the package never references a twin framework
    /// directly.
    ///
    /// Discovery is by <c>TypeCache</c>, which the Editor maintains as part of the compilation
    /// pipeline — there is no assembly scan and no registration call. When no provider is installed
    /// every accessor returns its neutral value and the package degrades to authored key/value
    /// context on the topology the author built, with no metadata attached.
    ///
    /// Every provider claims a key namespace, and this class is where that namespace is applied
    /// (<see cref="Qualify"/>) and recognised again (<see cref="IsDerivedKey"/>). A provider that
    /// cannot be given an unambiguous namespace is refused at discovery rather than half-supported,
    /// because <see cref="ContextMetadataSync"/> identifies the entries it owns by that prefix alone.
    ///
    /// The cache is a plain static: statics reset on domain reload, which is exactly when the set of
    /// compiled providers can change, so no explicit invalidation is needed.
    /// </summary>
    public static class ContextMetadataRegistry
    {
        /// <summary>Separates a provider's namespace from its own key: "oc" + '.' + "plcPath".</summary>
        public const char NamespaceSeparator = '.';

        private static IContextMetadataProvider[] _providers;

        public static IReadOnlyList<IContextMetadataProvider> Providers => _providers ??= Discover();

        /// <summary>Namespaces claimed by the installed providers, in provider order.</summary>
        public static IReadOnlyList<string> Namespaces =>
            Providers.Select(p => p.Namespace).ToArray();

        /// <summary>Drops the cache so the next access rediscovers.</summary>
        public static void Invalidate() => _providers = null;

        /// <summary>
        /// Replaces the discovered set with an explicit one, so a test can exercise both the
        /// no-provider and the many-provider paths regardless of which packages happen to be
        /// installed. Pass null to return to automatic discovery.
        ///
        /// Goes through the same namespace admission as discovery, so a test cannot install a
        /// provider the Editor itself would have refused.
        /// </summary>
        internal static void OverrideProviders(IContextMetadataProvider[] providers)
        {
            _providers = providers == null ? null : Admit(providers);
        }

        /// <summary>
        /// Whether this key belongs to an installed provider rather than to a human — the question
        /// that separates the derived half of a node's entry list from the authored half.
        ///
        /// False for a key namespaced to a provider that is *not* installed. Such an entry is an
        /// orphan: it stays in the scene untouched, because opening the project without a framework
        /// installed must not strip that framework's data out of every node. The cost is that nothing
        /// can clean it up either.
        /// </summary>
        public static bool IsDerivedKey(string key)
        {
            if (!IsNamespacedKey(key)) return false;

            var ns = key.Substring(0, key.IndexOf(NamespaceSeparator));
            return Providers.Any(p => string.Equals(p.Namespace, ns, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Whether this key merely *looks* namespaced — some prefix, then a separator. Unlike
        /// <see cref="IsDerivedKey"/> this asks nothing about which providers are installed, so it
        /// still recognises an orphan. Use it where the question is "did a framework write this",
        /// never where the question is "is this documentation": an author is free to write
        /// "Motor.Speed", and that is coverage.
        /// </summary>
        public static bool IsNamespacedKey(string key)
        {
            return !string.IsNullOrEmpty(key) && key.IndexOf(NamespaceSeparator) > 0;
        }

        private static IContextMetadataProvider[] Discover()
        {
            var found = new List<IContextMetadataProvider>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<IContextMetadataProvider>())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                try
                {
                    found.Add((IContextMetadataProvider)Activator.CreateInstance(type));
                }
                catch (Exception e)
                {
                    Debug.LogError($"PILAR Context: could not instantiate metadata provider '{type.FullName}': {e.Message}");
                }
            }

            return Admit(found.OrderBy(p => p.Order));
        }

        /// <summary>
        /// Keeps only providers with a usable, unclaimed namespace. A provider that gets past this is
        /// safe to sync with; one that does not would write entries nothing could ever identify as
        /// its own, so it is refused loudly rather than half-supported.
        /// </summary>
        private static IContextMetadataProvider[] Admit(IEnumerable<IContextMetadataProvider> candidates)
        {
            var admitted = new List<IContextMetadataProvider>();
            var claimed = new Dictionary<string, IContextMetadataProvider>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in candidates)
            {
                if (provider == null) continue;

                var ns = provider.Namespace;
                var typeName = provider.GetType().FullName;

                if (string.IsNullOrWhiteSpace(ns))
                {
                    Debug.LogError(
                        $"PILAR Context: metadata provider '{typeName}' declares no Namespace and is " +
                        "ignored. Its entries could not be told apart from authored ones.");
                    continue;
                }

                if (ns.Contains(NamespaceSeparator))
                {
                    Debug.LogError(
                        $"PILAR Context: metadata provider '{typeName}' declares the namespace '{ns}', " +
                        $"which contains '{NamespaceSeparator}' — the separator itself. It is ignored.");
                    continue;
                }

                if (claimed.TryGetValue(ns, out var owner))
                {
                    Debug.LogError(
                        $"PILAR Context: metadata providers '{owner.GetType().FullName}' and '{typeName}' " +
                        $"both claim the namespace '{ns}'. The second is ignored — a shared prefix would " +
                        "leave a sync unable to tell their entries apart.");
                    continue;
                }

                claimed.Add(ns, provider);
                admitted.Add(provider);
            }

            return admitted.ToArray();
        }

        /// <summary>True when any provider claims this subtree carries framework meaning.</summary>
        public static bool AnyRelevant(Transform subtreeRoot)
        {
            return Providers.Any(p => p.IsRelevant(subtreeRoot));
        }

        /// <summary>True when any provider recognises this Transform as a device.</summary>
        public static bool AnyDevice(Transform t)
        {
            return Providers.Any(p => p.IsDevice(t));
        }

        /// <summary>
        /// Every provider's metadata for this Transform, in <c>Order</c>, with each provider's
        /// namespace already applied to its keys. This is what a sync writes into a node.
        ///
        /// Each provider's own entry order is kept. Providers cannot collide with each other — their
        /// namespaces differ by construction — so first-answer-wins only settles a provider that
        /// yields the same key twice.
        ///
        /// Entries with a blank key or a blank value are dropped: a fact the framework does not know
        /// is an absent entry, never an empty one.
        /// </summary>
        public static IReadOnlyList<ContextEntry> Metadata(Transform t)
        {
            var merged = new List<ContextEntry>();
            var claimed = new HashSet<string>();

            foreach (var provider in Providers)
            {
                var entries = provider.ResolveMetadata(t);
                if (entries == null) continue;

                foreach (var entry in entries)
                {
                    if (entry == null) continue;
                    if (string.IsNullOrWhiteSpace(entry.key)) continue;
                    if (string.IsNullOrWhiteSpace(entry.value)) continue;

                    var key = Qualify(provider.Namespace, entry.key);
                    if (!claimed.Add(key)) continue;

                    merged.Add(new ContextEntry { key = key, value = entry.value });
                }
            }

            return merged;
        }

        /// <summary>Applies a namespace to a bare provider key: ("oc", "plcPath") -> "oc.plcPath".</summary>
        public static string Qualify(string ns, string key)
        {
            return ns + NamespaceSeparator + key.Trim();
        }
    }
}
