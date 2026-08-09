using NUnit.Framework;
using OC.Communication;
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

        [Test]
        public void ResolveRole_IsEmpty_WithoutHierarchy()
        {
            Assert.AreEqual(string.Empty, _provider.ResolveRole(_root.transform));
        }

        [Test]
        public void ResolveRole_IsGroup_ForAPlainHierarchy()
        {
            MakeHierarchy(_root, false);

            Assert.AreEqual("group", _provider.ResolveRole(_root.transform));
        }

        [Test]
        public void ResolveRole_IsSampler_WhenIsNameSamplerIsSet()
        {
            MakeHierarchy(_root, true);

            Assert.AreEqual("sampler", _provider.ResolveRole(_root.transform));
        }

        [Test]
        public void ResolveLinkState_IsNull_ForANonDevice()
        {
            // Null means "not a device at all", which downstream codegen treats differently from a
            // device whose link is disabled.
            Assert.IsNull(_provider.ResolveLinkState(_root.transform));
        }

        [Test]
        public void IsDevice_IsFalse_ForAPlainTransform()
        {
            Assert.IsFalse(_provider.IsDevice(_root.transform));
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
        public void ResolvePath_FallsBackToTransformNames_WithoutHierarchy()
        {
            var child = Child(_root, "P_Reader");

            // Hierarchy.Name falls back to the transform name, so a plain chain still resolves.
            Assert.AreEqual("P_Reader", _provider.ResolvePath(child.transform));
        }

        [Test]
        public void ResolvePath_JoinsGroupsWithDots()
        {
            MakeHierarchy(_root, false);
            var group = Child(_root, "FG_01");
            MakeHierarchy(group, false);
            var device = Child(group, "P_Reader");

            Assert.AreEqual("Root.FG_01.P_Reader", _provider.ResolvePath(device.transform));
        }

        [Test]
        public void ResolvePath_JoinsSamplersWithUnderscore()
        {
            var sampler = Child(_root, "FG");
            MakeHierarchy(sampler, true);
            var device = Child(sampler, "Transport");

            // A sampler opens no level: it prefixes its children instead, so the path stays flat.
            Assert.AreEqual("FG_Transport", _provider.ResolvePath(device.transform));
        }

        [Test]
        public void NullTransforms_AreTolerated()
        {
            // Providers receive arbitrary Transforms from anywhere in the scene.
            Assert.AreEqual(string.Empty, _provider.ResolvePath(null));
            Assert.AreEqual(string.Empty, _provider.ResolveRole(null));
            Assert.IsNull(_provider.ResolveLinkState(null));
            Assert.IsFalse(_provider.IsDevice(null));
            Assert.IsFalse(_provider.IsRelevant(null));
        }
    }
}
