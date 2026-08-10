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
    /// The cache is a plain static: statics reset on domain reload, which is exactly when the set of
    /// compiled providers can change, so no explicit invalidation is needed.
    /// </summary>
    public static class ContextMetadataRegistry
    {
        private static IContextMetadataProvider[] _providers;

        public static IReadOnlyList<IContextMetadataProvider> Providers => _providers ??= Discover();

        /// <summary>Drops the cache so the next access rediscovers.</summary>
        public static void Invalidate() => _providers = null;

        /// <summary>
        /// Replaces the discovered set with an explicit one, so a test can exercise both the
        /// no-provider and the many-provider paths regardless of which packages happen to be
        /// installed. Pass null to return to automatic discovery.
        /// </summary>
        internal static void OverrideProviders(IContextMetadataProvider[] providers)
        {
            _providers = providers;
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

            return found.OrderBy(p => p.Order).ToArray();
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
        /// Every provider's metadata for this Transform, merged in <c>Order</c>. Each provider's own
        /// entry order is kept, and the first provider to answer for a key owns it — a later one
        /// cannot overwrite the answer, only add keys nobody claimed.
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
                    if (!claimed.Add(entry.key)) continue;

                    merged.Add(new ContextEntry { key = entry.key, value = entry.value });
                }
            }

            return merged;
        }
    }
}
