using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PILAR.Context.Editor.Tests
{
    public class ContextTopologyPathTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Project");
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

        private static ContextNode NodeChild(GameObject parent, string name)
        {
            return Child(parent, name).AddComponent<ContextNode>();
        }

        [Test]
        public void Resolve_IsEmpty_ForATransformWithNoNode()
        {
            // The topology tree is exactly the ContextNode tree: an un-annotated object is not in it.
            Assert.AreEqual(string.Empty, ContextTopologyPath.Resolve(_root.transform));
        }

        [Test]
        public void Resolve_IsEmpty_ForNull()
        {
            Assert.AreEqual(string.Empty, ContextTopologyPath.Resolve(null));
        }

        [Test]
        public void Resolve_IsJustTheName_ForATopLevelNode()
        {
            var node = NodeChild(_root, "FG_01");

            Assert.AreEqual("FG_01", ContextTopologyPath.Resolve(node.transform));
        }

        [Test]
        public void Resolve_SkipsUnannotatedLevels()
        {
            var group = NodeChild(_root, "FG_01");
            var geometry = Child(group.gameObject, "Geometry");
            var inner = Child(geometry, "Mounts");
            var device = NodeChild(inner, "P_Reader");

            // Geometry and Mounts exist for the scene, not for the machine's structure.
            Assert.AreEqual("FG_01/P_Reader", ContextTopologyPath.Resolve(device.transform));
        }

        [Test]
        public void Resolve_UsesTheSegmentOverrideInsteadOfTheName()
        {
            var group = NodeChild(_root, "FG_01");
            group.TopologySegment = "Transport";
            var device = NodeChild(group.gameObject, "P_Reader");

            Assert.AreEqual("Transport/P_Reader", ContextTopologyPath.Resolve(device.transform));
        }

        [Test]
        public void Resolve_TrimsTheSegmentOverrideAndIgnoresABlankOne()
        {
            var group = NodeChild(_root, "FG_01");
            group.TopologySegment = "   ";
            var device = NodeChild(group.gameObject, "P_Reader");
            device.TopologySegment = "  Reader  ";

            Assert.AreEqual("FG_01/Reader", ContextTopologyPath.Resolve(device.transform));
        }

        [Test]
        public void Resolve_FollowsTheParentOverrideAwayFromTheTransformTree()
        {
            var transport = NodeChild(_root, "FG_Transport");
            var press = NodeChild(_root, "FG_Press");
            var device = NodeChild(press.gameObject, "M_Belt");
            device.TopologyParent = transport;

            // The belt sits under the press in the scene because that is where the CAD put it; it
            // belongs to transport in the machine's own structure.
            Assert.AreEqual("FG_Transport/M_Belt", ContextTopologyPath.Resolve(device.transform));
        }

        [Test]
        public void Resolve_FallsBackToTheAncestorWalk_WhenTheParentOverrideWasDestroyed()
        {
            var group = NodeChild(_root, "FG_01");
            var elsewhere = NodeChild(_root, "FG_02");
            var device = NodeChild(group.gameObject, "P_Reader");
            device.TopologyParent = elsewhere;

            UnityEngine.Object.DestroyImmediate(elsewhere.gameObject);

            // A dangling reference must not silently truncate the path to a bare name.
            Assert.AreEqual("FG_01/P_Reader", ContextTopologyPath.Resolve(device.transform));
        }

        [Test]
        public void Resolve_TerminatesAndWarns_OnAParentCycle()
        {
            var a = NodeChild(_root, "A");
            var b = NodeChild(_root, "B");
            a.TopologyParent = b;
            b.TopologyParent = a;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("topology parent cycle"));

            // Truncated rather than hung: the walk stops the moment it revisits a node.
            Assert.AreEqual("B/A", ContextTopologyPath.Resolve(a.transform));
        }

        [Test]
        public void Resolve_TerminatesAndWarns_OnASelfParent()
        {
            var node = NodeChild(_root, "A");
            node.TopologyParent = node;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("topology parent cycle"));

            Assert.AreEqual("A", ContextTopologyPath.Resolve(node.transform));
        }

        [Test]
        public void ParentOf_PrefersTheOverrideOverTheNearestAncestor()
        {
            var group = NodeChild(_root, "FG_01");
            var other = NodeChild(_root, "FG_02");
            var device = NodeChild(group.gameObject, "P_Reader");

            Assert.AreSame(group, ContextTopologyPath.ParentOf(device));

            device.TopologyParent = other;
            Assert.AreSame(other, ContextTopologyPath.ParentOf(device));
        }

        [Test]
        public void ParentOf_IsNullAtTheTopOfTheTopology()
        {
            var group = NodeChild(_root, "FG_01");

            Assert.IsNull(ContextTopologyPath.ParentOf(group));
            Assert.IsNull(ContextTopologyPath.ParentOf(null));
        }
    }
}
