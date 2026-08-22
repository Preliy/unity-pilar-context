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

        /// <summary>A straight chain L1/L2/.../Ln under the root, every level carrying a node.</summary>
        private static GameObject Chain(GameObject parent, int levels)
        {
            var current = parent;
            for (var i = 1; i <= levels; i++)
            {
                current = Child(current, "L" + i);
                current.AddComponent<ContextNode>();
            }
            return current;
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
        public void Build_SkipsADisabledChild()
        {
            var device = Child(_root, "Device");
            device.AddComponent<ContextNode>();
            device.SetActive(false);

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(0, node.children.Count);
        }

        [Test]
        public void Build_SkipsEverythingUnderADisabledObject()
        {
            var wrapper = Child(_root, "Wrapper");
            Child(wrapper, "Device").AddComponent<ContextNode>();
            wrapper.SetActive(false);

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(0, node.children.Count, "an enabled node under a disabled parent is not in the machine");
        }

        [Test]
        public void Build_PrunesAWrapperWhoseOnlyNodeIsDisabled()
        {
            var wrapper = Child(_root, "Wrapper");
            Child(wrapper, "Device").AddComponent<ContextNode>().gameObject.SetActive(false);

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(0, node.children.Count, "keeping the wrapper would export a branch with nothing in it");
        }

        [Test]
        public void Build_EmitsOneNodeWhenADisabledTwinSharesTheScenePath()
        {
            // The case this rule exists for: a station kept in the scene in two variants, one of them
            // switched off. Both carry the same name in the same place, so exporting both produces two
            // nodes a consumer cannot tell apart by scenePath.
            var live = Child(_root, "FG_01");
            live.AddComponent<ContextNode>().Add("Function", "The one that is built.");
            var spare = Child(_root, "FG_01");
            spare.AddComponent<ContextNode>().Add("Function", "The variant kept for later.");
            spare.SetActive(false);

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(1, node.children.Count);
            Assert.AreEqual("The one that is built.", node.children[0].entries[0].value);
        }

        [Test]
        public void Build_ExportsTheChildrenOfADisabledRoot()
        {
            // Pruning applies to children. A root the caller selected deliberately is exported whatever
            // its own state, or exporting a switched-off station on purpose would return an empty tree.
            _root.SetActive(false);
            Child(_root, "Device").AddComponent<ContextNode>();

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(1, node.children.Count);
            Assert.AreEqual("Device", node.children[0].name);
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
        public void Build_SkipsADisabledSubtreeAProviderVouchesFor()
        {
            Child(_root, "Machinery").SetActive(false);
            var provider = new FakeMetadataProvider();
            provider.RelevantNames.Add("Machinery");
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[] { provider });

            var node = ContextTreeFactory.Build(_root.transform);

            Assert.AreEqual(0, node.children.Count, "disabled wins over a provider's interest");
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

            // One list of serializable entries, so the whole dictionary survives intact - which is why
            // nothing has to be flattened into a string to be exported.
            CollectionAssert.AreEqual(
                new[] { "Function=Reads the RFID tag.", "oc.plcPath=MAIN.P_Reader" },
                child.entries.Select(e => $"{e.key}={e.value}").ToArray());
        }

        [Test]
        public void BuildJson_WritesLevelsDeeperThanJsonUtilityCanSerialize()
        {
            // JsonUtility stops at ten levels of nesting and drops the rest with a console warning -
            // and each exported level costs two of them, so it truncates from roughly the fifth. The
            // export writes its own JSON for exactly this reason; a machine hierarchy is deeper than
            // that. Asserted on the raw string because JsonUtility.FromJson has the same limit and
            // would fail to read a correct document.
            var deepest = Chain(_root, 12);
            deepest.GetComponent<ContextNode>().Add("Function", "The deepest thing in the machine.");

            var json = ContextTreeFactory.BuildJson(_root.transform);

            Assert.IsTrue(json.Contains("\"name\": \"L12\""), "the twelfth level is missing from the export");
            Assert.IsTrue(
                json.Contains("\"scenePath\": \"Root/L1/L2/L3/L4/L5/L6/L7/L8/L9/L10/L11/L12\""),
                "the deepest node's scenePath is missing or truncated");
            Assert.IsTrue(
                json.Contains("\"value\": \"The deepest thing in the machine.\""),
                "the deepest node's entries were dropped");
        }

        [Test]
        public void BuildJson_EscapesWhatAnAuthorTypesIntoAnEntry()
        {
            // Context is prose a human writes in the Inspector, so quotes, backslashes and newlines
            // all reach the exporter. Unescaped, any one of them produces a file nothing can parse.
            var node = Child(_root, "P_Reader").AddComponent<ContextNode>();
            const string prose = "Reads the \"DMC\".\nPath C:\\plc\\main.\tRetries: 3.";
            node.Add("Function", prose);

            var json = ContextTreeFactory.BuildJson(_root.transform);
            var parsed = JsonUtility.FromJson<ExportRootMirror>(json);

            Assert.IsFalse(json.Contains("\nPath"), "the newline reached the file raw and broke the document");
            Assert.AreEqual(prose, parsed.root.children[0].entries[0].value);
        }

        [Test]
        public void BuildJson_WritesEmptyListsAsEmptyArrays()
        {
            var json = ContextTreeFactory.BuildJson(_root.transform);

            // A node with nothing in it still carries every field, so a consumer never has to test
            // for a missing key before iterating.
            Assert.IsTrue(json.Contains("\"components\": []"));
            Assert.IsTrue(json.Contains("\"entries\": []"));
            Assert.IsTrue(json.Contains("\"children\": []"));
        }

        [Test]
        public void BuildJson_CompactFormOmitsAllWhitespace()
        {
            Child(_root, "P_Reader").AddComponent<ContextNode>().Add("Function", "Reads the tag.");

            var json = ContextTreeFactory.BuildJson(_root.transform, prettyPrint: false);

            Assert.IsFalse(json.Contains("\n"));
            Assert.IsTrue(json.Contains("\"name\":\"P_Reader\""));
            Assert.AreEqual("P_Reader", JsonUtility.FromJson<ExportRootMirror>(json).root.children[0].name);
        }
    }
}
