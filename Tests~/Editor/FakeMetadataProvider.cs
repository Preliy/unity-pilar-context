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

        /// <summary>Transforms this provider claims as relevant, matched by name.</summary>
        public HashSet<string> RelevantNames { get; } = new();

        /// <summary>Transforms this provider reports as devices, matched by name.</summary>
        public HashSet<string> DeviceNames { get; } = new();

        public Func<Transform, string> PathFunc { get; set; } = _ => string.Empty;
        public Func<Transform, bool?> LinkStateFunc { get; set; } = _ => null;
        public Func<Transform, string> RoleFunc { get; set; } = _ => string.Empty;
        public Func<Transform, IEnumerable<string>> NotesFunc { get; set; } = _ => Enumerable.Empty<string>();

        public bool IsRelevant(Transform subtreeRoot)
        {
            return subtreeRoot != null &&
                   subtreeRoot.GetComponentsInChildren<Transform>(true).Any(t => RelevantNames.Contains(t.name));
        }

        public bool IsDevice(Transform t) => t != null && DeviceNames.Contains(t.name);

        public string ResolvePath(Transform t) => PathFunc(t);

        public bool? ResolveLinkState(Transform t) => LinkStateFunc(t);

        public string ResolveRole(Transform t) => RoleFunc(t);

        public IEnumerable<string> InspectorNotes(Transform t) => NotesFunc(t);
    }
}
