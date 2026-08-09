using NUnit.Framework;
using PILAR.Context.Editor;
using PILAR.Context.Editor.Tests;
using UnityEngine;

namespace PILAR.Context.Pipeline.Tests
{
    /// <summary>
    /// Shared scaffolding for the Pipeline suites.
    ///
    /// The commands reach global state in exactly one place — <c>ContextTargets.ResolveRoot</c> calls
    /// <c>GameObject.Find</c> — so the fixture has to be real, active GameObjects in the open scene
    /// rather than a detached hierarchy.
    ///
    /// Device-ness is forced through <see cref="FakeMetadataProvider"/>. Without that the same test
    /// would classify differently depending on whether a twin framework happens to be installed, so
    /// the CI legs would disagree with each other.
    /// </summary>
    public abstract class PipelineTestFixture
    {
        protected GameObject Root;

        // internal rather than protected: FakeMetadataProvider is itself internal, and a protected
        // member cannot be less accessible than its own type. Subclasses live in this assembly, so
        // internal reaches them just the same.
        internal FakeMetadataProvider Provider;

        [SetUp]
        public void BaseSetUp()
        {
            Provider = new FakeMetadataProvider();
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[] { Provider });
            Root = new GameObject("Project");
        }

        [TearDown]
        public void BaseTearDown()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            ContextMetadataRegistry.OverrideProviders(null);
        }

        /// <summary>Creates a child GameObject, parented without keeping world position.</summary>
        protected static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>Marks a transform as a device for the fake provider, by name.</summary>
        protected Transform Device(Transform parent, string name)
        {
            var t = Child(parent, name);
            Provider.DeviceNames.Add(name);
            return t;
        }

        protected static ContextNode NodeWith(Transform t, params (string key, string value)[] entries)
        {
            var node = t.gameObject.AddComponent<ContextNode>();
            foreach (var (key, value) in entries) node.Set(key, value);
            return node;
        }

        /// <summary>
        /// Project / FG_01 / { Station / Sensor(device), Loose } and Project / FG_02.
        /// Gives one of every tier: machine, group, assembly (Station, via the device below it),
        /// device (Sensor), and a target-less transform (Loose).
        /// </summary>
        protected void BuildStandardFixture()
        {
            var fg1 = Child(Root.transform, "FG_01");
            var station = Child(fg1, "Station");
            Device(station, "Sensor");
            Child(fg1, "Loose");
            Child(Root.transform, "FG_02");
        }
    }
}
