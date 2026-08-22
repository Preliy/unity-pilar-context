using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OC.Communication;
using OC.Interactions;
using UnityEditor;
using UnityEngine;

namespace PILAR.Context.OpenCommissioning.Tests
{
    /// <summary>
    /// Exercises the Open Commissioning integration against real OC components. This whole assembly
    /// is excluded when OC is not installed, which is what makes the no-oc CI leg possible.
    /// </summary>
    public class OpenCommissioningMetadataProviderTests
    {
        private GameObject _root;
        private OpenCommissioningMetadataProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Root");
            _provider = new OpenCommissioningMetadataProvider();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            return go;
        }

        /// <summary>
        /// IsNameSampler is exposed read-only, so tests drive the backing field through
        /// SerializedObject. That keeps the dependency visible to a rename refactor, unlike raw
        /// reflection on a string field name.
        /// </summary>
        private static Hierarchy MakeHierarchy(GameObject go, bool isNameSampler)
        {
            var hierarchy = go.AddComponent<Hierarchy>();
            if (!isNameSampler) return hierarchy;

            var so = new SerializedObject(hierarchy);
            so.FindProperty("_isNameSampler").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hierarchy;
        }

        /// <summary>A concrete SampleDevice, so it can also go into a PanelSampler's component list.</summary>
        private static Lamp MakeDevice(GameObject parent, string name, bool linked)
        {
            var device = Child(parent, name).AddComponent<Lamp>();
            device.Link.Enable = linked;
            return device;
        }

        private Dictionary<string, string> Metadata(Transform t)
        {
            return _provider.ResolveMetadata(t).ToDictionary(e => e.key, e => e.value);
        }

        [Test]
        public void Namespace_IsOc()
        {
            // Fixed forever: it is how a sync recognises its own entries in a node, so changing it
            // would strand the OC data already written into every scene of every project.
            Assert.AreEqual("oc", _provider.Namespace);
        }

        // ------------------------------------------------------------------ plcPath

        [Test]
        public void Metadata_PlcPathFallsBackToTransformNames_WithoutHierarchy()
        {
            var child = Child(_root, "P_Reader");

            // Hierarchy.Name falls back to the transform name, so a plain chain still resolves.
            Assert.AreEqual("P_Reader", Metadata(child.transform)["plcPath"]);
        }

        [Test]
        public void Metadata_PlcPathJoinsGroupsWithDots()
        {
            MakeHierarchy(_root, false);
            var group = Child(_root, "FG_01");
            MakeHierarchy(group, false);
            var device = Child(group, "P_Reader");

            Assert.AreEqual("Root.FG_01.P_Reader", Metadata(device.transform)["plcPath"]);
        }

        [Test]
        public void Metadata_PlcPathJoinsSamplersWithUnderscore()
        {
            var sampler = Child(_root, "FG");
            MakeHierarchy(sampler, true);
            var device = Child(sampler, "Transport");

            // A sampler opens no level: it prefixes its children instead, so the path stays flat.
            Assert.AreEqual("FG_Transport", Metadata(device.transform)["plcPath"]);
        }

        // ------------------------------------------------------------- hierarchyRole

        [Test]
        public void Metadata_HasNoRole_WithoutHierarchy()
        {
            CollectionAssert.DoesNotContain(Metadata(_root.transform).Keys, "hierarchyRole");
        }

        [Test]
        public void Metadata_RoleIsGroup_ForAPlainHierarchy()
        {
            MakeHierarchy(_root, false);

            Assert.AreEqual("group", Metadata(_root.transform)["hierarchyRole"]);
        }

        [Test]
        public void Metadata_RoleIsSampler_WhenIsNameSamplerIsSet()
        {
            MakeHierarchy(_root, true);

            Assert.AreEqual("sampler", Metadata(_root.transform)["hierarchyRole"]);
        }

        // ---------------------------------------------------------- simulation split

