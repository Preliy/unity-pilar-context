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
    /// every accessor returns its neutral value and the package degrades to plain key/value context
    /// on a plain transform hierarchy.
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

        /// <summary>Framework path for this Transform, or empty when no provider supplies one.</summary>
        public static string Path(Transform t)
        {
            foreach (var provider in Providers)
            {
                var path = provider.ResolvePath(t);
                if (!string.IsNullOrEmpty(path)) return path;
            }

            return string.Empty;
        }

        /// <summary>Controller link state, or null when this Transform is not a device.</summary>
        public static bool? LinkState(Transform t)
        {
            foreach (var provider in Providers)
            {
                var state = provider.ResolveLinkState(t);
                if (state.HasValue) return state;
            }

            return null;
        }

        /// <summary>Structural role in the framework tree, or empty when it opens no level.</summary>
        public static string Role(Transform t)
        {
            foreach (var provider in Providers)
            {
                var role = provider.ResolveRole(t);
                if (!string.IsNullOrEmpty(role)) return role;
            }

            return string.Empty;
        }

        /// <summary>Derived-information lines for the inspector, across all providers.</summary>
        public static IEnumerable<string> Notes(Transform t)
        {
            return Providers.SelectMany(p => p.InspectorNotes(t) ?? Enumerable.Empty<string>())
                .Where(note => !string.IsNullOrEmpty(note));
        }
    }
}
