using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PILAR.Context.Pipeline.Tests
{
    public class ContextTargetsTests : PipelineTestFixture
    {
        // ------------------------------------------------------------------ tiers

        [TestCase(ContextTargets.TierMachine, "machine")]
        [TestCase(ContextTargets.TierGroup, "group")]
        [TestCase(ContextTargets.TierAssembly, "assembly")]
        [TestCase(ContextTargets.TierDevice, "device")]
        [TestCase(ContextTargets.TierNone, "none")]
        [TestCase(99, "none")]
        public void TierName_MapsEveryTier(int tier, string expected)
        {
            Assert.AreEqual(expected, ContextTargets.TierName(tier));
        }

        [Test]
        public void GetTier_RootIsMachine()
        {
            BuildStandardFixture();
            Assert.AreEqual(ContextTargets.TierMachine,
                ContextTargets.GetTier(Root.transform, Root.transform));
        }

        [Test]
        public void GetTier_DirectChildOfRootIsGroup()
        {
            BuildStandardFixture();
            var fg1 = Root.transform.Find("FG_01");
            Assert.AreEqual(ContextTargets.TierGroup, ContextTargets.GetTier(fg1, Root.transform));
        }

        [Test]
        public void GetTier_DeviceIsDevice()
        {
            BuildStandardFixture();
            var sensor = Root.transform.Find("FG_01/Station/Sensor");
            Assert.AreEqual(ContextTargets.TierDevice, ContextTargets.GetTier(sensor, Root.transform));
        }

        [Test]
        public void GetTier_AncestorOfDeviceIsAssembly()
        {
            BuildStandardFixture();
            var station = Root.transform.Find("FG_01/Station");
            Assert.AreEqual(ContextTargets.TierAssembly, ContextTargets.GetTier(station, Root.transform));
        }

        [Test]
        public void GetTier_TransformWithNoDeviceBelowIsNone()
        {
            BuildStandardFixture();
            var loose = Root.transform.Find("FG_01/Loose");
            Assert.AreEqual(ContextTargets.TierNone, ContextTargets.GetTier(loose, Root.transform));
        }

        [Test]
        public void GetTier_DeviceContainingAnotherDeviceIsStillDevice()
        {
            // Devices nest - the self-is-device check must win over has-device-in-subtree, or an
            // outer device would be demoted to assembly.
            var fg = Child(Root.transform, "FG_01");
            var outer = Device(fg, "Outer");
            Device(outer, "Inner");

            Assert.AreEqual(ContextTargets.TierDevice, ContextTargets.GetTier(outer, Root.transform));
            Assert.AreEqual(ContextTargets.TierDevice,
                ContextTargets.GetTier(outer.Find("Inner"), Root.transform));
        }

        [Test]
        public void GetTier_GroupWinsOverDeviceForDirectChildOfRoot()
        {
            // Tier order is positional first: a device parked directly under the root is still the
            // group tier, because that check runs first.
            var direct = Device(Root.transform, "DirectDevice");
            Assert.AreEqual(ContextTargets.TierGroup, ContextTargets.GetTier(direct, Root.transform));
        }

        // ----------------------------------------------------------------- scopes

        [Test]
        public void Enumerate_ExcludesNonTargets()
        {
            BuildStandardFixture();
            var names = ContextTargets.Enumerate(Root.transform).Select(t => t.name).ToList();

            CollectionAssert.AreEquivalent(
                new[] { "Project", "FG_01", "FG_02", "Station", "Sensor" }, names);
            CollectionAssert.DoesNotContain(names, "Loose");
        }

        [Test]
        public void Enumerate_StructuralExcludesDevices()
        {
            BuildStandardFixture();
            var names = ContextTargets.Enumerate(Root.transform, "structural").Select(t => t.name).ToList();

            CollectionAssert.DoesNotContain(names, "Sensor");
            CollectionAssert.Contains(names, "Station");
        }

        [Test]
        public void Enumerate_DevicesOnlyReturnsDevices()
        {
            BuildStandardFixture();
            var names = ContextTargets.Enumerate(Root.transform, "devices").Select(t => t.name).ToList();
            CollectionAssert.AreEqual(new[] { "Sensor" }, names);
        }

        [Test]
        public void Enumerate_MissingCoversBothNoNodeAndEmptyNode()
        {
            BuildStandardFixture();
            var station = Root.transform.Find("FG_01/Station");
            var sensor = Root.transform.Find("FG_01/Station/Sensor");

            NodeWith(station, ("Function", "documented"));   // covered
            sensor.gameObject.AddComponent<ContextNode>();   // present but empty - still missing

            var names = ContextTargets.Enumerate(Root.transform, "missing").Select(t => t.name).ToList();

            CollectionAssert.DoesNotContain(names, "Station");
            CollectionAssert.Contains(names, "Sensor");
            CollectionAssert.Contains(names, "Project");
        }

        [Test]
        public void Enumerate_NullOrBlankScopeDefaultsToAll()
        {
            BuildStandardFixture();
            var all = ContextTargets.Enumerate(Root.transform, "all").Count();
            Assert.AreEqual(all, ContextTargets.Enumerate(Root.transform, null).Count());
            Assert.AreEqual(all, ContextTargets.Enumerate(Root.transform, "   ").Count());
        }

        [Test]
        public void Enumerate_ScopeIsCaseAndWhitespaceInsensitive()
        {
            BuildStandardFixture();
            var names = ContextTargets.Enumerate(Root.transform, "  DEVICES ").Select(t => t.name).ToList();
            CollectionAssert.AreEqual(new[] { "Sensor" }, names);
        }

        [Test]
        public void Enumerate_UnknownScopeThrows()
        {
            BuildStandardFixture();
            // Enumerate is lazy, so the throw only surfaces on iteration.
            var ex = Assert.Throws<ArgumentException>(
                () => ContextTargets.Enumerate(Root.transform, "bogus").ToList());
            StringAssert.Contains("bogus", ex.Message);
        }

        // ------------------------------------------------------------------ paths

        [Test]
        public void ScenePath_IsSlashDelimitedFromRootInclusive()
        {
            BuildStandardFixture();
            var sensor = Root.transform.Find("FG_01/Station/Sensor");
            Assert.AreEqual("Project/FG_01/Station/Sensor",
                ContextTargets.ScenePath(sensor, Root.transform));
        }

        [Test]
        public void ScenePath_OfRootIsJustTheRootName()
        {
            Assert.AreEqual("Project", ContextTargets.ScenePath(Root.transform, Root.transform));
        }

        [Test]
        public void RelativePath_IsEmptyForTheAncestorItself()
        {
            Assert.AreEqual(string.Empty,
                ContextTargets.RelativePath(Root.transform, Root.transform));
        }

        [Test]
        public void RelativePath_ExcludesTheAncestor()
        {
            BuildStandardFixture();
            var sensor = Root.transform.Find("FG_01/Station/Sensor");
            Assert.AreEqual("FG_01/Station/Sensor",
                ContextTargets.RelativePath(sensor, Root.transform));
        }

        [Test]
        public void RelativePath_ThrowsWhenNotADescendant()
        {
            BuildStandardFixture();
            var stranger = new GameObject("Stranger");
            try
            {
                Assert.Throws<ArgumentException>(
                    () => ContextTargets.RelativePath(stranger.transform, Root.transform));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stranger);
            }
        }

        // --------------------------------------------------------------- resolving

        [Test]
        public void Resolve_ByScenePath()
        {
            BuildStandardFixture();
            var t = ContextTargets.Resolve("Project/FG_01/Station/Sensor", Root.transform);
            Assert.AreEqual("Sensor", t.name);
        }

        [Test]
        public void Resolve_ByTopologyPath()
        {
            BuildStandardFixture();
            var station = Root.transform.Find("FG_01/Station");
            var sensor = Root.transform.Find("FG_01/Station/Sensor");
            NodeWith(station);
            NodeWith(sensor);

            // FG_01 carries no node, so it is absent from the topology path even though the scene
            // path goes through it.
            Assert.AreSame(sensor, ContextTargets.Resolve("Station/Sensor", Root.transform));
        }

        [Test]
        public void Resolve_ByFrameworkPathFromMetadataIgnoringCase()
        {
            BuildStandardFixture();
            SetMetadata("Sensor", ("plcPath", "MAIN.FG_01.Sensor"));

            // Any metadata value is a handle, whatever key it arrived under — that is what keeps a
            // framework's own addressing working without ContextTargets knowing the framework.
            Assert.AreEqual("Sensor", ContextTargets.Resolve("MAIN.FG_01.Sensor", Root.transform).name);
            Assert.AreEqual("Sensor", ContextTargets.Resolve("main.fg_01.sensor", Root.transform).name);
        }

        [Test]
        public void Resolve_AmbiguousMetadataValueThrowsAndSuggestsAFullPath()
        {
            BuildStandardFixture();
            Provider.MetadataFunc = t => t.name == "FG_01" || t.name == "FG_02"
                ? new[] { new ContextEntry { key = "plcPath", value = "MAIN.Shared" } }
                : System.Array.Empty<ContextEntry>();

            var ex = Assert.Throws<ArgumentException>(
                () => ContextTargets.Resolve("MAIN.Shared", Root.transform));

            StringAssert.Contains("ambiguous", ex.Message);
            StringAssert.Contains("Project/FG_0", ex.Message);
        }

        [Test]
        public void Resolve_ByUniqueBareName()
        {
            BuildStandardFixture();
            Assert.AreEqual("Sensor", ContextTargets.Resolve("Sensor", Root.transform).name);
        }

        [Test]
        public void Resolve_TrimsTheTarget()
        {
            BuildStandardFixture();
            Assert.AreEqual("Sensor", ContextTargets.Resolve("  Sensor  ", Root.transform).name);
        }

        [Test]
        public void Resolve_AmbiguousNameThrowsAndSuggestsAFullPath()
        {
            var a = Child(Root.transform, "FG_01");
            var b = Child(Root.transform, "FG_02");
            Child(a, "Twin");
            Child(b, "Twin");

            var ex = Assert.Throws<ArgumentException>(
                () => ContextTargets.Resolve("Twin", Root.transform));

            StringAssert.Contains("ambiguous", ex.Message);
            // The message has to carry a usable path, or the caller cannot recover from it.
            StringAssert.Contains("Project/FG_0", ex.Message);
        }

        [Test]
        public void Resolve_UnknownTargetThrows()
        {
            BuildStandardFixture();
            var ex = Assert.Throws<ArgumentException>(
                () => ContextTargets.Resolve("NoSuchThing", Root.transform));
            StringAssert.Contains("NoSuchThing", ex.Message);
        }

        [Test]
        public void Resolve_BlankTargetThrows()
        {
            Assert.Throws<ArgumentException>(() => ContextTargets.Resolve("  ", Root.transform));
            Assert.Throws<ArgumentException>(() => ContextTargets.Resolve(null, Root.transform));
        }

        [Test]
        public void Resolve_PrefersScenePathOverBareName()
        {
            // A GameObject literally named like a path must not shadow the real path match.
            var fg = Child(Root.transform, "FG_01");
            var real = Child(fg, "Station");
            Child(Root.transform, "Project/FG_01/Station");

            Assert.AreSame(real, ContextTargets.Resolve("Project/FG_01/Station", Root.transform));
        }

        [Test]
        public void ResolveRoot_ThrowsWhenTheRootIsMissing()
        {
            var ex = Assert.Throws<ArgumentException>(() => ContextTargets.ResolveRoot("NoSuchRoot"));
            StringAssert.Contains("NoSuchRoot", ex.Message);
        }

        [Test]
        public void ResolveRoot_BlankFallsBackToTheDefaultRoot()
        {
            Assert.AreEqual("Project", ContextTargets.ResolveRoot(null).name);
            Assert.AreEqual("Project", ContextTargets.ResolveRoot("  ").name);
        }

        // --------------------------------------------------------------- describe

        [Test]
        public void Describe_MapsNodeState()
        {
            BuildStandardFixture();
            var station = Root.transform.Find("FG_01/Station");
            NodeWith(station, ("Function", "a"), ("Process", "b"));

            var info = ContextTargets.Describe(station, Root.transform);

            Assert.AreEqual("Station", info.name);
            Assert.AreEqual("Project/FG_01/Station", info.scenePath);
            Assert.AreEqual(ContextTargets.TierAssembly, info.tier);
            Assert.AreEqual("assembly", info.tierName);
            Assert.IsTrue(info.hasNode);
            Assert.AreEqual(2, info.entryCount);
            CollectionAssert.AreEqual(new[] { "Function", "Process" }, info.entryKeys);
        }

        [Test]
        public void Describe_ReportsAbsentNodeAsEmptyRatherThanNull()
        {
            BuildStandardFixture();
            var info = ContextTargets.Describe(Root.transform.Find("FG_02"), Root.transform);

            Assert.IsFalse(info.hasNode);
            Assert.AreEqual(0, info.entryCount);
            CollectionAssert.IsEmpty(info.entryKeys);
        }

        [Test]
        public void Describe_CarriesProviderMetadata()
        {
            BuildStandardFixture();
            SetMetadata("FG_01", ("plcPath", "MAIN.FG_01"), ("hierarchyRole", "group"));

            var fg1 = ContextTargets.Describe(Root.transform.Find("FG_01"), Root.transform);
            var fg2 = ContextTargets.Describe(Root.transform.Find("FG_02"), Root.transform);

            CollectionAssert.AreEqual(
                new[] { "plcPath=MAIN.FG_01", "hierarchyRole=group" },
                fg1.metadata.Select(e => $"{e.key}={e.value}").ToArray());
            CollectionAssert.IsEmpty(fg2.metadata);
        }

        [Test]
        public void Describe_TopologyPathIsEmptyWithoutANode()
        {
            BuildStandardFixture();
            var station = Root.transform.Find("FG_01/Station");

            Assert.AreEqual(string.Empty,
                ContextTargets.Describe(station, Root.transform).topologyPath);

            NodeWith(station);
            Assert.AreEqual("Station",
                ContextTargets.Describe(station, Root.transform).topologyPath);
        }

        [Test]
        public void Describe_PrefabAssetIsNullForPlainSceneObjects()
        {
            BuildStandardFixture();
            var info = ContextTargets.Describe(Root.transform.Find("FG_01"), Root.transform);
            Assert.IsNull(info.prefabAsset);
        }
    }
}
