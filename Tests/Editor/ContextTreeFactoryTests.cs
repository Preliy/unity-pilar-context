using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PILAR.Context.Editor.Tests
{
    public class ContextTreeFactoryTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            // Start from no providers so pruning is driven purely by authored ContextNodes; the
            // provider-driven cases install a fake explicitly.
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[0]);
            _root = new GameObject("Root");
        }

        [TearDown]
        public void TearDown()
        {
            ContextMetadataRegistry.OverrideProviders(null);
            UnityEngine.Object.DestroyImmediate(_root);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            return go;
        }

        [Test]
        public void Build_PrunesChildWithNoContextAndNoProviderInterest()
        {
            Child(_root, "CadMesh");

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(0, node.children.Count);
        }

        [Test]
        public void Build_KeepsChildThatCarriesAContextNode()
        {
            Child(_root, "Device").AddComponent<ContextNode>();

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(1, node.children.Count);
            Assert.AreEqual("Device", node.children[0].name);
        }

        [Test]
        public void Build_KeepsWrapperWhenContextNodeIsDeeperInSubtree()
        {
            var wrapper = Child(_root, "Wrapper");
            var inner = Child(wrapper, "Inner");
            Child(inner, "Leaf").AddComponent<ContextNode>();

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(1, node.children.Count, "an empty wrapper must survive if anything below it matters");
            Assert.AreEqual("Wrapper", node.children[0].name);
            Assert.AreEqual("Inner", node.children[0].children[0].name);
            Assert.AreEqual("Leaf", node.children[0].children[0].children[0].name);
        }

        [Test]
        public void Build_KeepsSubtreeAProviderVouchesFor()
        {
            Child(_root, "Machinery");
            var provider = new FakeMetadataProvider();
            provider.RelevantNames.Add("Machinery");
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[] { provider });

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(1, node.children.Count);
            Assert.AreEqual("Machinery", node.children[0].name);
        }

        [Test]
        public void Build_NeverPrunesTheRootItself()
        {
            // HasMeaningfulContent is applied to children only. A root with nothing in it still
            // produces a node, and callers rely on always getting one back.
            var node = ContextTreeFactory.Build(_root.transform);

            Assert.IsNotNull(node);
            Assert.AreEqual("Root", node.name);
            Assert.AreEqual(0, node.children.Count);
        }

        [Test]
        public void Build_ComposesScenePathFromTheRoot()
        {
            var group = Child(_root, "FG_01");
            var device = Child(group, "P_Reader");
            device.AddComponent<ContextNode>();

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual("Root", node.scenePath);
            Assert.AreEqual("Root/FG_01", node.children[0].scenePath);
            Assert.AreEqual("Root/FG_01/P_Reader", node.children[0].children[0].scenePath);
        }

        [Test]
        public void Build_ComposesTopologyPathFromTheContextNodesOnly()
        {
            var group = Child(_root, "FG_01");
            group.AddComponent<ContextNode>();
            var wrapper = Child(group, "Geometry");
            Child(wrapper, "P_Reader").AddComponent<ContextNode>();

            var node = ContextTreeFactory.Build(_root.transform);
            var groupNode = node.children[0];
            var wrapperNode = groupNode.children[0];
            var deviceNode = wrapperNode.children[0];

            // Root and Geometry carry no node, so they are in the scene path and not the topology.
            Assert.AreEqual(string.Empty, node.topologyPath);
            Assert.AreEqual("FG_01", groupNode.topologyPath);
            Assert.AreEqual(string.Empty, wrapperNode.topologyPath);
            Assert.AreEqual("FG_01/P_Reader", deviceNode.topologyPath);
        }

        [Test]
        public void Build_PreservesSiblingOrder()
        {
            foreach (var name in new[] { "c", "a", "b" })
                Child(_root, name).AddComponent<ContextNode>();

            var node = ContextTreeFactory.Build(_root.transform);

            CollectionAssert.AreEqual(new[] { "c", "a", "b" }, node.children.Select(c => c.name).ToArray());
        }

        [Test]
        public void Build_CopiesTheNodesEntries()
        {
            var device = Child(_root, "P_Reader");
            var contextNode = device.AddComponent<ContextNode>();
            contextNode.Add("Function", "Reads the RFID tag.");

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(1, node.children[0].entries.Count);
            Assert.AreEqual("Function", node.children[0].entries[0].key);
            Assert.AreEqual("Reads the RFID tag.", node.children[0].entries[0].value);
        }

        [Test]
        public void Build_DoesNotAskProvidersForContent()
        {
            Child(_root, "P_Reader").AddComponent<ContextNode>();
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[]
            {
                FakeMetadataProvider.Emitting("oc", ("plcPath", "MAIN.FG_01.P_Reader"))
            });

            var child = ContextTreeFactory.Build(_root.transform).children[0];

            // Metadata reaches a node through ContextMetadataSync, not through the export. An
            // unsynced scene exports nothing extra no matter what a provider would have said.
            CollectionAssert.IsEmpty(child.entries);
        }

        [Test]
        public void Build_DumpsSyncedAndAuthoredEntriesAlikeInNodeOrder()
        {
            var node = Child(_root, "P_Reader").AddComponent<ContextNode>();
            node.Set("Function", "Reads the RFID tag.");
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[]
            {
                FakeMetadataProvider.Emitting("oc", ("plcPath", "MAIN.FG_01.P_Reader"))
            });
            ContextMetadataSync.Apply(node);

            var child = ContextTreeFactory.Build(_root.transform).children[0];

            // One list, verbatim: the factory neither partitions nor reorders it, which is what keeps
            // the schema free of any framework's vocabulary.
            CollectionAssert.AreEqual(
                new[] { "Function=Reads the RFID tag.", "oc.plcPath=MAIN.FG_01.P_Reader" },
                child.entries.Select(e => $"{e.key}={e.value}").ToArray());
        }

        [Test]
        public void BuildJson_WrapsTheTreeWithSceneMetadata()
        {
            Child(_root, "P_Reader").AddComponent<ContextNode>();

            var json = ContextTreeFactory.BuildJson(_root.transform);

            Assert.IsTrue(json.Contains("\"sceneName\""));
            Assert.IsTrue(json.Contains("\"generatedAtUtc\""));
            Assert.IsTrue(json.Contains("\"root\""));
        }

        /// <summary>
        /// Mirrors the internal ContextExportRoot. JsonUtility binds by field name, so this reads the
        /// real payload without needing InternalsVisibleTo — and it fails loudly if the wrapper's
        /// shape ever changes, which is the contract downstream consumers actually depend on.
        /// </summary>
        [System.Serializable]
        private class ExportRootMirror
        {
            public string sceneName;
            public string generatedAtUtc;
            public ContextExportNode root;
        }

        [Test]
        public void BuildJson_ProducesADeserializableNodeTree()
        {
            var device = Child(_root, "P_Reader");
            var node = device.AddComponent<ContextNode>();
            node.Add("Function", "Reads the RFID tag.");
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[]
            {
                FakeMetadataProvider.Emitting("oc", ("plcPath", "MAIN.P_Reader"))
            });
            ContextMetadataSync.Apply(node);

            // Never assert the exact string: it embeds the active scene name and UtcNow.
            var parsed = JsonUtility.FromJson<ExportRootMirror>(ContextTreeFactory.BuildJson(_root.transform));

            Assert.IsFalse(string.IsNullOrEmpty(parsed.generatedAtUtc));
            Assert.AreEqual("Root", parsed.root.name);
            Assert.AreEqual(1, parsed.root.children.Count);

            var child = parsed.root.children[0];
            Assert.AreEqual("P_Reader", child.name);
            Assert.AreEqual("Root/P_Reader", child.scenePath);
            Assert.AreEqual("P_Reader", child.topologyPath);

            // One list of serializable entries, so the whole dictionary survives JsonUtility intact -
            // which is why nothing has to be flattened into a string to be exported.
            CollectionAssert.AreEqual(
                new[] { "Function=Reads the RFID tag.", "oc.plcPath=MAIN.P_Reader" },
                child.entries.Select(e => $"{e.key}={e.value}").ToArray());
        }
    }
}
