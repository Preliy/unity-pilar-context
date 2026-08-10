using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PILAR.Context.Pipeline.Tests
{
    public class ContextPipelineCommandsTests : PipelineTestFixture
    {
        // ------------------------------------------------------------ context_tree

        [Test]
        public void ContextTree_FlatIsOrderedByScenePathOrdinal()
        {
            BuildStandardFixture();
            var result = (ContextTreeFlatResult)ContextPipelineCommands.ContextTree(flat: true);

            Assert.AreEqual("Project", result.root);
            Assert.AreEqual("all", result.scope);
            Assert.AreEqual(result.targets.Count, result.count);

            var paths = result.targets.Select(t => t.scenePath).ToArray();
            CollectionAssert.AreEqual(paths.OrderBy(p => p, StringComparer.Ordinal).ToArray(), paths);
        }

        [Test]
        public void ContextTree_FlatRespectsScope()
        {
            BuildStandardFixture();
            var result = (ContextTreeFlatResult)ContextPipelineCommands.ContextTree(
                scope: "devices", flat: true);

            Assert.AreEqual("devices", result.scope);
            CollectionAssert.AreEqual(new[] { "Sensor" }, result.targets.Select(t => t.name).ToArray());
        }

        [Test]
        public void ContextTree_NestedKeepsBranchesThatLeadToAMatch()
        {
            BuildStandardFixture();
            var result = (ContextTreeNestedResult)ContextPipelineCommands.ContextTree(scope: "devices");

            // Only Sensor matches, but FG_01/Station must survive as the path to it - while FG_02,
            // which leads nowhere, must be pruned.
            var top = result.tree.children.Select(c => c.name).ToArray();
            CollectionAssert.Contains(top, "FG_01");
            CollectionAssert.DoesNotContain(top, "FG_02");

            var station = result.tree.children.Single(c => c.name == "FG_01").children.Single();
            Assert.AreEqual("Station", station.name);
            Assert.AreEqual("Sensor", station.children.Single().name);
        }

        [Test]
        public void ContextTree_DepthTruncates()
        {
            BuildStandardFixture();
            var result = (ContextTreeNestedResult)ContextPipelineCommands.ContextTree(depth: 1);

            Assert.IsNotEmpty(result.tree.children);
            // Depth 1 means the root's children are the last level expanded.
            foreach (var child in result.tree.children) CollectionAssert.IsEmpty(child.children);
        }

        [Test]
        public void ContextTree_UnknownRootThrows()
        {
            BuildStandardFixture();
            Assert.Throws<ArgumentException>(() => ContextPipelineCommands.ContextTree(root: "Nope"));
        }

        // ------------------------------------------------------------- context_get

        [Test]
        public void ContextGet_ReturnsEntriesInAuthorOrder()
        {
            BuildStandardFixture();
            NodeWith(Root.transform.Find("FG_01/Station"), ("Function", "one"), ("Process", "two"));

            var result = (ContextGetResult)ContextPipelineCommands.ContextGet("Project/FG_01/Station");

            Assert.AreEqual("Station", result.name);
            Assert.IsTrue(result.hasNode);
            CollectionAssert.AreEqual(new[] { "Function", "Process" }, result.entries.Select(e => e.key).ToArray());
            CollectionAssert.AreEqual(new[] { "one", "two" }, result.entries.Select(e => e.value).ToArray());
        }

        [Test]
        public void ContextGet_ReturnsEmptyEntriesWhenThereIsNoNode()
        {
            BuildStandardFixture();
            var result = (ContextGetResult)ContextPipelineCommands.ContextGet("FG_02");

            Assert.IsFalse(result.hasNode);
            CollectionAssert.IsEmpty(result.entries);
        }

        [Test]
        public void ContextGet_UnknownTargetThrows()
        {
            BuildStandardFixture();
            Assert.Throws<ArgumentException>(() => ContextPipelineCommands.ContextGet("Ghost"));
        }

        // ----------------------------------------------------------- context_audit

        [Test]
        public void ContextAudit_CountsCoverageNotJustPresence()
        {
            BuildStandardFixture();
            var station = Root.transform.Find("FG_01/Station");
            var sensor = Root.transform.Find("FG_01/Station/Sensor");

            NodeWith(station, ("Function", "documented"));
            sensor.gameObject.AddComponent<ContextNode>();   // present but empty

            var result = (ContextAuditResult)ContextPipelineCommands.ContextAudit();

            Assert.AreEqual(5, result.total);
            Assert.AreEqual(2, result.withNode);
            Assert.AreEqual(1, result.nonEmpty, "an empty node is presence, not coverage");
        }

        [Test]
        public void ContextAudit_ListsMissingAndEmptySeparately()
        {
            BuildStandardFixture();
            NodeWith(Root.transform.Find("FG_01/Station"), ("Function", "documented"));
            Root.transform.Find("FG_01/Station/Sensor").gameObject.AddComponent<ContextNode>();

            var result = (ContextAuditResult)ContextPipelineCommands.ContextAudit();

            CollectionAssert.Contains(result.emptyNode, "Project/FG_01/Station/Sensor");
            CollectionAssert.DoesNotContain(result.missingNode, "Project/FG_01/Station/Sensor");
            CollectionAssert.Contains(result.missingNode, "Project/FG_02");
        }

        [Test]
        public void ContextAudit_GroupsByTier()
        {
            BuildStandardFixture();
            var result = (ContextAuditResult)ContextPipelineCommands.ContextAudit();

            var tiers = result.byTier.Select(t => t.tierName).ToArray();
            CollectionAssert.AreEqual(new[] { "machine", "group", "assembly", "device" }, tiers);
            Assert.AreEqual(2, result.byTier.Single(t => t.tierName == "group").total);
            Assert.AreEqual(1, result.byTier.Single(t => t.tierName == "device").total);
        }

        [Test]
        public void ContextAudit_ReportsCoverageWithoutAnyFrameworkVocabulary()
        {
            BuildStandardFixture();
            SetMetadata("FG_01", ("hierarchyRole", "group"));

            var result = (ContextAuditResult)ContextPipelineCommands.ContextAudit();

            // The audit counts what is annotated and what is not. Anything a framework knows stays in
            // each target's metadata, which context_tree returns - the audit does not aggregate over
            // a vocabulary it does not own.
            Assert.AreEqual(5, result.total);
            Assert.AreEqual(0, result.withNode);
            Assert.AreEqual(0, result.nonEmpty);
            CollectionAssert.Contains(result.missingNode, "Project/FG_01");
        }

        [Test]
        public void ContextAudit_IsUnmovedByAFullSync()
        {
            BuildStandardFixture();
            foreach (var t in ContextTargets.Enumerate(Root.transform).ToList())
                NodeWith(t);
            SetMetadata(null, ("plcPath", "MAIN.Anything"));

            ContextPipelineCommands.ContextSync(dryRun: false);
            var result = (ContextAuditResult)ContextPipelineCommands.ContextAudit();

            // The trap this whole design has to avoid: a sync fills every node's list, and coverage
            // reads 100% while not one word has been written about the machine.
            Assert.AreEqual(5, result.withNode);
            Assert.AreEqual(0, result.nonEmpty, "synced metadata is not documentation");
            CollectionAssert.Contains(result.emptyNode, "Project/FG_01");
        }

        // ------------------------------------------------------------ context_sync

        [Test]
        public void ContextSync_DryRunReportsWithoutWriting()
        {
            BuildStandardFixture();
            var node = NodeWith(Root.transform.Find("FG_01"));
            SetMetadata("FG_01", ("plcPath", "MAIN.FG_01"));

            var result = (ContextSyncResult)ContextPipelineCommands.ContextSync();

            Assert.IsTrue(result.dryRun, "dry run is the default - a sync must never surprise a caller");
            Assert.AreEqual(1, result.changed);
            Assert.AreEqual(1, result.added);
            CollectionAssert.AreEqual(new[] { "fake.plcPath" }, result.targets.Single().added);
            Assert.AreEqual("Project/FG_01", result.targets.Single().scenePath);
            CollectionAssert.IsEmpty(node.Entries);
        }

        [Test]
        public void ContextSync_AppliedRunWrites()
        {
            BuildStandardFixture();
            var node = NodeWith(Root.transform.Find("FG_01"), ("Function", "Feeds the press."));
            SetMetadata("FG_01", ("plcPath", "MAIN.FG_01"));

            ContextPipelineCommands.ContextSync(dryRun: false);

            Assert.IsTrue(node.TryGetValue("fake.plcPath", out var written));
            Assert.AreEqual("MAIN.FG_01", written);
            Assert.IsTrue(node.TryGetValue("Function", out _), "authored entries survive a sync");
        }

        [Test]
        public void ContextSync_ReportsNothingOnceInStep()
        {
            BuildStandardFixture();
            NodeWith(Root.transform.Find("FG_01"));
            SetMetadata("FG_01", ("plcPath", "MAIN.FG_01"));
            ContextPipelineCommands.ContextSync(dryRun: false);

            var result = (ContextSyncResult)ContextPipelineCommands.ContextSync();

            Assert.AreEqual(0, result.changed);
            CollectionAssert.IsEmpty(result.targets);
        }

        [Test]
        public void ContextSync_ReportsAKeyWhoseFactDisappeared()
        {
            BuildStandardFixture();
            NodeWith(Root.transform.Find("FG_01"));
            SetMetadata("FG_01", ("plcPath", "MAIN.FG_01"), ("hierarchyRole", "group"));
            ContextPipelineCommands.ContextSync(dryRun: false);

            SetMetadata("FG_01", ("plcPath", "MAIN.FG_01"));
            var result = (ContextSyncResult)ContextPipelineCommands.ContextSync();

            Assert.AreEqual(1, result.removed);
            CollectionAssert.AreEqual(new[] { "fake.hierarchyRole" }, result.targets.Single().removed);
        }

        [Test]
        public void ContextSync_SkipsTargetsWithNoNode()
        {
            BuildStandardFixture();
            SetMetadata(null, ("plcPath", "MAIN.Anything"));

            var result = (ContextSyncResult)ContextPipelineCommands.ContextSync();

            // Sync fills nodes; it does not create them. context_ensure is the command that does.
            Assert.AreEqual(0, result.scanned);
            Assert.AreEqual(0, result.changed);
        }

        // ------------------------------------------------------------- context_set

        [Test]
        public void ContextSet_CreatesTheNodeWhenAbsent()
        {
            BuildStandardFixture();
            var result = (ContextSetResult)ContextPipelineCommands.ContextSet(
                "FG_02", key: "Function", value: "written");

            Assert.AreEqual("scene", result.wroteTo);
            CollectionAssert.AreEqual(new[] { "Function" }, result.applied);
            Assert.AreEqual(1, result.totalEntries);

            var node = Root.transform.Find("FG_02").GetComponent<ContextNode>();
            Assert.IsNotNull(node);
            Assert.IsTrue(node.TryGetValue("Function", out var v));
            Assert.AreEqual("written", v);
        }

        [Test]
        public void ContextSet_IsAnUpsertThatPreservesUnmentionedKeys()
        {
            BuildStandardFixture();
            var station = Root.transform.Find("FG_01/Station");
            NodeWith(station, ("Keep", "original"), ("Function", "before"));

            ContextPipelineCommands.ContextSet("Project/FG_01/Station", key: "Function", value: "after");

            var node = station.GetComponent<ContextNode>();
            Assert.AreEqual(2, node.Entries.Count);
            node.TryGetValue("Keep", out var keep);
            node.TryGetValue("Function", out var fn);
            Assert.AreEqual("original", keep);
            Assert.AreEqual("after", fn);
        }

        [Test]
        public void ContextSet_AcceptsAnEntriesJsonArray()
        {
            BuildStandardFixture();
            var result = (ContextSetResult)ContextPipelineCommands.ContextSet(
                "FG_02",
                entries: "[{\"key\":\"A\",\"value\":\"1\"},{\"key\":\"B\",\"value\":\"2\"}]");

            CollectionAssert.AreEqual(new[] { "A", "B" }, result.applied);
            Assert.AreEqual(2, result.totalEntries);
        }

        [Test]
        public void ContextSet_EntriesArrayOverridesADuplicateKeyFromKeyValue()
        {
            BuildStandardFixture();
            var result = (ContextSetResult)ContextPipelineCommands.ContextSet(
                "FG_02", key: "A", value: "from-key",
                entries: "[{\"key\":\"A\",\"value\":\"from-entries\"}]");

            CollectionAssert.AreEqual(new[] { "A" }, result.applied);
            Root.transform.Find("FG_02").GetComponent<ContextNode>().TryGetValue("A", out var v);
            Assert.AreEqual("from-entries", v);
        }

        [Test]
        public void ContextSet_WithNothingToWriteThrows()
        {
            BuildStandardFixture();
            Assert.Throws<ArgumentException>(() => ContextPipelineCommands.ContextSet("FG_02"));
        }

        [Test]
        public void ContextSet_MalformedEntriesJsonThrows()
        {
            BuildStandardFixture();
            Assert.Throws<ArgumentException>(
                () => ContextPipelineCommands.ContextSet("FG_02", entries: "not json at all"));
        }

        [Test]
        public void ContextSet_RefusesAKeyAProviderOwns()
        {
            BuildStandardFixture();

            // Hand-writing a namespaced key survives only until the next sync reverts it, so failing
            // loudly beats letting the caller believe it stuck.
            var ex = Assert.Throws<ArgumentException>(
                () => ContextPipelineCommands.ContextSet("FG_02", key: "fake.plcPath", value: "MAIN.Mine"));

            StringAssert.Contains("context_sync", ex.Message);
            StringAssert.Contains("fake", ex.Message);
        }

        [Test]
        public void ContextSet_AllowsAnAuthoredDottedKey()
        {
            BuildStandardFixture();

            // "Motor.Speed" claims no installed namespace, so it is documentation like any other key.
            Assert.DoesNotThrow(
                () => ContextPipelineCommands.ContextSet("FG_02", key: "Motor.Speed", value: "1.4 m/s"));
        }

        // ---------------------------------------------------------- context_remove

        [Test]
        public void ContextRemove_RemovesAnExistingKey()
        {
            BuildStandardFixture();
            var station = Root.transform.Find("FG_01/Station");
            NodeWith(station, ("Gone", "x"), ("Kept", "y"));

            var result = (ContextRemoveResult)ContextPipelineCommands.ContextRemove(
                "Project/FG_01/Station", key: "Gone");

            Assert.IsTrue(result.removed);
            Assert.AreEqual("Gone", result.key);
            Assert.AreEqual(1, result.totalEntries);
            Assert.IsFalse(station.GetComponent<ContextNode>().ContainsKey("Gone"));
        }

        [Test]
        public void ContextRemove_ReportsFalseForAKeyThatWasNotThere()
        {
            BuildStandardFixture();
            NodeWith(Root.transform.Find("FG_01/Station"), ("Present", "x"));

            var result = (ContextRemoveResult)ContextPipelineCommands.ContextRemove(
                "Project/FG_01/Station", key: "Absent");

            Assert.IsFalse(result.removed);
            Assert.AreEqual(1, result.totalEntries);
        }

        [Test]
        public void ContextRemove_ReturnsTheSkippedShapeWhenThereIsNoNode()
        {
            BuildStandardFixture();
            var result = ContextPipelineCommands.ContextRemove("FG_02", key: "Anything");

            var skipped = (ContextRemoveSkippedResult)result;
            Assert.IsFalse(skipped.removed);
            StringAssert.Contains("No ContextNode", skipped.reason);
        }

        [Test]
        public void ContextRemove_RefusesAKeyAProviderOwns()
        {
            BuildStandardFixture();
            var node = NodeWith(Root.transform.Find("FG_01"));
            SetMetadata("FG_01", ("plcPath", "MAIN.FG_01"));
            ContextPipelineCommands.ContextSync(dryRun: false);

            // Deleting it by hand is undone by the next sync; removing the scene component that
            // produced it is the real edit.
            Assert.Throws<ArgumentException>(
                () => ContextPipelineCommands.ContextRemove("FG_01", "fake.plcPath"));
            Assert.IsTrue(node.ContainsKey("fake.plcPath"));
        }

        [Test]
        public void ContextRemove_BlankKeyThrows()
        {
            BuildStandardFixture();
            Assert.Throws<ArgumentException>(
                () => ContextPipelineCommands.ContextRemove("FG_02", key: "  "));
        }

        // ---------------------------------------------------------- context_ensure

        [Test]
        public void ContextEnsure_DefaultsToDryRunAndWritesNothing()
        {
            BuildStandardFixture();
            var result = (ContextEnsureResult)ContextPipelineCommands.ContextEnsure();

            Assert.IsTrue(result.dryRun, "dry run is the default, so a careless call cannot mutate");
            Assert.AreEqual(5, result.missingTotal);
            CollectionAssert.IsNotEmpty(result.addedScene);
            Assert.IsNull(Root.transform.Find("FG_02").GetComponent<ContextNode>());
        }

        [Test]
        public void ContextEnsure_AppliesWhenDryRunIsOff()
        {
            BuildStandardFixture();
            var result = (ContextEnsureResult)ContextPipelineCommands.ContextEnsure(dryRun: false);

            Assert.IsFalse(result.dryRun);
            foreach (var name in new[] { "FG_01", "FG_02" })
                Assert.IsNotNull(Root.transform.Find(name).GetComponent<ContextNode>(), name);
            Assert.IsNotNull(Root.GetComponent<ContextNode>());
        }

        [Test]
        public void ContextEnsure_SkipsTargetsThatAlreadyHaveANode()
        {
            BuildStandardFixture();
            NodeWith(Root.transform.Find("FG_02"));

            var result = (ContextEnsureResult)ContextPipelineCommands.ContextEnsure();

            Assert.AreEqual(4, result.missingTotal);
            CollectionAssert.DoesNotContain(result.addedScene, "Project/FG_02");
        }

        [Test]
        public void ContextEnsure_RespectsScope()
        {
            BuildStandardFixture();
            var result = (ContextEnsureResult)ContextPipelineCommands.ContextEnsure(scope: "devices");

            Assert.AreEqual("devices", result.scope);
            CollectionAssert.AreEqual(new[] { "Project/FG_01/Station/Sensor" }, result.addedScene);
        }

        [Test]
        public void ContextEnsure_NormalisesModeAndEchoesIt()
        {
            BuildStandardFixture();
            var result = (ContextEnsureResult)ContextPipelineCommands.ContextEnsure(mode: "  SCENE ");
            Assert.AreEqual("scene", result.mode);
        }

        [Test]
        public void ContextEnsure_UnknownModeThrows()
        {
            BuildStandardFixture();
            var ex = Assert.Throws<ArgumentException>(
                () => ContextPipelineCommands.ContextEnsure(mode: "sideways"));
            StringAssert.Contains("sideways", ex.Message);
        }
    }
}
