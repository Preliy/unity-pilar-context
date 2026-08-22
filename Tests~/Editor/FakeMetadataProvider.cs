using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PILAR.Context.Editor.Tests
{
    /// <summary>
    /// Stand-in for a twin-framework integration, so the core can be tested without installing one.
    ///
    /// The constructor is deliberately internal: <see cref="ContextMetadataRegistry"/> only
    /// instantiates types with a public parameterless constructor, so this fake is invisible to
    /// automatic discovery and cannot leak into a real Editor session. Tests install it explicitly
    /// through <c>ContextMetadataRegistry.OverrideProviders</c>.
    /// </summary>
    internal class FakeMetadataProvider : IContextMetadataProvider
    {
        internal FakeMetadataProvider()
        {
        }

        public int Order { get; set; }

        /// <summary>Settable so a test can exercise the registry's namespace admission rules.</summary>
        public string Namespace { get; set; } = "fake";

        /// <summary>Transforms this provider claims as relevant, matched by name.</summary>
        public HashSet<string> RelevantNames { get; } = new();

        /// <summary>Transforms this provider reports as devices, matched by name.</summary>
        public HashSet<string> DeviceNames { get; } = new();

        public Func<Transform, IEnumerable<ContextEntry>> MetadataFunc { get; set; } =
            _ => Enumerable.Empty<ContextEntry>();

        public bool IsRelevant(Transform subtreeRoot)
        {
            return subtreeRoot != null &&
                   subtreeRoot.GetComponentsInChildren<Transform>(true).Any(t => RelevantNames.Contains(t.name));
        }

        public bool IsDevice(Transform t) => t != null && DeviceNames.Contains(t.name);

        public IEnumerable<ContextEntry> ResolveMetadata(Transform t) => MetadataFunc(t);

        /// <summary>Shorthand for the common case: a fixed set of bare-key pairs for every Transform.</summary>
        internal static FakeMetadataProvider Emitting(params (string key, string value)[] entries)
        {
            return new FakeMetadataProvider
            {
                MetadataFunc = _ => entries.Select(e => new ContextEntry { key = e.key, value = e.value })
            };
        }

        /// <summary>Same, under a namespace of its own, for the multi-provider cases.</summary>
        internal static FakeMetadataProvider Emitting(
            string ns, params (string key, string value)[] entries)
        {
            var provider = Emitting(entries);
            provider.Namespace = ns;
            return provider;
        }
    }
}