        [Test]
        public void Metadata_SaysNothingAboutSimulation_ForANonDevice()
        {
            var keys = Metadata(_root.transform).Keys;

            CollectionAssert.DoesNotContain(keys, "simulationDevice");
            CollectionAssert.DoesNotContain(keys, "aggregatedBy");
        }

        [Test]
        public void Metadata_NamesTheDeviceType_OnDevicesOnly()
        {
            var device = MakeDevice(_root, "H_Lamp", linked: true);
            var structure = Child(_root, "Station");

            // Every Transform resolves a plcPath, so deviceType is what tells a reader that this node
            // is a device at all — the export has no tier field to say it.
            Assert.AreEqual("Lamp", Metadata(device.transform)["deviceType"]);
            CollectionAssert.DoesNotContain(Metadata(structure.transform).Keys, "deviceType");
        }

        [Test]
        public void Metadata_SaysNothingAboutSimulation_ForALinkedDevice()
        {
            var device = MakeDevice(_root, "P_Reader", linked: true);

            // Absence is the positive case: a linked device is the ordinary one, and the tier already
            // states that it is a device at all.
            CollectionAssert.DoesNotContain(Metadata(device.transform).Keys, "simulationDevice");
        }

        [Test]
        public void Metadata_MarksAnUnlinkedDeviceAsSimulation()
        {
            var device = MakeDevice(_root, "M_Fake", linked: false);

            // OC has no simulation flag, so this is inferred from a disabled link that no sampler
            // accounts for.
            Assert.AreEqual("true", Metadata(device.transform)["simulationDevice"]);
        }

        [Test]
        public void Metadata_AttributesAnUnlinkedDeviceToItsAggregatingSampler()
        {
            var panel = Child(_root, "Panel_Main");
            var sampler = panel.AddComponent<PanelSampler>();
            var device = MakeDevice(panel, "H_Lamp", linked: false);
            sampler.Components.Add(device);

            var metadata = Metadata(device.transform);

            // The link is disabled because the panel folded this device into its own symbol, not
            // because the device is simulated — conflating the two would mislabel every panel member.
            Assert.AreEqual("Panel_Main", metadata["aggregatedBy"]);
            CollectionAssert.DoesNotContain(metadata.Keys, "simulationDevice");
        }

        [Test]
        public void Metadata_UnlistedDeviceUnderASamplerIsStillSimulation()
        {
            var panel = Child(_root, "Panel_Main");
            panel.AddComponent<PanelSampler>();
            var device = MakeDevice(panel, "H_Lamp", linked: false);

            // Sitting under a panel is not membership: only the components list decides.
            Assert.AreEqual("true", Metadata(device.transform)["simulationDevice"]);
        }

        // ----------------------------------------------------------------- structure

        [Test]
        public void IsDevice_IsFalse_ForAPlainTransform()
        {
            Assert.IsFalse(_provider.IsDevice(_root.transform));
        }

        [Test]
        public void IsDevice_IsTrue_ForAnIDevice()
        {
            var device = MakeDevice(_root, "P_Reader", linked: true);

            Assert.IsTrue(_provider.IsDevice(device.transform));
        }

        [Test]
        public void IsRelevant_IsFalse_ForBareTransforms()
        {
            Child(_root, "CadMesh");

            Assert.IsFalse(_provider.IsRelevant(_root.transform));
        }

        [Test]
        public void IsRelevant_IsTrue_WhenAHierarchyExistsAnywhereBelow()
        {
            var wrapper = Child(_root, "Wrapper");
            MakeHierarchy(Child(wrapper, "Station"), false);

            Assert.IsTrue(_provider.IsRelevant(_root.transform));
        }

        [Test]
        public void NullTransforms_AreTolerated()
        {
            // Providers receive arbitrary Transforms from anywhere in the scene.
            CollectionAssert.IsEmpty(_provider.ResolveMetadata(null).ToArray());
            Assert.IsFalse(_provider.IsDevice(null));
            Assert.IsFalse(_provider.IsRelevant(null));
        }
    }
}
